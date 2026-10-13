using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;

namespace BrewHouse.Services;

/// <summary>
/// Authentik OIDC login for the admin pages (docs/01 ADR 125). The auction itself stays anonymous; only
/// pages carrying <c>[Authorize(Policy = AdminAuthentication.Policy)]</c> need a signed-in member of the
/// Authentik group <see cref="DefaultAdminGroup"/>. Authority, client id and client secret come from
/// configuration (<c>Authentik:*</c>, rendered into /etc/brewhouse/brewhouse.env by the Infisical Agent);
/// until all three exist the policy denies everyone instead of failing open.
/// </summary>
public static class AdminAuthentication
{
    public const string Policy = "BrewHouseAdmin";
    public const string DefaultAdminGroup = "brewhouse-admins";
    public const string GroupsClaim = "groups";
    public const string LoginPath = "/auth/login";
    public const string LogoutPath = "/auth/logout";

    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Authentik:Authority"])
        && !string.IsNullOrWhiteSpace(configuration["Authentik:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["Authentik:ClientSecret"]);

    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var configured = IsConfigured(configuration);
        var adminGroup = configuration["Authentik:AdminGroup"] is { Length: > 0 } g ? g : DefaultAdminGroup;

        var authentication = services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                if (configured)
                    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "brewhouse.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                // Unauthenticated/forbidden requests land on the public page, never on a login loop.
                options.LoginPath = "/";
                options.AccessDeniedPath = "/";
            });

        if (configured)
        {
            authentication.AddOpenIdConnect(options =>
            {
                options.Authority = configuration["Authentik:Authority"];
                options.ClientId = configuration["Authentik:ClientId"];
                options.ClientSecret = configuration["Authentik:ClientSecret"];
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                // Keep Authentik's claim names ("groups", "preferred_username") as they are.
                options.MapInboundClaims = false;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            });
        }

        services.AddAuthorizationBuilder().AddPolicy(Policy, policy =>
        {
            policy.RequireAuthenticatedUser();
            if (configured)
                policy.RequireClaim(GroupsClaim, adminGroup);
            else
                policy.RequireAssertion(_ => false);
        });
        services.AddCascadingAuthenticationState();
        return services;
    }

    public static void MapAdminAuthentication(this WebApplication app)
    {
        var configured = IsConfigured(app.Configuration);

        // GET /auth/login?returnUrl=/admin/dead-letters - starts the Authentik authorization-code flow.
        app.MapGet(LoginPath, (string? returnUrl) =>
            configured
                ? Results.Challenge(
                    new AuthenticationProperties { RedirectUri = SafeLocalUrl(returnUrl) },
                    [OpenIdConnectDefaults.AuthenticationScheme])
                : Results.NotFound());

        // GET /auth/logout - clears this app's cookie only (the Authentik session is left alone).
        app.MapGet(LogoutPath, async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        });
    }

    /// <summary>Only same-site relative paths are honoured as a post-login target (no open redirect).</summary>
    public static string SafeLocalUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl[0] == '/'
        && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
            ? returnUrl
            : "/";
}
