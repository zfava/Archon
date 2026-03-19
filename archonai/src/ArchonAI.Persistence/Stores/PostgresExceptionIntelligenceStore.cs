using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.ExceptionIntelligence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresExceptionIntelligenceStore : IExceptionIntelligenceService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly IEventBus _eventBus;
    private readonly ILogger<PostgresExceptionIntelligenceStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private string TableName => $"{_schema}.operational_exceptions";

    public PostgresExceptionIntelligenceStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresExceptionIntelligenceStore> logger,
        IEventBus eventBus)
    {
        _connectionString = options.Value.ConnectionString
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
        _eventBus = eventBus;
    }

    // ── Initialization ──────────────────────────────────────────────

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                CREATE SCHEMA IF NOT EXISTS {_schema};

                CREATE TABLE IF NOT EXISTS {TableName} (
                    id                          uuid PRIMARY KEY,
                    tenant_id                   uuid NOT NULL,
                    category                    int NOT NULL,
                    severity                    int NOT NULL,
                    title                       text NOT NULL,
                    description                 text NOT NULL,
                    domain                      text NOT NULL,
                    status                      int NOT NULL,
                    urgency                     double precision NOT NULL,
                    economic_impact_estimate     double precision NOT NULL,
                    confidence                  double precision NOT NULL,
                    escalation_level            int NOT NULL,
                    assigned_to                 text,
                    escalation_path             text,
                    linked_artifacts            jsonb NOT NULL DEFAULT '[]',
                    recommended_action          jsonb,
                    created_by                  text NOT NULL,
                    created_at_utc              timestamptz NOT NULL,
                    updated_at_utc              timestamptz NOT NULL,
                    acknowledged_at_utc         timestamptz,
                    resolved_at_utc             timestamptz
                );

                CREATE INDEX IF NOT EXISTS idx_opex_tenant_id  ON {TableName} (tenant_id);
                CREATE INDEX IF NOT EXISTS idx_opex_severity   ON {TableName} (severity);
                CREATE INDEX IF NOT EXISTS idx_opex_category   ON {TableName} (category);
                CREATE INDEX IF NOT EXISTS idx_opex_status     ON {TableName} (status);
                CREATE INDEX IF NOT EXISTS idx_opex_domain     ON {TableName} (domain);
            ";
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
            _logger.LogInformation("PostgresExceptionIntelligenceStore initialized (schema={Schema}).", _schema);
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── RaiseExceptionAsync ─────────────────────────────────────────

    public async Task<OperationalException> RaiseExceptionAsync(
        OperationalException exception, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {TableName}
                (id, tenant_id, category, severity, title, description, domain, status,
                 urgency, economic_impact_estimate, confidence, escalation_level,
                 assigned_to, escalation_path, linked_artifacts, recommended_action,
                 created_by, created_at_utc, updated_at_utc, acknowledged_at_utc, resolved_at_utc)
            VALUES
                (@id, @tenantId, @category, @severity, @title, @description, @domain, @status,
                 @urgency, @economicImpact, @confidence, @escalationLevel,
                 @assignedTo, @escalationPath, @linkedArtifacts::jsonb, @recommendedAction::jsonb,
                 @createdBy, @createdAtUtc, @updatedAtUtc, @acknowledgedAtUtc, @resolvedAtUtc)
        ", conn);

        AddParameters(cmd, exception);
        await cmd.ExecuteNonQueryAsync(ct);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "exception.raised", nameof(PostgresExceptionIntelligenceStore),
            exception.Id,
            new Dictionary<string, string>
            {
                ["exceptionId"] = exception.Id.ToString(),
                ["tenantId"] = exception.TenantId.ToString(),
                ["category"] = exception.Category.ToString(),
                ["severity"] = exception.Severity.ToString(),
                ["title"] = exception.Title,
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        _logger.LogWarning(
            "Exception raised: {ExceptionId} severity={Severity} category={Category} title={Title}",
            exception.Id, exception.Severity, exception.Category, exception.Title);

        return exception;
    }

    // ── GetExceptionAsync ───────────────────────────────────────────

    public async Task<OperationalException?> GetExceptionAsync(
        Guid exceptionId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {TableName} WHERE id = @id AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("id", exceptionId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapException(reader) : null;
    }

    // ── ListExceptionsAsync ─────────────────────────────────────────

    public async Task<IReadOnlyList<OperationalException>> ListExceptionsAsync(
        Guid tenantId,
        ExceptionSeverity? severity = null,
        ExceptionCategory? category = null,
        ExceptionStatus? status = null,
        string? domain = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {TableName} WHERE tenant_id = @tenantId";
        if (severity.HasValue) sql += " AND severity = @severity";
        if (category.HasValue) sql += " AND category = @category";
        if (status.HasValue) sql += " AND status = @status";
        if (domain is not null) sql += " AND LOWER(domain) = LOWER(@domain)";
        sql += " ORDER BY created_at_utc DESC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        if (severity.HasValue) cmd.Parameters.AddWithValue("severity", (int)severity.Value);
        if (category.HasValue) cmd.Parameters.AddWithValue("category", (int)category.Value);
        if (status.HasValue) cmd.Parameters.AddWithValue("status", (int)status.Value);
        if (domain is not null) cmd.Parameters.AddWithValue("domain", domain);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<OperationalException>();
        while (await reader.ReadAsync(ct))
            results.Add(MapException(reader));

        // Sort by priority score descending (matching in-memory behavior)
        results.Sort((a, b) => ComputePriorityScore(b).CompareTo(ComputePriorityScore(a)));
        return results;
    }

    // ── UpdateStatusAsync ───────────────────────────────────────────

    public async Task<OperationalException?> UpdateStatusAsync(
        Guid exceptionId, Guid tenantId,
        ExceptionStatus status, string? assignedTo = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var now = DateTimeOffset.UtcNow;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Build dynamic SET clause for conditional timestamp updates
        var setClauses = new List<string>
        {
            "status = @status",
            "updated_at_utc = @now",
        };

        if (assignedTo is not null)
            setClauses.Add("assigned_to = @assignedTo");

        // Set acknowledged timestamp if transitioning to Acknowledged
        if (status == ExceptionStatus.Acknowledged)
            setClauses.Add("acknowledged_at_utc = COALESCE(acknowledged_at_utc, @now)");

        // Set resolved timestamp if transitioning to Resolved
        if (status == ExceptionStatus.Resolved)
            setClauses.Add("resolved_at_utc = COALESCE(resolved_at_utc, @now)");

        var sql = $"UPDATE {TableName} SET {string.Join(", ", setClauses)} WHERE id = @id AND tenant_id = @tenantId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("status", (int)status);
        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("id", exceptionId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        if (assignedTo is not null)
            cmd.Parameters.AddWithValue("assignedTo", assignedTo);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows == 0) return null;

        return await GetExceptionAsync(exceptionId, tenantId, ct);
    }

    // ── SetRecommendedActionAsync ───────────────────────────────────

    public async Task<OperationalException?> SetRecommendedActionAsync(
        Guid exceptionId, Guid tenantId,
        RecommendedAction action, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {TableName}
            SET recommended_action = @action::jsonb,
                updated_at_utc = @now
            WHERE id = @id AND tenant_id = @tenantId
        ", conn);

        cmd.Parameters.AddWithValue("action", JsonSerializer.Serialize(action, JsonOpts));
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue("id", exceptionId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows == 0) return null;

        return await GetExceptionAsync(exceptionId, tenantId, ct);
    }

    // ── GetQueueSummaryAsync ────────────────────────────────────────

    public async Task<ExceptionQueueSummary> GetQueueSummaryAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Aggregate query for open exceptions (not Resolved or Dismissed)
        await using var cmd = new NpgsqlCommand($@"
            SELECT
                COUNT(*) AS total_open,
                COUNT(*) FILTER (WHERE severity = @critical) AS critical_count,
                COUNT(*) FILTER (WHERE severity = @high) AS high_count,
                COUNT(*) FILTER (WHERE severity = @warning) AS warning_count,
                COALESCE(SUM(economic_impact_estimate), 0) AS total_economic_exposure
            FROM {TableName}
            WHERE tenant_id = @tenantId
              AND status NOT IN (@resolved, @dismissed)
        ", conn);

        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("critical", (int)ExceptionSeverity.Critical);
        cmd.Parameters.AddWithValue("high", (int)ExceptionSeverity.High);
        cmd.Parameters.AddWithValue("warning", (int)ExceptionSeverity.Warning);
        cmd.Parameters.AddWithValue("resolved", (int)ExceptionStatus.Resolved);
        cmd.Parameters.AddWithValue("dismissed", (int)ExceptionStatus.Dismissed);

        int totalOpen = 0, critical = 0, high = 0, warning = 0;
        double totalEconomicExposure = 0;

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                totalOpen = reader.GetInt32(reader.GetOrdinal("total_open"));
                critical = reader.GetInt32(reader.GetOrdinal("critical_count"));
                high = reader.GetInt32(reader.GetOrdinal("high_count"));
                warning = reader.GetInt32(reader.GetOrdinal("warning_count"));
                totalEconomicExposure = reader.GetDouble(reader.GetOrdinal("total_economic_exposure"));
            }
        }

        // Category breakdown
        await using var catCmd = new NpgsqlCommand($@"
            SELECT category, COUNT(*) AS cnt
            FROM {TableName}
            WHERE tenant_id = @tenantId
              AND status NOT IN (@resolved, @dismissed)
            GROUP BY category
        ", conn);

        catCmd.Parameters.AddWithValue("tenantId", tenantId);
        catCmd.Parameters.AddWithValue("resolved", (int)ExceptionStatus.Resolved);
        catCmd.Parameters.AddWithValue("dismissed", (int)ExceptionStatus.Dismissed);

        var byCategory = new Dictionary<string, int>();
        await using (var catReader = await catCmd.ExecuteReaderAsync(ct))
        {
            while (await catReader.ReadAsync(ct))
            {
                var catValue = (ExceptionCategory)catReader.GetInt32(catReader.GetOrdinal("category"));
                var count = catReader.GetInt32(catReader.GetOrdinal("cnt"));
                byCategory[catValue.ToString()] = count;
            }
        }

        return new ExceptionQueueSummary(
            TotalOpen: totalOpen,
            Critical: critical,
            High: high,
            Warning: warning,
            TotalEconomicExposure: totalEconomicExposure,
            ByCategory: byCategory,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    // ── GetPrioritizedQueueAsync ────────────────────────────────────

    public async Task<IReadOnlyList<ExceptionPriorityScore>> GetPrioritizedQueueAsync(
        Guid tenantId, int limit = 20, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {TableName}
            WHERE tenant_id = @tenantId
              AND status NOT IN (@resolved, @dismissed)
        ", conn);

        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("resolved", (int)ExceptionStatus.Resolved);
        cmd.Parameters.AddWithValue("dismissed", (int)ExceptionStatus.Dismissed);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var exceptions = new List<OperationalException>();
        while (await reader.ReadAsync(ct))
            exceptions.Add(MapException(reader));

        var scored = exceptions
            .Select(e => new ExceptionPriorityScore(
                e.Id,
                ComputePriorityScore(e),
                FormatBreakdown(e)))
            .OrderByDescending(s => s.Score)
            .Take(limit)
            .ToList();

        return scored;
    }

    // ── Priority scoring ────────────────────────────────────────────
    // Composite: severity_weight x urgency x economic_factor x confidence x escalation_boost

    internal static double ComputePriorityScore(OperationalException ex)
    {
        double severityWeight = ex.Severity switch
        {
            ExceptionSeverity.Critical => 4.0,
            ExceptionSeverity.High => 3.0,
            ExceptionSeverity.Warning => 2.0,
            ExceptionSeverity.Info => 1.0,
            _ => 1.0,
        };

        double escalationBoost = ex.EscalationLevel switch
        {
            EscalationLevel.Executive => 1.5,
            EscalationLevel.Manager => 1.2,
            EscalationLevel.Operator => 1.0,
            EscalationLevel.None => 1.0,
            _ => 1.0,
        };

        var economicFactor = 1.0 + Math.Min(Math.Log10(Math.Max(ex.EconomicImpactEstimate, 1)), 6) / 6.0;

        return severityWeight
             * Math.Max(ex.Urgency, 0.1)
             * economicFactor
             * Math.Max(ex.Confidence, 0.1)
             * escalationBoost;
    }

    private static string FormatBreakdown(OperationalException ex)
    {
        return $"severity={ex.Severity}, urgency={ex.Urgency:F1}, " +
               $"economic=${ex.EconomicImpactEstimate:N0}, " +
               $"confidence={ex.Confidence:F1}, escalation={ex.EscalationLevel}";
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void AddParameters(NpgsqlCommand cmd, OperationalException e)
    {
        cmd.Parameters.AddWithValue("id", e.Id);
        cmd.Parameters.AddWithValue("tenantId", e.TenantId);
        cmd.Parameters.AddWithValue("category", (int)e.Category);
        cmd.Parameters.AddWithValue("severity", (int)e.Severity);
        cmd.Parameters.AddWithValue("title", e.Title);
        cmd.Parameters.AddWithValue("description", e.Description);
        cmd.Parameters.AddWithValue("domain", e.Domain);
        cmd.Parameters.AddWithValue("status", (int)e.Status);
        cmd.Parameters.AddWithValue("urgency", e.Urgency);
        cmd.Parameters.AddWithValue("economicImpact", e.EconomicImpactEstimate);
        cmd.Parameters.AddWithValue("confidence", e.Confidence);
        cmd.Parameters.AddWithValue("escalationLevel", (int)e.EscalationLevel);
        cmd.Parameters.AddWithValue("assignedTo", (object?)e.AssignedTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("escalationPath", (object?)e.EscalationPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("linkedArtifacts", JsonSerializer.Serialize(e.LinkedArtifacts, JsonOpts));
        cmd.Parameters.AddWithValue("recommendedAction", e.RecommendedAction is not null
            ? JsonSerializer.Serialize(e.RecommendedAction, JsonOpts)
            : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("createdBy", e.CreatedBy);
        cmd.Parameters.AddWithValue("createdAtUtc", e.CreatedAtUtc);
        cmd.Parameters.AddWithValue("updatedAtUtc", e.UpdatedAtUtc);
        cmd.Parameters.AddWithValue("acknowledgedAtUtc", (object?)e.AcknowledgedAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("resolvedAtUtc", (object?)e.ResolvedAtUtc ?? DBNull.Value);
    }

    private static OperationalException MapException(NpgsqlDataReader r)
    {
        var recommendedActionOrd = r.GetOrdinal("recommended_action");
        RecommendedAction? recommendedAction = r.IsDBNull(recommendedActionOrd)
            ? null
            : JsonSerializer.Deserialize<RecommendedAction>(r.GetString(recommendedActionOrd), JsonOpts);

        return new OperationalException(
            Id: r.GetGuid(r.GetOrdinal("id")),
            TenantId: r.GetGuid(r.GetOrdinal("tenant_id")),
            Category: (ExceptionCategory)r.GetInt32(r.GetOrdinal("category")),
            Severity: (ExceptionSeverity)r.GetInt32(r.GetOrdinal("severity")),
            Title: r.GetString(r.GetOrdinal("title")),
            Description: r.GetString(r.GetOrdinal("description")),
            Domain: r.GetString(r.GetOrdinal("domain")),
            Status: (ExceptionStatus)r.GetInt32(r.GetOrdinal("status")),
            Urgency: r.GetDouble(r.GetOrdinal("urgency")),
            EconomicImpactEstimate: r.GetDouble(r.GetOrdinal("economic_impact_estimate")),
            Confidence: r.GetDouble(r.GetOrdinal("confidence")),
            EscalationLevel: (EscalationLevel)r.GetInt32(r.GetOrdinal("escalation_level")),
            AssignedTo: r.IsDBNull(r.GetOrdinal("assigned_to")) ? null : r.GetString(r.GetOrdinal("assigned_to")),
            EscalationPath: r.IsDBNull(r.GetOrdinal("escalation_path")) ? null : r.GetString(r.GetOrdinal("escalation_path")),
            LinkedArtifacts: JsonSerializer.Deserialize<List<ExceptionArtifactLink>>(r.GetString(r.GetOrdinal("linked_artifacts")), JsonOpts) ?? new(),
            RecommendedAction: recommendedAction,
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at_utc")),
            UpdatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("updated_at_utc")),
            AcknowledgedAtUtc: r.IsDBNull(r.GetOrdinal("acknowledged_at_utc")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("acknowledged_at_utc")),
            ResolvedAtUtc: r.IsDBNull(r.GetOrdinal("resolved_at_utc")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("resolved_at_utc")));
    }
}
