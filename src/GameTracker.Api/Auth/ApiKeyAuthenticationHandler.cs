using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GameTracker.Api.Auth;

/// <summary>
/// Authenticates the owner by the X-Api-Key header. When auth is disabled, every request is the owner,
/// so the same authorization requirement passes and the v1 no-auth behavior is kept. The settings are
/// read per request, so they always reflect the current configuration.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    IOptionsMonitor<ApiKeyAuthOptions> apiKeyOptions,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var settings = apiKeyOptions.CurrentValue;
        if (!settings.Enabled)
        {
            return Task.FromResult(Success());
        }

        // Exactly one non-empty value; a repeated header is rejected rather than guessed at.
        if (!Request.Headers.TryGetValue(ApiKeyAuthOptions.HeaderName, out var values)
            || values.Count != 1
            || string.IsNullOrEmpty(values[0]))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        return Task.FromResult(KeysMatch(values[0]!, settings.ApiKey!)
            ? Success()
            : AuthenticateResult.Fail("Invalid API key."));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"{ApiKeyAuthOptions.Scheme} header=\"{ApiKeyAuthOptions.HeaderName}\"";
        return Task.CompletedTask;
    }

    /// <summary>
    /// Compares hashes in constant time, so neither the key's contents nor its length leak through timing.
    /// </summary>
    private static bool KeysMatch(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    private AuthenticateResult Success()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "owner")], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
