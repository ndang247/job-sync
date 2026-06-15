using Microsoft.AspNetCore.Mvc;

namespace web_api.Controllers;

[ApiController]
[Route("api/v1/mail-connect")]
public class MailConnectController : ControllerBase
{
    private const string ErrorCode = "PLATFORM_AUTH_REQUIRED";
    private const string ErrorMessage = "Platform authentication must be enabled before connecting Gmail.";

    [HttpGet("gmail/start")]
    public IActionResult GmailStart()
    {
        return PlatformAuthRequired();
    }

    [HttpGet("gmail/callback")]
    public IActionResult GmailCallback()
    {
        return PlatformAuthRequired();
    }

    private ObjectResult PlatformAuthRequired()
    {
        return StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new { code = ErrorCode, error = ErrorMessage });
    }
}
