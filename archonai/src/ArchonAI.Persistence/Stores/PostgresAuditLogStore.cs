using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.AuditLog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresAuditLogStore : IAuditLogService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresAuditLogStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private Guid? _lastEntryId;
    private string _latestChecksum = string.Empty;

    private string TableName => $"{_schema}.audit_log";

    public PostgresAuditLogStore(IOptions<PersistenceOptions> options, ILogger<PostgresAuditLogStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new InvalidOperationException("Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task<AuditEntry> RecordAsync(
        string eventType,
        string category,
        string source,
        string subjectId,
        string subjectType,
        string action,
        string resourceType,
        string resourceId,
        string description,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var entryId = Guid.NewGuid();
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var previousEntryId = _lastEntryId;
        var checksum = ComputeChecksum(entryId, eventType, category, source, subjectId, action, resourceType, resourceId, occurredAtUtc, _latestChecksum);
        var meta = metadata ?? new Dictionary<string, string>();

        var entry = new AuditEntry(
            entryId, eventType, category, source, subjectId, subjectType,
            action, resourceType, resourceId, description, meta,
            checksum, previousEntryId, occurredAtUtc);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {TableName}
            (id, event_type, category, source, subject_id, subject_type, action, resource_type, resource_id, description, metadata, checksum, previous_entry_id, occurred_at_utc)
            VALUES
            (@id, @eventType, @category, @source, @subjectId, @subjectType, @action, @resourceType, @resourceId, @description, @metadata::jsonb, @checksum, @previousEntryId, @occurredAtUtc);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", entryId);
        command.Parameters.AddWithValue("eventType", eventType);
        command.Parameters.AddWithValue("category", category);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("subjectId", subjectId);
        command.Parameters.AddWithValue("subjectType", subjectType);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("resourceType", resourceType);
        command.Parameters.AddWithValue("resourceId", resourceId);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(meta));
        command.Parameters.AddWithValue("checksum", checksum);
        command.Parameters.AddWithValue("previousEntryId", (object?)previousEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue("occurredAtUtc", occurredAtUtc.UtcDateTime);

        await command.ExecuteNonQueryAsync(ct);

        _lastEntryId = entryId;
        _latestChecksum = checksum;

        _logger.LogDebug("Audit entry {EntryId} recorded for {EventType}/{Category}", entryId, eventType, category);

        return entry;
    }

    public async global::System.Threading.Tasks.Task<AuditQueryResult> QueryAsync(
        string? category = null,
        string? subjectId = null,
        string? resourceType = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var whereClauses = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (category is not null)
        {
            whereClauses.Add("category = @category");
            parameters.Add(new NpgsqlParameter("category", category));
        }
        if (subjectId is not null)
        {
            whereClauses.Add("subject_id = @subjectId");
            parameters.Add(new NpgsqlParameter("subjectId", subjectId));
        }
        if (resourceType is not null)
        {
            whereClauses.Add("resource_type = @resourceType");
            parameters.Add(new NpgsqlParameter("resourceType", resourceType));
        }
        if (fromUtc.HasValue)
        {
            whereClauses.Add("occurred_at_utc >= @fromUtc");
            parameters.Add(new NpgsqlParameter("fromUtc", fromUtc.Value.UtcDateTime));
        }
        if (toUtc.HasValue)
        {
            whereClauses.Add("occurred_at_utc <= @toUtc");
            parameters.Add(new NpgsqlParameter("toUtc", toUtc.Value.UtcDateTime));
        }

        var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        // Count query
        var countSql = $"SELECT COUNT(*) FROM {TableName} {whereClause};";
        await using var countCmd = new NpgsqlCommand(countSql, connection);
        foreach (var p in parameters) countCmd.Parameters.Add(p.Clone());
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        // Data query
        var dataSql = $"""
            SELECT id, event_type, category, source, subject_id, subject_type, action, resource_type, resource_id, description, metadata, checksum, previous_entry_id, occurred_at_utc
            FROM {TableName}
            {whereClause}
            ORDER BY occurred_at_utc DESC
            OFFSET @offset LIMIT @limit;
            """;

        await using var dataCmd = new NpgsqlCommand(dataSql, connection);
        foreach (var p in parameters) dataCmd.Parameters.Add(p.Clone());
        dataCmd.Parameters.AddWithValue("offset", offset);
        dataCmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000));

        await using var reader = await dataCmd.ExecuteReaderAsync(ct);
        var entries = new List<AuditEntry>();
        while (await reader.ReadAsync(ct))
        {
            entries.Add(ReadAuditEntry(reader));
        }

        return new AuditQueryResult(entries, totalCount, offset + entries.Count < totalCount, DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<AuditEntry?> GetEntryAsync(Guid entryId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT id, event_type, category, source, subject_id, subject_type, action, resource_type, resource_id, description, metadata, checksum, previous_entry_id, occurred_at_utc
            FROM {TableName}
            WHERE id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", entryId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadAuditEntry(reader);
        }

        return null;
    }

    public async global::System.Threading.Tasks.Task<bool> VerifyIntegrityAsync(Guid? fromEntryId = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var whereClause = fromEntryId.HasValue
            ? "WHERE occurred_at_utc >= (SELECT occurred_at_utc FROM " + TableName + " WHERE id = @fromId)"
            : "";

        var sql = $"""
            SELECT id, event_type, category, source, subject_id, action, resource_type, resource_id, checksum, previous_entry_id, occurred_at_utc
            FROM {TableName}
            {whereClause}
            ORDER BY occurred_at_utc ASC;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        if (fromEntryId.HasValue)
        {
            command.Parameters.AddWithValue("fromId", fromEntryId.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        string previousChecksum = string.Empty;

        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(reader.GetOrdinal("id"));
            var eventType = reader.GetString(reader.GetOrdinal("event_type"));
            var category = reader.GetString(reader.GetOrdinal("category"));
            var source = reader.GetString(reader.GetOrdinal("source"));
            var subjectId = reader.GetString(reader.GetOrdinal("subject_id"));
            var action = reader.GetString(reader.GetOrdinal("action"));
            var resourceType = reader.GetString(reader.GetOrdinal("resource_type"));
            var resourceId = reader.GetString(reader.GetOrdinal("resource_id"));
            var storedChecksum = reader.GetString(reader.GetOrdinal("checksum"));
            var occurredAtUtc = new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("occurred_at_utc")), TimeSpan.Zero);

            var expectedChecksum = ComputeChecksum(id, eventType, category, source, subjectId, action, resourceType, resourceId, occurredAtUtc, previousChecksum);

            if (storedChecksum != expectedChecksum)
            {
                _logger.LogWarning("Integrity check failed at entry {EntryId}: expected {Expected}, got {Actual}", id, expectedChecksum, storedChecksum);
                return false;
            }

            previousChecksum = storedChecksum;
        }

        return true;
    }

    public AuditLogStatus GetStatus()
    {
        // This is synchronous per the interface; we run a blocking query.
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();

        long total = 0, agentAction = 0, workflowChange = 0, userActivity = 0;

        using (var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {TableName};", connection))
        {
            total = Convert.ToInt64(cmd.ExecuteScalar());
        }

        using (var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {TableName} WHERE category = 'agent_action';", connection))
        {
            agentAction = Convert.ToInt64(cmd.ExecuteScalar());
        }

        using (var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {TableName} WHERE category = 'workflow_change';", connection))
        {
            workflowChange = Convert.ToInt64(cmd.ExecuteScalar());
        }

        using (var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {TableName} WHERE category = 'user_activity';", connection))
        {
            userActivity = Convert.ToInt64(cmd.ExecuteScalar());
        }

        return new AuditLogStatus(
            IsActive: true,
            TotalEntries: total,
            AgentActionEntries: agentAction,
            WorkflowChangeEntries: workflowChange,
            UserActivityEntries: userActivity,
            LatestChecksum: _latestChecksum,
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    internal static string ComputeChecksum(
        Guid entryId, string eventType, string category, string source,
        string subjectId, string action, string resourceType, string resourceId,
        DateTimeOffset occurredAtUtc, string previousChecksum)
    {
        var payload = $"{entryId}|{eventType}|{category}|{source}|{subjectId}|{action}|{resourceType}|{resourceId}|{occurredAtUtc:O}|{previousChecksum}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static AuditEntry ReadAuditEntry(NpgsqlDataReader reader)
    {
        var previousEntryIdOrdinal = reader.GetOrdinal("previous_entry_id");
        Guid? previousEntryId = reader.IsDBNull(previousEntryIdOrdinal) ? null : reader.GetGuid(previousEntryIdOrdinal);

        var metadataJson = reader.GetString(reader.GetOrdinal("metadata"));
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? new Dictionary<string, string>();

        return new AuditEntry(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            EventType: reader.GetString(reader.GetOrdinal("event_type")),
            Category: reader.GetString(reader.GetOrdinal("category")),
            Source: reader.GetString(reader.GetOrdinal("source")),
            SubjectId: reader.GetString(reader.GetOrdinal("subject_id")),
            SubjectType: reader.GetString(reader.GetOrdinal("subject_type")),
            Action: reader.GetString(reader.GetOrdinal("action")),
            ResourceType: reader.GetString(reader.GetOrdinal("resource_type")),
            ResourceId: reader.GetString(reader.GetOrdinal("resource_id")),
            Description: reader.GetString(reader.GetOrdinal("description")),
            Metadata: metadata,
            Checksum: reader.GetString(reader.GetOrdinal("checksum")),
            PreviousEntryId: previousEntryId,
            OccurredAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("occurred_at_utc")), TimeSpan.Zero));
    }

    private async global::System.Threading.Tasks.Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var schemaSql = $"CREATE SCHEMA IF NOT EXISTS {_schema};";
            await using (var schemaCmd = new NpgsqlCommand(schemaSql, connection))
            {
                await schemaCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            var bootstrapSql = $$"""
                CREATE TABLE IF NOT EXISTS {{TableName}} (
                    id uuid PRIMARY KEY,
                    event_type text NOT NULL,
                    category text NOT NULL,
                    source text NOT NULL,
                    subject_id text NOT NULL,
                    subject_type text NOT NULL,
                    action text NOT NULL,
                    resource_type text NOT NULL,
                    resource_id text NOT NULL,
                    description text NOT NULL,
                    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
                    checksum text NOT NULL,
                    previous_entry_id uuid,
                    occurred_at_utc timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_audit_log_category ON {{TableName}}(category);
                CREATE INDEX IF NOT EXISTS idx_audit_log_subject_id ON {{TableName}}(subject_id);
                CREATE INDEX IF NOT EXISTS idx_audit_log_resource_type ON {{TableName}}(resource_type);
                CREATE INDEX IF NOT EXISTS idx_audit_log_occurred_at_utc ON {{TableName}}(occurred_at_utc DESC);
                """;

            await using var bootstrapCmd = new NpgsqlCommand(bootstrapSql, connection);
            await bootstrapCmd.ExecuteNonQueryAsync(cancellationToken);

            // Restore chain state from the latest entry
            var restoreSql = $"""
                SELECT id, checksum FROM {TableName}
                ORDER BY occurred_at_utc DESC
                LIMIT 1;
                """;
            await using var restoreCmd = new NpgsqlCommand(restoreSql, connection);
            await using var reader = await restoreCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                _lastEntryId = reader.GetGuid(0);
                _latestChecksum = reader.GetString(1);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
