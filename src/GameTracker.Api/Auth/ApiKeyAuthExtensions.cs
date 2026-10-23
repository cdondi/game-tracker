using Microsoft.AspNetCore.Authentication;

namespace GameTracker.Api.Auth;

public static class ApiKeyAuthExtensions
{
    /// <summary>
    /// Registers API key authentication and authorization. Startup fails if auth is enabled without a key,
    /// so the app is never silently open or silently locked.
    /// </summary>
    public static IServiceCollection AddApiKeyAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiKeyAuthOptions>()
            .Bind(configuration.GetSection(ApiKeyAuthOptions.SectionName))
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.ApiKey),
                "Auth:Enabled is true but Auth:ApiKey is not set. For local development, run: " +
                "dotnet user-secrets set \"Auth:ApiKey\" \"<your key>\" --project src/GameTracker.Api " +
                "(or set Auth:Enabled to false).")
            .ValidateOnStart();

        services.AddAuthentication(ApiKeyAuthOptions.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthOptions.Scheme, null);
        services.AddAuthorization();

        return services;
    }
}
