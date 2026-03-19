using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Decisions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresDecisionStore : IDecisionService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly IEventBus _eventBus;
    private readonly ILogger<PostgresDecisionStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PostgresDecisionStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresDecisionStore> logger,
        IEventBus eventBus)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
        _eventBus = eventBus;
    }

    private string DecisionsTable => $"{_schema}.decisions";
    private string LifecycleTable => $"{_schema}.decision_lifecycle_events";

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

                CREATE TABLE IF NOT EXISTS {DecisionsTable} (
                    id                    uuid PRIMARY KEY,
                    tenant_id             uuid NOT NULL,
                    title                 text NOT NULL,
                    domain                text NOT NULL,
                    objective             text NOT NULL,
                    constraints           jsonb NOT NULL DEFAULT '[]',
                    assumptions           jsonb NOT NULL DEFAULT '[]',
                    alternatives          jsonb NOT NULL DEFAULT '[]',
                    recommended_option_id text NOT NULL,
                    confidence            double precision NOT NULL,
                    reversibility         int NOT NULL,
                    risk_level            int NOT NULL,
                    expected_value        numeric,
                    requires_approval     bool NOT NULL,
                    linked_artifacts      jsonb NOT NULL DEFAULT '[]',
                    status                int NOT NULL,
                    created_by            text NOT NULL,
                    created_at_utc        timestamptz NOT NULL,
                    updated_at_utc        timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_decisions_tenant_id ON {DecisionsTable} (tenant_id);
                CREATE INDEX IF NOT EXISTS idx_decisions_domain    ON {DecisionsTable} (domain);
                CREATE INDEX IF NOT EXISTS idx_decisions_status    ON {DecisionsTable} (status);

                CREATE TABLE IF NOT EXISTS {LifecycleTable} (
                    id              uuid PRIMARY KEY,
                    decision_id     uuid NOT NULL,
                    event_type      text NOT NULL,
                    actor           text NOT NULL,
                    detail          text,
                    occurred_at_utc timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_lifecycle_decision_id ON {LifecycleTable} (decision_id);
            ";
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
            _logger.LogInformation("PostgresDecisionStore initialized (schema={Schema}).", _schema);
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── CreateAsync ─────────────────────────────────────────────────

    public async Task<DecisionRecord> CreateAsync(DecisionRecord decision, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {DecisionsTable}
                (id, tenant_id, title, domain, objective, constraints, assumptions, alternatives,
                 recommended_option_id, confidence, reversibility, risk_level, expected_value,
                 requires_approval, linked_artifacts, status, created_by, created_at_utc, updated_at_utc)
            VALUES
                (@id, @tenantId, @title, @domain, @objective, @constraints::jsonb, @assumptions::jsonb,
                 @alternatives::jsonb, @recommendedOptionId, @confidence, @reversibility, @riskLevel,
                 @expectedValue, @requiresApproval, @linkedArtifacts::jsonb, @status, @createdBy,
                 @createdAtUtc, @updatedAtUtc)
        ", conn);

        cmd.Parameters.AddWithValue("id", decision.Id);
        cmd.Parameters.AddWithValue("tenantId", decision.TenantId);
        cmd.Parameters.AddWithValue("title", decision.Title);
        cmd.Parameters.AddWithValue("domain", decision.Domain);
        cmd.Parameters.AddWithValue("objective", decision.Objective);
        cmd.Parameters.AddWithValue("constraints", JsonSerializer.Serialize(decision.Constraints, JsonOpts));
        cmd.Parameters.AddWithValue("assumptions", JsonSerializer.Serialize(decision.Assumptions, JsonOpts));
        cmd.Parameters.AddWithValue("alternatives", JsonSerializer.Serialize(decision.Alternatives, JsonOpts));
        cmd.Parameters.AddWithValue("recommendedOptionId", decision.RecommendedOptionId);
        cmd.Parameters.AddWithValue("confidence", decision.Confidence);
        cmd.Parameters.AddWithValue("reversibility", (int)decision.Reversibility);
        cmd.Parameters.AddWithValue("riskLevel", (int)decision.RiskLevel);
        cmd.Parameters.AddWithValue("expectedValue", (object?)decision.ExpectedValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("requiresApproval", decision.RequiresApproval);
        cmd.Parameters.AddWithValue("linkedArtifacts", JsonSerializer.Serialize(decision.LinkedArtifacts, JsonOpts));
        cmd.Parameters.AddWithValue("status", (int)decision.Status);
        cmd.Parameters.AddWithValue("createdBy", decision.CreatedBy);
        cmd.Parameters.AddWithValue("createdAtUtc", decision.CreatedAtUtc);
        cmd.Parameters.AddWithValue("updatedAtUtc", decision.UpdatedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "Decision.Created",
            nameof(PostgresDecisionStore),
            decision.Id,
            new Dictionary<string, string>
            {
                ["decisionId"] = decision.Id.ToString(),
                ["tenantId"] = decision.TenantId.ToString(),
                ["title"] = decision.Title,
            },
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Decision {DecisionId} created.", decision.Id);
        return decision;
    }

    // ── GetAsync ────────────────────────────────────────────────────

    public async Task<DecisionRecord?> GetAsync(Guid decisionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {DecisionsTable} WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", decisionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapDecision(reader) : null;
    }

    // ── ListAsync ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<DecisionRecord>> ListAsync(
        Guid tenantId, string? domain = null,
        DecisionStatus? status = null, int limit = 50,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {DecisionsTable} WHERE tenant_id = @tenantId";
        if (domain is not null) sql += " AND domain = @domain";
        if (status.HasValue) sql += " AND status = @status";
        sql += " ORDER BY created_at_utc DESC LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        if (domain is not null) cmd.Parameters.AddWithValue("domain", domain);
        if (status.HasValue) cmd.Parameters.AddWithValue("status", (int)status.Value);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<DecisionRecord>();
        while (await reader.ReadAsync(ct))
            results.Add(MapDecision(reader));
        return results;
    }

    // ── UpdateStatusAsync ───────────────────────────────────────────

    public async Task<DecisionRecord?> UpdateStatusAsync(
        Guid decisionId, DecisionStatus newStatus,
        string actor, string? detail = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var now = DateTimeOffset.UtcNow;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Update status
        await using (var cmd = new NpgsqlCommand($@"
            UPDATE {DecisionsTable}
            SET status = @status, updated_at_utc = @now
            WHERE id = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("status", (int)newStatus);
            cmd.Parameters.AddWithValue("now", now);
            cmd.Parameters.AddWithValue("id", decisionId);
            var rows = await cmd.ExecuteNonQueryAsync(ct);
            if (rows == 0) { await tx.RollbackAsync(ct); return null; }
        }

        // Insert lifecycle event
        await using (var cmd = new NpgsqlCommand($@"
            INSERT INTO {LifecycleTable}
                (id, decision_id, event_type, actor, detail, occurred_at_utc)
            VALUES (@eventId, @decisionId, @eventType, @actor, @detail, @occurredAt)", conn, tx))
        {
            cmd.Parameters.AddWithValue("eventId", Guid.NewGuid());
            cmd.Parameters.AddWithValue("decisionId", decisionId);
            cmd.Parameters.AddWithValue("eventType", $"StatusChanged:{newStatus}");
            cmd.Parameters.AddWithValue("actor", actor);
            cmd.Parameters.AddWithValue("detail", (object?)detail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("occurredAt", now);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        // Re-read updated record
        var record = await GetAsync(decisionId, ct);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "Decision.StatusUpdated",
            nameof(PostgresDecisionStore),
            decisionId,
            new Dictionary<string, string>
            {
                ["decisionId"] = decisionId.ToString(),
                ["newStatus"] = newStatus.ToString(),
                ["actor"] = actor,
            },
            DateTimeOffset.UtcNow), ct);

        return record;
    }

    // ── LinkArtifactAsync ───────────────────────────────────────────

    public async Task<DecisionRecord?> LinkArtifactAsync(
        Guid decisionId, DecisionLink link,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Append to the linked_artifacts jsonb array
        await using var cmd = new NpgsqlCommand($@"
            UPDATE {DecisionsTable}
            SET linked_artifacts = linked_artifacts || @link::jsonb,
                updated_at_utc = @now
            WHERE id = @id", conn);

        cmd.Parameters.AddWithValue("link", JsonSerializer.Serialize(new[] { link }, JsonOpts));
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue("id", decisionId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows == 0 ? null : await GetAsync(decisionId, ct);
    }

    // ── GetHistoryAsync ─────────────────────────────────────────────

    public async Task<IReadOnlyList<DecisionLifecycleEvent>> GetHistoryAsync(
        Guid decisionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {LifecycleTable}
            WHERE decision_id = @decisionId
            ORDER BY occurred_at_utc ASC", conn);
        cmd.Parameters.AddWithValue("decisionId", decisionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var events = new List<DecisionLifecycleEvent>();
        while (await reader.ReadAsync(ct))
        {
            events.Add(new DecisionLifecycleEvent(
                reader.GetGuid(reader.GetOrdinal("id")),
                reader.GetGuid(reader.GetOrdinal("decision_id")),
                reader.GetString(reader.GetOrdinal("event_type")),
                reader.GetString(reader.GetOrdinal("actor")),
                reader.IsDBNull(reader.GetOrdinal("detail")) ? null : reader.GetString(reader.GetOrdinal("detail")),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("occurred_at_utc"))));
        }
        return events;
    }

    // ── Row Mapper ──────────────────────────────────────────────────

    private static DecisionRecord MapDecision(NpgsqlDataReader reader)
    {
        return new DecisionRecord(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetGuid(reader.GetOrdinal("tenant_id")),
            reader.GetString(reader.GetOrdinal("title")),
            reader.GetString(reader.GetOrdinal("domain")),
            reader.GetString(reader.GetOrdinal("objective")),
            JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("constraints")), JsonOpts) ?? new(),
            JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("assumptions")), JsonOpts) ?? new(),
            JsonSerializer.Deserialize<List<DecisionAlternative>>(reader.GetString(reader.GetOrdinal("alternatives")), JsonOpts) ?? new(),
            reader.GetString(reader.GetOrdinal("recommended_option_id")),
            reader.GetDouble(reader.GetOrdinal("confidence")),
            (DecisionReversibility)reader.GetInt32(reader.GetOrdinal("reversibility")),
            (DecisionRiskLevel)reader.GetInt32(reader.GetOrdinal("risk_level")),
            reader.IsDBNull(reader.GetOrdinal("expected_value")) ? null : reader.GetDecimal(reader.GetOrdinal("expected_value")),
            reader.GetBoolean(reader.GetOrdinal("requires_approval")),
            JsonSerializer.Deserialize<List<DecisionLink>>(reader.GetString(reader.GetOrdinal("linked_artifacts")), JsonOpts) ?? new(),
            (DecisionStatus)reader.GetInt32(reader.GetOrdinal("status")),
            reader.GetString(reader.GetOrdinal("created_by")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at_utc")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at_utc")));
    }
}
