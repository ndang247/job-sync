using api_contracts.Requests;
using api_contracts.Responses;
using core.Entities;
using core.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using web_api.Authentication;
using web_api.Interfaces;
using web_api.Options;

namespace web_api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    UserManager<User> userManager,
    IOneTimeCodeGenerator codeGenerator,
    IOneTimeCodeHasher codeHasher,
    IOneTimeCodeStore codeStore,
    IEmailSender emailSender,
    IAuthTokenService tokenService,
    IOptions<OtpOptions> otpOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string RefreshTokenCookieName = "job_sync_refresh";
    private const string Purpose = "email-login";
    private const string GenericMessage =
        "If the address can receive email, a verification code has been sent.";
    private readonly OtpOptions _otpOptions = otpOptions.Value;

    [HttpPost("otp/request")]
    [EnableRateLimiting("otp-request")]
    public async Task<IActionResult> RequestOtp(
        RequestOtpRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var code = codeGenerator.Generate();
        var codeHash = codeHasher.Hash(email, Purpose, code);
        var issueResult = codeStore.TryIssue(email, Purpose, codeHash);

        if (!issueResult.Succeeded)
        {
            var retryAfterSeconds = Math.Max(
                1,
                (int)Math.Ceiling(issueResult.RetryAfter!.Value.TotalSeconds));
            Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                code = "OTP_REQUEST_THROTTLED",
                error = "Please wait before requesting another code."
            });
        }

        try
        {
            await emailSender.SendOtpAsync(
                email,
                code,
                TimeSpan.FromSeconds(_otpOptions.ExpirationSeconds),
                cancellationToken);
        }
        catch (Exception exception)
        {
            codeStore.Remove(email, Purpose);
            logger.LogError(exception, "Failed to deliver an OTP email.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "OTP_DELIVERY_FAILED",
                error = "The verification code could not be delivered."
            });
        }

        return Accepted(new OtpRequestedResponse(
            GenericMessage,
            _otpOptions.ExpirationSeconds,
            _otpOptions.ResendCooldownSeconds));
    }

    [HttpPost("otp/verify")]
    public async Task<IActionResult> VerifyOtp(
        VerifyOtpRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var codeHash = codeHasher.Hash(email, Purpose, request.Code);

        if (codeStore.Verify(email, Purpose, codeHash) != OtpVerificationStatus.Succeeded)
        {
            return Unauthorized(new
            {
                code = "OTP_INVALID_OR_EXPIRED",
                error = "The verification code is invalid or expired."
            });
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = string.Empty,
                LastName = string.Empty
            };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                user = await userManager.FindByEmailAsync(email);
                if (user is null)
                {
                    return Conflict(new
                    {
                        code = "USER_CREATION_FAILED",
                        error = "The account could not be created."
                    });
                }
            }
        }
        else if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return Conflict(new
                {
                    code = "USER_UPDATE_FAILED",
                    error = "The account could not be updated."
                });
            }
        }

        var tokens = await tokenService.IssueAsync(user, cancellationToken);
        SetRefreshTokenCookie(tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
        return Ok(ToClientResponse(tokens));
    }

    [HttpPost("token/refresh")]
    public async Task<IActionResult> Refresh(
        CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookieName, out var refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new
            {
                code = "REFRESH_TOKEN_INVALID",
                error = "The refresh token is invalid or expired."
            });
        }

        var tokens = await tokenService.RefreshAsync(refreshToken, cancellationToken);
        if (tokens is null)
        {
            ClearRefreshTokenCookie();
            return Unauthorized(new
            {
                code = "REFRESH_TOKEN_INVALID",
                error = "The refresh token is invalid or expired."
            });
        }

        SetRefreshTokenCookie(tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
        return Ok(ToClientResponse(tokens));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(RefreshTokenCookieName, out var refreshToken) &&
            !string.IsNullOrWhiteSpace(refreshToken))
        {
            await tokenService.RevokeFamilyAsync(refreshToken, cancellationToken);
        }

        ClearRefreshTokenCookie();
        return NoContent();
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private void SetRefreshTokenCookie(
        string refreshToken,
        DateTimeOffset expiresAt)
    {
        Response.Cookies.Append(
            RefreshTokenCookieName,
            refreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = ShouldUseSecureCookie(),
                SameSite = SameSiteMode.Lax,
                Expires = expiresAt,
                Path = "/api/v1/auth"
            });
    }

    private void ClearRefreshTokenCookie()
    {
        Response.Cookies.Delete(
            RefreshTokenCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = ShouldUseSecureCookie(),
                SameSite = SameSiteMode.Lax,
                Path = "/api/v1/auth"
            });
    }

    private static object ToClientResponse(TokenResponse tokens) => new
    {
        tokens.TokenType,
        tokens.AccessToken,
        tokens.ExpiresInSeconds,
        tokens.RefreshTokenExpiresAt
    };

    private bool ShouldUseSecureCookie()
    {
        var environment = HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        return !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
    }
}
