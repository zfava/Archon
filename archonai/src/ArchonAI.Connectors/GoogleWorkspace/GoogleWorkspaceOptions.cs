namespace ArchonAI.Connectors.GoogleWorkspace;

public sealed class GoogleWorkspaceOptions
{
    public const string SectionName = "Connectors:GoogleWorkspace";

    public string OAuthTokenUrl { get; set; } = "https://oauth2.googleapis.com/token";
    public string GmailBaseUrl { get; set; } = "https://gmail.googleapis.com/gmail/v1";
    public string DocsBaseUrl { get; set; } = "https://docs.googleapis.com/v1";
    public string SheetsBaseUrl { get; set; } = "https://sheets.googleapis.com/v4";
    public string DriveBaseUrl { get; set; } = "https://www.googleapis.com/drive/v3";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string ServiceAccountEmail { get; set; } = string.Empty;
    public string ImpersonateUser { get; set; } = string.Empty;
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 1000;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public string[] RequiredRoles { get; set; } = ["Operator", "Admin"];
}
