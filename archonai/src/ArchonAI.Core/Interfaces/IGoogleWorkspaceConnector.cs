namespace ArchonAI.Core.Interfaces;

public interface IGoogleWorkspaceConnector : IConnector
{
    global::System.Threading.Tasks.Task<GoogleWorkspaceAuthResult> AuthenticateAsync(CancellationToken cancellationToken = default);

    // Gmail
    global::System.Threading.Tasks.Task<IReadOnlyList<GmailMessage>> GetEmailsAsync(
        string? query,
        int maxResults = 20,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<GmailSendResult> SendEmailAsync(
        string to,
        string subject,
        string body,
        bool isHtml = false,
        CancellationToken cancellationToken = default);

    // Google Docs
    global::System.Threading.Tasks.Task<GoogleDocument> GetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<string> CreateDocumentAsync(
        string title,
        string? content,
        CancellationToken cancellationToken = default);

    // Google Sheets
    global::System.Threading.Tasks.Task<GoogleSheetData> ReadSpreadsheetAsync(
        string spreadsheetId,
        string range,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<int> WriteSpreadsheetAsync(
        string spreadsheetId,
        string range,
        IReadOnlyList<IReadOnlyList<string>> values,
        CancellationToken cancellationToken = default);

    // Google Drive
    global::System.Threading.Tasks.Task<IReadOnlyList<DriveFileInfo>> ListFilesAsync(
        string? query,
        int maxResults = 50,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<DriveFileInfo> GetFileMetadataAsync(
        string fileId,
        CancellationToken cancellationToken = default);

    GoogleWorkspaceConnectorStatus GetStatus();
}

public sealed record GoogleWorkspaceAuthResult(
    bool IsAuthenticated,
    string? Email,
    DateTimeOffset? ExpiresAtUtc,
    string? Error);

public sealed record GmailMessage(
    string Id,
    string ThreadId,
    string? From,
    string? To,
    string? Subject,
    string Snippet,
    DateTimeOffset ReceivedAtUtc);

public sealed record GmailSendResult(
    bool IsSuccess,
    string? MessageId,
    string? ThreadId,
    string? Error);

public sealed record GoogleDocument(
    string DocumentId,
    string Title,
    string? BodyText,
    DateTimeOffset RetrievedAtUtc);

public sealed record GoogleSheetData(
    string SpreadsheetId,
    string Range,
    IReadOnlyList<IReadOnlyList<string>> Values,
    int RowCount,
    int ColumnCount,
    DateTimeOffset RetrievedAtUtc);

public sealed record DriveFileInfo(
    string Id,
    string Name,
    string MimeType,
    long? SizeBytes,
    string? ParentId,
    DateTimeOffset? ModifiedAtUtc,
    DateTimeOffset RetrievedAtUtc);

public sealed record GoogleWorkspaceConnectorStatus(
    bool IsConnected,
    string? Email,
    DateTimeOffset? LastAuthenticatedAtUtc,
    DateTimeOffset? TokenExpiresAtUtc,
    long TotalRequests,
    long FailedRequests,
    long EmailsSent,
    long DocsAccessed,
    long SheetsAccessed,
    long DriveOps,
    int RateLimitRemaining,
    DateTimeOffset StatusAsOfUtc);
