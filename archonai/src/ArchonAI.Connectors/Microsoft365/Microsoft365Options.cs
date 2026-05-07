namespace ArchonAI.Connectors.Microsoft365;

public sealed class Microsoft365Options
{
    public const string SectionName = "Connectors:Microsoft365";

    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";
    public string OAuthTokenUrl { get; set; } = "https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "https://graph.microsoft.com/.default";
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 1000;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public string[] RequiredRoles { get; set; } = ["Operator", "Admin"];
}
