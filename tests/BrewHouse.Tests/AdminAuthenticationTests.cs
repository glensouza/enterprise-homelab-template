using System.Security.Claims;
using BrewHouse.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BrewHouse.Tests;

/// <summary>
/// The admin pages sit behind Authentik OIDC + the brewhouse-admins group (docs/01 ADR 125). These tests
/// pin the authorization decision itself - who gets in - without a broker, a database or an IdP.
/// </summary>
public class AdminAuthenticationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    private static IConfiguration Configured(string? adminGroup = null) => adminGroup is null
        ? Config(
            ("Authentik:Authority", "https://authentik.example/application/o/brewhouse/"),
            ("Authentik:ClientId", "client"),
            ("Authentik:ClientSecret", "secret"))
        : Config(
            ("Authentik:Authority", "https://authentik.example/application/o/brewhouse/"),
            ("Authentik:ClientId", "client"),
            ("Authentik:ClientSecret", "secret"),
            ("Authentik:AdminGroup", adminGroup));

    private static IAuthorizationService BuildAuthorization(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdminAuthentication(configuration);
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal SignedIn(params string[] groups)
    {
        var claims = new List<Claim> { new("preferred_username", "alice") };
        claims.AddRange(groups.Select(g => new Claim(AdminAuthentication.GroupsClaim, g)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void IsConfigured_Requires_Authority_ClientId_And_ClientSecret(bool authority, bool clientId, bool clientSecret, bool expected)
    {
        var configuration = Config(
            ("Authentik:Authority", authority ? "https://a/" : ""),
            ("Authentik:ClientId", clientId ? "id" : null),
            ("Authentik:ClientSecret", clientSecret ? "s" : " "));

        Assert.Equal(expected, AdminAuthentication.IsConfigured(configuration));
    }

    [Fact]
    public async Task Policy_Allows_A_Member_Of_The_Admin_Group()
    {
        var result = await BuildAuthorization(Configured())
            .AuthorizeAsync(SignedIn(AdminAuthentication.DefaultAdminGroup), null, AdminAuthentication.Policy);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Policy_Denies_A_Signed_In_User_Outside_The_Admin_Group()
    {
        var result = await BuildAuthorization(Configured())
            .AuthorizeAsync(SignedIn("authentik Admins", "someone-else"), null, AdminAuthentication.Policy);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Policy_Denies_An_Anonymous_Request()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await BuildAuthorization(Configured())
            .AuthorizeAsync(anonymous, null, AdminAuthentication.Policy);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Policy_Honours_A_Configured_Admin_Group_Name()
    {
        var authorization = BuildAuthorization(Configured("ops-team"));

        Assert.True((await authorization.AuthorizeAsync(SignedIn("ops-team"), null, AdminAuthentication.Policy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(SignedIn(AdminAuthentication.DefaultAdminGroup), null, AdminAuthentication.Policy)).Succeeded);
    }

    [Fact]
    public async Task Policy_Fails_Closed_When_Authentik_Is_Not_Configured()
    {
        // Even a principal that carries the admin group is refused: no OIDC settings means no trusted login.
        var result = await BuildAuthorization(Config())
            .AuthorizeAsync(SignedIn(AdminAuthentication.DefaultAdminGroup), null, AdminAuthentication.Policy);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("/admin/dead-letters", "/admin/dead-letters")]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("admin", "/")]
    public void SafeLocalUrl_Only_Allows_Same_Site_Paths(string? input, string expected)
    {
        Assert.Equal(expected, AdminAuthentication.SafeLocalUrl(input));
    }
}
