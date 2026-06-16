using core.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Microsoft.IdentityModel.Tokens;

namespace web_api.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public IGoogleTokenExchanger MockTokenExchanger { get; } = Substitute.For<IGoogleTokenExchanger>();
    public ISyncJobChannel MockSyncJobChannel { get; } = Substitute.For<ISyncJobChannel>();
    public TestEmailSender EmailSender { get; } = new();

    private readonly string _dbName = $"TestDb-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Google:ClientId"] = "test-client-id",
                ["Google:ClientSecret"] = "test-client-secret",
                ["Google:RedirectUri"] = "http://localhost/api/v1/mail-connect/gmail/callback",
                ["OpenAI:ApiKey"] = "test-openai-key",
                ["OpenAI:Model"] = "gpt-4o-mini",
                ["FrontendUrl"] = "http://localhost:4200",
                ["Otp:Pepper"] = "test-otp-pepper-with-at-least-32-characters",
                ["Jwt:Issuer"] = "job-sync-tests",
                ["Jwt:Audience"] = "job-sync-test-clients",
                ["Jwt:SigningKey"] = "test-jwt-signing-key-with-at-least-32-characters",
                ["Smtp:Host"] = "smtp.example.test",
                ["Smtp:Port"] = "587",
                ["Smtp:UserName"] = "sender@example.test",
                ["Smtp:Password"] = "test-password",
                ["Smtp:SenderEmail"] = "sender@example.test",
                ["Smtp:SenderName"] = "Job Sync Tests",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove all EF Core / Npgsql registrations to avoid dual-provider conflict
            var efDescriptors = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true ||
                d.ServiceType.FullName?.Contains("Npgsql") == true ||
                d.ImplementationType?.FullName?.Contains("Npgsql") == true)
                .ToList();
            foreach (var d in efDescriptors) services.Remove(d);

            // Also remove the DbContext registration itself
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(AppDbContext));
            if (dbContextDescriptor != null) services.Remove(dbContextDescriptor);

            // Remove real hosted services (background worker)
            var hostedServices = services.Where(
                d => d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var svc in hostedServices) services.Remove(svc);

            // Use InMemory database
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            // Replace external dependencies with mocks
            ReplaceService<IGoogleTokenExchanger>(services, MockTokenExchanger);
            ReplaceService<ISyncJobChannel>(services, MockSyncJobChannel);
            ReplaceService<IEmailSender>(services, EmailSender);
        });
    }

    private static void ReplaceService<T>(IServiceCollection services, T mock) where T : class
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null) services.Remove(descriptor);
        services.AddSingleton(mock);
    }

    public AppDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    public HttpClient CreateAuthenticatedClient(
        Guid userId,
        string email = "test@example.com",
        bool allowAutoRedirect = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken(userId, email));
        return client;
    }

    public static string CreateAccessToken(Guid userId, string email = "test@example.com")
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("test-jwt-signing-key-with-at-least-32-characters"));
        var token = new JwtSecurityToken(
            "job-sync-tests",
            "job-sync-test-clients",
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ],
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(15),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
