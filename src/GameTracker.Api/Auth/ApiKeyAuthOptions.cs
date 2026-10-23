namespace GameTracker.Api.Auth;

/// <summary>
/// Settings for the single-owner API key, bound from the "Auth" configuration section. The key itself
/// comes from user secrets (or an environment variable) and is never committed.
/// </summary>
public class ApiKeyAuthOptions
{
    public const string SectionName = "Auth";
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    public bool Enabled { get; set; }

    public string? ApiKey { get; set; }
}
