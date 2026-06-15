using web_api.Hubs;
using web_api.Services;
using core.Entities;
using core.Interfaces;
using infrastructure.Data;
using infrastructure.Options;
using infrastructure.Services;
using worker;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using web_api.Authentication;
using web_api.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsEnvironment("Testing");
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<OtpOptions>()
    .Bind(builder.Configuration.GetSection(OtpOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.IdleTimeout = TimeSpan.FromMinutes(10);
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentityCore<User>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, configuredOptions) =>
    {
        var jwtOptions = configuredOptions.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "sub"
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs/sync"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
var otpRequestPermitLimit = builder.Environment.IsEnvironment("Testing") ? 1_000 : 5;
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("otp-request", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = otpRequestPermitLimit,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter =
            TimeSpan.FromMinutes(10).TotalSeconds.ToString("0");
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            code = "OTP_REQUEST_THROTTLED",
            error = "Too many verification code requests."
        }, cancellationToken);
    };
});

builder.Services.AddSingleton<IOneTimeCodeGenerator, OneTimeCodeGenerator>();
builder.Services.AddSingleton<IOneTimeCodeHasher, OneTimeCodeHasher>();
builder.Services.AddSingleton<IOneTimeCodeStore, MemoryOneTimeCodeStore>();
builder.Services.AddSingleton<IGoogleOAuthStateStore, GoogleOAuthStateStore>();
builder.Services.AddScoped<IAuthTokenService, AuthTokenService>();
builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();
builder.Services.AddScoped<IEmailService, GmailService>();
builder.Services.AddScoped<IAIService, OpenAIService>();
builder.Services.AddScoped<ISyncOrchestrator, SyncOrchestrator>();
builder.Services.AddScoped<IJobApplicationService, JobApplicationService>();
builder.Services.AddSingleton<IApplicationListCacheState, ApplicationListCacheState>();
builder.Services.AddScoped<ISyncProgressReporter, SyncProgressReporter>();
builder.Services.AddScoped<ISyncHubNotifier, SyncHubNotifier>();
builder.Services.AddScoped<IGoogleTokenExchanger, GoogleTokenExchanger>();
builder.Services.AddSingleton<ISyncJobChannel, SyncJobChannel>();
builder.Services.AddHostedService<SyncBackgroundService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("SignalRCors", policy =>
    {
        policy.WithOrigins(builder.Configuration["FrontendUrl"] ?? "http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("SignalRCors");
app.UseSession();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<SyncHub>("/hubs/sync");

app.Run();

// Make the implicit Program class public so test projects can access it
public partial class Program { }
