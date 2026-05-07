namespace ArchonAI.Connectors.Slack;

public sealed class SlackOptions
{
    public const string SectionName = "Connectors:Slack";

    public string BaseUrl { get; set; } = "https://slack.com/api";
    public string BotToken { get; set; } = string.Empty;
    public string SigningSecret { get; set; } = string.Empty;
    public string AppToken { get; set; } = string.Empty;
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 1000;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public string[] RequiredRoles { get; set; } = ["Operator", "Admin"];
    public string DefaultAlertChannel { get; set; } = string.Empty;
}
