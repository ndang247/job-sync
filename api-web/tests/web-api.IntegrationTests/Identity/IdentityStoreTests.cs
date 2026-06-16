using System.Security.Claims;
using core.Entities;
using infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace web_api.IntegrationTests.Identity;

public class IdentityStoreTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public IdentityStoreTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UserManager_CreatesAndFindsPasswordlessUser()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "person@example.com",
            Email = "person@example.com",
            FirstName = "Test",
            LastName = "Person"
        };

        var result = await userManager.CreateAsync(user);
        var storedUser = await userManager.FindByEmailAsync("PERSON@example.com");

        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Description)));
        Assert.NotNull(storedUser);
        Assert.Equal(user.Id, storedUser.Id);
        Assert.Equal("PERSON@EXAMPLE.COM", storedUser.NormalizedUserName);
        Assert.Equal("PERSON@EXAMPLE.COM", storedUser.NormalizedEmail);
        Assert.NotEqual(default, storedUser.CreatedAt);
        Assert.Null(storedUser.PasswordHash);
    }

    [Fact]
    public async Task UserManager_PersistsClaimsLoginsAndTokens()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "+61412345678",
            PhoneNumber = "+61412345678",
            Email = "phone-user@example.com",
            FirstName = "Phone",
            LastName = "User"
        };

        var createResult = await userManager.CreateAsync(user);
        Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(error => error.Description)));

        var claim = new Claim("permission", "sync");
        var login = new UserLoginInfo("test-provider", "provider-key", "Test provider");
        Assert.True((await userManager.AddClaimAsync(user, claim)).Succeeded);
        Assert.True((await userManager.AddLoginAsync(user, login)).Succeeded);
        Assert.True((await userManager.SetAuthenticationTokenAsync(user, "test-provider", "refresh-token", "token-value")).Succeeded);

        var claims = await userManager.GetClaimsAsync(user);
        var foundByLogin = await userManager.FindByLoginAsync(login.LoginProvider, login.ProviderKey);
        var token = await userManager.GetAuthenticationTokenAsync(user, "test-provider", "refresh-token");

        Assert.Contains(claims, storedClaim => storedClaim.Type == claim.Type && storedClaim.Value == claim.Value);
        Assert.Equal(user.Id, foundByLogin?.Id);
        Assert.Equal("token-value", token);
    }

    [Fact]
    public void IdentityRoles_AreNotRegistered()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<RoleManager<IdentityRole<Guid>>>());
    }

    [Fact]
    public void UserOwnershipRelationships_AreRequiredAndCascadeOnDelete()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        AssertRequiredCascadeRelationship<EmailConnection>(dbContext);
        AssertRequiredCascadeRelationship<SyncJob>(dbContext);
        AssertRequiredCascadeRelationship<JobApplication>(dbContext);
    }

    [Fact]
    public void SyncJobEmailConnectionRelationship_IsRequiredAndCascadesOnDelete()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var foreignKey = dbContext.Model
            .FindEntityType(typeof(SyncJob))!
            .GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(EmailConnection));

        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    private static void AssertRequiredCascadeRelationship<TEntity>(AppDbContext dbContext)
    {
        var foreignKey = dbContext.Model
            .FindEntityType(typeof(TEntity))!
            .GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(User));

        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }
}
