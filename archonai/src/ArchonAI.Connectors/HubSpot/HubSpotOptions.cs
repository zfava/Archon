namespace ArchonAI.Connectors.HubSpot;

public sealed class HubSpotOptions
{
    public const string SectionName = "Connectors:HubSpot";

    public string BaseUrl { get; set; } = "https://api.hubapi.com";
    public string OAuthTokenUrl { get; set; } = "https://api.hubapi.com/oauth/v1/token";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 1000;
    public int RateLimitBufferPercent { get; set; } = 10;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public string[] RequiredRoles { get; set; } = ["Operator", "Admin"];
}
