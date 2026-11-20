using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Stria.Client.Security;

namespace Stria.Security;

/// <summary>
/// Who may call Stria. Browser operators: OpenID Connect (authorization-code flow) plus an
/// encrypted cookie. Automation/CLI: JWT bearer tokens for this API's audience. Data-plane
/// services: their own key, never an operator credential.
/// </summary>
public static class AuthenticationSetup
{
    public static IServiceCollection AddStriaAuthentication(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Pages use the cookie by default. Every API route names its schemes explicitly instead of
        // relying on a default (see Endpoints/ControlPlaneEndpoints.cs).
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddMicrosoftIdentityWebApp(configuration.GetSection("AzureAd"));

        services.AddAuthentication()
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAdApi"),
                JwtBearerDefaults.AuthenticationScheme);

        // Data plane: polling services authenticate with their own key.
        services.AddOptions<DataPlaneOptions>()
            .Bind(configuration.GetSection(DataPlaneOptions.SectionName))
            .Validate(DataPlaneKeys.IsValid,
                "DataPlane:ServiceKeyHashes entries must name a registered service and hold a 64-character hex SHA-256.")
            .ValidateOnStart();

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, DataPlaneAuthenticationHandler>(
                DataPlaneDefaults.AuthenticationScheme, configureOptions: null);

        services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.MapInboundClaims = false; // keep Entra's short claim names ("oid", "roles", "name")
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "roles";
            // An unauthenticated call to an /api route without explicit schemes must get 401, not a
            // redirect to the Microsoft sign-in page.
            var redirect = options.Events.OnRedirectToIdentityProvider;
            options.Events.OnRedirectToIdentityProvider = ctx =>
            {
                if (!IsApi(ctx.Request))
                    return redirect(ctx);
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.HandleResponse();
                return Task.CompletedTask;
            };
        });

        // Microsoft.Identity.Web installs its own JsonWebTokenHandler, and the MapInboundClaims setter
        // above only reaches the framework's default handlers. Turn mapping off on the handler that
        // actually validates the ID token; otherwise Entra's "roles" claim is renamed to ClaimTypes.Role,
        // RoleClaimType "roles" never matches, and every signed-in user is denied.
        services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            if (options.TokenHandler is JsonWebTokenHandler handler)
                handler.MapInboundClaims = false;
        });

        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "roles";
        });

        services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.AccessDeniedPath = "/access-denied";

            // API callers get status codes; only page requests are redirected.
            var toLogin = options.Events.OnRedirectToLogin;
            var toDenied = options.Events.OnRedirectToAccessDenied;
            options.Events.OnRedirectToLogin = ctx =>
                IsApi(ctx.Request) ? SetStatus(ctx.Response, StatusCodes.Status401Unauthorized) : toLogin(ctx);
            options.Events.OnRedirectToAccessDenied = ctx =>
                IsApi(ctx.Request) ? SetStatus(ctx.Response, StatusCodes.Status403Forbidden) : toDenied(ctx);
        });
        return services;
    }

    public static IServiceCollection AddStriaAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, OwnDirectiveHandler>();

        services.AddAuthorization(options =>
        {
            ChaosPolicies.AddPolicies(options);
            // Secure by default: an endpoint without its own authorization metadata still requires a
            // signed-in user. Public endpoints opt out explicitly with AllowAnonymous().
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy(DataPlaneDefaults.Policy, policy => policy
                .AddAuthenticationSchemes(DataPlaneDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new OwnDirectiveRequirement()));
        });
        return services;
    }

    private static bool IsApi(HttpRequest request) => request.Path.StartsWithSegments("/api");
    private static Task SetStatus(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
