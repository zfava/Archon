using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Governance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresTrustTierStore : ITrustTierService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresTrustTierStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Default tier when no policy exists for a scope — observe only.
    /// </summary>
    private const ExecutionTrustTier DefaultTier = ExecutionTrustTier.ObserveOnly;

    private string TableName => $"{_schema}.trust_tier_policies";

    public PostgresTrustTierStore(IOptions<PersistenceOptions> options, ILogger<PostgresTrustTierStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new InvalidOperationException("Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    public async Task<TrustTierEvaluation> EvaluateAsync(
        string tenantId, string actionScope, ExecutionTrustTier requestedTier,
        double? confidence = null, decimal? value = null, bool? reversible = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await EnsureTenantSeededAsync(tenantId, ct);

        var policy = await FindPolicyAsync(tenantId, actionScope, ct);
        var maxTier = policy?.MaxTier ?? DefaultTier;
        var effectiveTier = (ExecutionTrustTier)Math.Min((int)requestedTier, (int)maxTier);

        // Apply guardrails from the policy
        if (policy is not null)
        {
            // Confidence gate
            if (policy.ConfidenceThreshold.HasValue && confidence.HasValue
                && confidence.Value < policy.ConfidenceThreshold.Value
                && effectiveTier >= ExecutionTrustTier.AutoExecuteReversible)
            {
                effectiveTier = ExecutionTrustTier.DraftApprovalRequired;
            }

            // Value ceiling
            if (policy.ValueCeiling.HasValue && value.HasValue
                && value.Value > policy.ValueCeiling.Value
                && effectiveTier >= ExecutionTrustTier.AutoExecuteReversible)
            {
                effectiveTier = ExecutionTrustTier.DraftApprovalRequired;
            }

            // Reversibility gate
            if (policy.RequireReversible && reversible == false
                && effectiveTier >= ExecutionTrustTier.AutoExecuteReversible)
            {
                effectiveTier = ExecutionTrustTier.DraftApprovalRequired;
            }
        }

        var allowed = effectiveTier >= requestedTier;
        var disposition = DetermineDisposition(effectiveTier);
        string? reason = null;

        if (!allowed)
        {
            reason = $"Requested tier {requestedTier} exceeds maximum allowed tier {maxTier} for scope '{actionScope}'.";
            if (policy is not null)
            {
                if (policy.ConfidenceThreshold.HasValue && confidence.HasValue && confidence.Value < policy.ConfidenceThreshold.Value)
                    reason += $" Confidence {confidence:F2} below threshold {policy.ConfidenceThreshold:F2}.";
                if (policy.ValueCeiling.HasValue && value.HasValue && value.Value > policy.ValueCeiling.Value)
                    reason += $" Value ${value} exceeds ceiling ${policy.ValueCeiling}.";
                if (policy.RequireReversible && reversible == false)
                    reason += " Action is irreversible but policy requires reversibility.";
            }
        }

        var evaluation = new TrustTierEvaluation(
            actionScope, requestedTier, effectiveTier, allowed, disposition, reason);

        _logger.LogInformation(
            "Trust tier evaluation: scope={Scope} requested={Requested} effective={Effective} allowed={Allowed} disposition={Disposition}",
            actionScope, requestedTier, effectiveTier, allowed, disposition);

        return evaluation;
    }

    public async Task<ExecutionTrustTier> GetEffectiveTierAsync(
        string tenantId, string actionScope, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await EnsureTenantSeededAsync(tenantId, ct);

        var policy = await FindPolicyAsync(tenantId, actionScope, ct);
        return policy?.MaxTier ?? DefaultTier;
    }

    public async Task<IReadOnlyList<TrustTierPolicy>> ListPoliciesAsync(
        string tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await EnsureTenantSeededAsync(tenantId, ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT id, tenant_id, action_scope, max_tier, confidence_threshold, value_ceiling,
                   require_reversible, description, is_enabled, created_by, created_at_utc, updated_at_utc
            FROM {TableName}
            WHERE is_enabled = true AND tenant_id = @tenantId
            ORDER BY action_scope;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<TrustTierPolicy>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadTrustTierPolicy(reader));
        }

        return result;
    }

    public async Task<TrustTierPolicy> SetPolicyAsync(TrustTierPolicy policy, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Upsert: insert or update on conflict
        var sql = $"""
            INSERT INTO {TableName}
            (id, tenant_id, action_scope, max_tier, confidence_threshold, value_ceiling,
             require_reversible, description, is_enabled, created_by, created_at_utc, updated_at_utc)
            VALUES
            (@id, @tenantId, @actionScope, @maxTier, @confidenceThreshold, @valueCeiling,
             @requireReversible, @description, @isEnabled, @createdBy, @createdAtUtc, @updatedAtUtc)
            ON CONFLICT (id) DO UPDATE SET
                action_scope = EXCLUDED.action_scope,
                max_tier = EXCLUDED.max_tier,
                confidence_threshold = EXCLUDED.confidence_threshold,
                value_ceiling = EXCLUDED.value_ceiling,
                require_reversible = EXCLUDED.require_reversible,
                description = EXCLUDED.description,
                is_enabled = EXCLUDED.is_enabled,
                updated_at_utc = EXCLUDED.updated_at_utc;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", policy.Id);
        command.Parameters.AddWithValue("tenantId", policy.TenantId);
        command.Parameters.AddWithValue("actionScope", policy.ActionScope);
        command.Parameters.AddWithValue("maxTier", (int)policy.MaxTier);
        command.Parameters.AddWithValue("confidenceThreshold", (object?)policy.ConfidenceThreshold ?? DBNull.Value);
        command.Parameters.AddWithValue("valueCeiling", (object?)policy.ValueCeiling ?? DBNull.Value);
        command.Parameters.AddWithValue("requireReversible", policy.RequireReversible);
        command.Parameters.AddWithValue("description", (object?)policy.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("isEnabled", policy.IsEnabled);
        command.Parameters.AddWithValue("createdBy", policy.CreatedBy);
        command.Parameters.AddWithValue("createdAtUtc", policy.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("updatedAtUtc", policy.UpdatedAtUtc.UtcDateTime);

        await command.ExecuteNonQueryAsync(ct);

        _logger.LogInformation(
            "Trust tier policy set: {PolicyId} scope={Scope} maxTier={MaxTier} tenant={TenantId}",
            policy.Id, policy.ActionScope, policy.MaxTier, policy.TenantId);

        return policy;
    }

    public async Task<bool> DeletePolicyAsync(Guid policyId, string tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Enforce tenant isolation — cannot delete another tenant's policy
        var sql = $"DELETE FROM {TableName} WHERE id = @id AND tenant_id = @tenantId;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", policyId);
        command.Parameters.AddWithValue("tenantId", tenantId);

        var rowsAffected = await command.ExecuteNonQueryAsync(ct);
        return rowsAffected > 0;
    }

    public async Task<IReadOnlyDictionary<string, ExecutionTrustTier>> GetTierMapAsync(
        string tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await EnsureTenantSeededAsync(tenantId, ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT action_scope, max_tier
            FROM {TableName}
            WHERE is_enabled = true AND tenant_id = @tenantId
            ORDER BY action_scope;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var map = new Dictionary<string, ExecutionTrustTier>();
        while (await reader.ReadAsync(ct))
        {
            var scope = reader.GetString(0);
            var tier = (ExecutionTrustTier)reader.GetInt32(1);
            map[scope] = tier;
        }

        return map;
    }

    private async Task<TrustTierPolicy?> FindPolicyAsync(string tenantId, string actionScope, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT id, tenant_id, action_scope, max_tier, confidence_threshold, value_ceiling,
                   require_reversible, description, is_enabled, created_by, created_at_utc, updated_at_utc
            FROM {TableName}
            WHERE tenant_id = @tenantId AND action_scope = @actionScope AND is_enabled = true
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("actionScope", actionScope);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadTrustTierPolicy(reader);
        }

        return null;
    }

    private async Task EnsureTenantSeededAsync(string tenantId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var countSql = $"SELECT COUNT(*) FROM {TableName} WHERE tenant_id = @tenantId;";
        await using var countCmd = new NpgsqlCommand(countSql, connection);
        countCmd.Parameters.AddWithValue("tenantId", tenantId);
        var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));

        if (count > 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var defaults = new[]
        {
            new TrustTierPolicy(Guid.NewGuid(), tenantId, "workflow.execute",
                ExecutionTrustTier.AutoExecuteReversible, null, null, false,
                "Workflow execution — auto-execute reversible actions", true, "system", now, now),
            new TrustTierPolicy(Guid.NewGuid(), tenantId, "decision.execute",
                ExecutionTrustTier.DraftApprovalRequired, null, null, false,
                "Decision execution requires approval by default", true, "system", now, now),
            new TrustTierPolicy(Guid.NewGuid(), tenantId, "connector.send",
                ExecutionTrustTier.AutoExecuteReversible, null, null, false,
                "Connector send — auto-execute reversible actions", true, "system", now, now),
            new TrustTierPolicy(Guid.NewGuid(), tenantId, "data.read",
                ExecutionTrustTier.PolicyEnvelope, null, null, false,
                "Data reads operate within full policy envelope", true, "system", now, now),
            new TrustTierPolicy(Guid.NewGuid(), tenantId, "notification.send",
                ExecutionTrustTier.AutoExecuteHighConfidence, null, 1000m, false,
                "Notifications auto-execute with high confidence under $1K impact", true, "system", now, now),
        };

        foreach (var policy in defaults)
        {
            var sql = $"""
                INSERT INTO {TableName}
                (id, tenant_id, action_scope, max_tier, confidence_threshold, value_ceiling,
                 require_reversible, description, is_enabled, created_by, created_at_utc, updated_at_utc)
                VALUES
                (@id, @tenantId, @actionScope, @maxTier, @confidenceThreshold, @valueCeiling,
                 @requireReversible, @description, @isEnabled, @createdBy, @createdAtUtc, @updatedAtUtc);
                """;

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", policy.Id);
            command.Parameters.AddWithValue("tenantId", policy.TenantId);
            command.Parameters.AddWithValue("actionScope", policy.ActionScope);
            command.Parameters.AddWithValue("maxTier", (int)policy.MaxTier);
            command.Parameters.AddWithValue("confidenceThreshold", (object?)policy.ConfidenceThreshold ?? DBNull.Value);
            command.Parameters.AddWithValue("valueCeiling", (object?)policy.ValueCeiling ?? DBNull.Value);
            command.Parameters.AddWithValue("requireReversible", policy.RequireReversible);
            command.Parameters.AddWithValue("description", (object?)policy.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("isEnabled", policy.IsEnabled);
            command.Parameters.AddWithValue("createdBy", policy.CreatedBy);
            command.Parameters.AddWithValue("createdAtUtc", policy.CreatedAtUtc.UtcDateTime);
            command.Parameters.AddWithValue("updatedAtUtc", policy.UpdatedAtUtc.UtcDateTime);

            await command.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation("Seeded 5 default trust tier policies for tenant {TenantId}", tenantId);
    }

    private static TrustTierPolicy ReadTrustTierPolicy(NpgsqlDataReader reader)
    {
        var confidenceOrdinal = reader.GetOrdinal("confidence_threshold");
        var valueCeilingOrdinal = reader.GetOrdinal("value_ceiling");
        var descriptionOrdinal = reader.GetOrdinal("description");

        return new TrustTierPolicy(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            TenantId: reader.GetString(reader.GetOrdinal("tenant_id")),
            ActionScope: reader.GetString(reader.GetOrdinal("action_scope")),
            MaxTier: (ExecutionTrustTier)reader.GetInt32(reader.GetOrdinal("max_tier")),
            ConfidenceThreshold: reader.IsDBNull(confidenceOrdinal) ? null : reader.GetDouble(confidenceOrdinal),
            ValueCeiling: reader.IsDBNull(valueCeilingOrdinal) ? null : reader.GetDecimal(valueCeilingOrdinal),
            RequireReversible: reader.GetBoolean(reader.GetOrdinal("require_reversible")),
            Description: reader.IsDBNull(descriptionOrdinal) ? null : reader.GetString(descriptionOrdinal),
            IsEnabled: reader.GetBoolean(reader.GetOrdinal("is_enabled")),
            CreatedBy: reader.GetString(reader.GetOrdinal("created_by")),
            CreatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero),
            UpdatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("updated_at_utc")), TimeSpan.Zero));
    }

    private static string DetermineDisposition(ExecutionTrustTier tier) => tier switch
    {
        ExecutionTrustTier.ObserveOnly => TrustDisposition.Observe,
        ExecutionTrustTier.RecommendOnly => TrustDisposition.Recommend,
        ExecutionTrustTier.DraftApprovalRequired => TrustDisposition.DraftForApproval,
        >= ExecutionTrustTier.AutoExecuteReversible => TrustDisposition.AutoExecute,
        _ => TrustDisposition.Blocked,
    };

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
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

            var bootstrapSql = $"""
                CREATE TABLE IF NOT EXISTS {TableName} (
                    id uuid PRIMARY KEY,
                    tenant_id text NOT NULL,
                    action_scope text NOT NULL,
                    max_tier int NOT NULL,
                    confidence_threshold double precision,
                    value_ceiling numeric,
                    require_reversible boolean NOT NULL DEFAULT false,
                    description text,
                    is_enabled boolean NOT NULL DEFAULT true,
                    created_by text NOT NULL,
                    created_at_utc timestamptz NOT NULL,
                    updated_at_utc timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_trust_tier_policies_tenant_id ON {TableName}(tenant_id);
                CREATE INDEX IF NOT EXISTS idx_trust_tier_policies_action_scope ON {TableName}(action_scope);
                CREATE INDEX IF NOT EXISTS idx_trust_tier_policies_tenant_scope ON {TableName}(tenant_id, action_scope);
                """;

            await using var bootstrapCmd = new NpgsqlCommand(bootstrapSql, connection);
            await bootstrapCmd.ExecuteNonQueryAsync(cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
