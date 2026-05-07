namespace ArchonAI.Connectors.QuickBooks;

public sealed class QuickBooksOptions
{
    public const string SectionName = "Connectors:QuickBooks";

    public string BaseUrl { get; set; } = "https://quickbooks.api.intuit.com";
    public string OAuthTokenUrl { get; set; } = "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v3";
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 1000;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public string[] RequiredRoles { get; set; } = ["Operator", "Admin"];
}
