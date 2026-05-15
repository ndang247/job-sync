using core.Interfaces;
using infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace web_api.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public IGoogleTokenExchanger MockTokenExchanger { get; } = Substitute.For<IGoogleTokenExchanger>();
    public ISyncJobChannel MockSyncJobChannel { get; } = Substitute.For<ISyncJobChannel>();

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
                ["Google:GeminiApiKey"] = "test-gemini-key",
                ["FrontendUrl"] = "http://localhost:4200",
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
}
