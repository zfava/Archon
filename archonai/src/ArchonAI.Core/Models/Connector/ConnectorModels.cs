namespace ArchonAI.Core.Models.Connector;

// ── Error classification ─────────────────────────────────────────

public enum ConnectorErrorCategory
{
    None,
    Authentication,
    Authorization,
    RateLimit,
    Timeout,
    NetworkFailure,
    InvalidRequest,
    NotFound,
    Conflict,
    ServerError,
    ConfigurationError,
    CredentialExpired,
    Unknown
}

// ── Unified connector health ─────────────────────────────────────

public enum ConnectorHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown,
    NotConfigured
}

public sealed record ConnectorHealthReport(
    string ConnectorName,
    ConnectorHealthStatus Status,
    bool IsAuthenticated,
    DateTimeOffset? LastAuthenticatedAtUtc,
    DateTimeOffset? TokenExpiresAtUtc,
    bool CredentialExpiryWarning,
    long TotalRequests,
    long FailedRequests,
    double ErrorRate,
    int RateLimitRemaining,
    string? StatusMessage,
    DateTimeOffset CheckedAtUtc);

// ── Sync event tracking ──────────────────────────────────────────

public enum SyncDirection
{
    Inbound,
    Outbound,
    Bidirectional
}

public enum SyncStatus
{
    Started,
    Completed,
    Failed,
    PartialSuccess,
    Retrying
}

public sealed record SyncEvent(
    Guid Id,
    string ConnectorName,
    string OperationType,
    SyncDirection Direction,
    SyncStatus Status,
    int RecordsProcessed,
    int RecordsFailed,
    string? ErrorMessage,
    ConnectorErrorCategory ErrorCategory,
    string? CursorState,
    TimeSpan Duration,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

// ── Credential status ────────────────────────────────────────────

public sealed record CredentialStatus(
    string ConnectorName,
    bool IsConfigured,
    bool IsValid,
    DateTimeOffset? ExpiresAtUtc,
    bool ExpiryWarning,
    string? ValidationError,
    DateTimeOffset CheckedAtUtc);

// ── Integration dashboard ────────────────────────────────────────

public sealed record IntegrationDashboard(
    IReadOnlyList<ConnectorHealthReport> ConnectorHealth,
    IReadOnlyList<SyncEvent> RecentSyncs,
    IReadOnlyList<SyncEvent> FailedSyncs,
    IReadOnlyList<CredentialStatus> CredentialStatuses,
    int TotalConnectors,
    int HealthyConnectors,
    int DegradedConnectors,
    int UnhealthyConnectors,
    DateTimeOffset GeneratedAtUtc);

// ── Webhook event visibility ─────────────────────────────────────

public sealed record WebhookEvent(
    Guid Id,
    string ConnectorName,
    string EventType,
    bool SignatureValid,
    bool Processed,
    string? ErrorMessage,
    DateTimeOffset ReceivedAtUtc);
