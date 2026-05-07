namespace ArchonAI.Connectors.Salesforce;

public sealed class SalesforceOptions
{
    public const string SectionName = "Connectors:Salesforce";

    public string LoginUrl { get; set; } = "https://login.salesforce.com";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SecurityToken { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v59.0";
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 1000;
    public int RateLimitBufferPercent { get; set; } = 10;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public string[] RequiredRoles { get; set; } = ["Operator", "Admin"];
}
