using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ControlPlane;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresControlPlaneStore : IControlPlaneRepository
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresControlPlaneStore> _logger;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PostgresControlPlaneStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresControlPlaneStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    private string TenantsTable => $"{_schema}.tenants";
    private string WorkflowsTable => $"{_schema}.managed_workflows";
    private string AgentsTable => $"{_schema}.managed_agents";
    private string PoliciesTable => $"{_schema}.platform_policies";
    private string ConfigurationsTable => $"{_schema}.platform_configurations";

    private Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return Task.CompletedTask;
        // Table creation is managed by DbUp migrations in ArchonAI.Migrations.
        // See Scripts/022_create_control_plane.sql
        _initialized = true;
        return Task.CompletedTask;
    }

    // ════════════════════════════════════════════════════════════
    // ── Tenants ────────────────────────────────────────────────
    // ════════════════════════════════════════════════════════════

    public async Task<Tenant> UpsertTenantAsync(Tenant tenant, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {TenantsTable}
                (id, name, display_name, status, tier, resource_quota, metadata,
                 created_at_utc, activated_at_utc, suspended_at_utc)
            VALUES
                (@id, @name, @displayName, @status, @tier, @resourceQuota::jsonb, @metadata::jsonb,
                 @createdAtUtc, @activatedAtUtc, @suspendedAtUtc)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                display_name = EXCLUDED.display_name,
                status = EXCLUDED.status,
                tier = EXCLUDED.tier,
                resource_quota = EXCLUDED.resource_quota,
                metadata = EXCLUDED.metadata,
                activated_at_utc = EXCLUDED.activated_at_utc,
                suspended_at_utc = EXCLUDED.suspended_at_utc
        ", conn);

        cmd.Parameters.AddWithValue("id", tenant.Id);
        cmd.Parameters.AddWithValue("name", tenant.Name);
        cmd.Parameters.AddWithValue("displayName", tenant.DisplayName);
        cmd.Parameters.AddWithValue("status", (int)tenant.Status);
        cmd.Parameters.AddWithValue("tier", (int)tenant.Tier);
        cmd.Parameters.AddWithValue("resourceQuota", JsonSerializer.Serialize(tenant.ResourceQuota, JsonOpts));
        cmd.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(tenant.Metadata, JsonOpts));
        cmd.Parameters.AddWithValue("createdAtUtc", tenant.CreatedAtUtc);
        cmd.Parameters.AddWithValue("activatedAtUtc", (object?)tenant.ActivatedAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("suspendedAtUtc", (object?)tenant.SuspendedAtUtc ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Tenant {TenantId} ({Name}) upserted.", tenant.Id, tenant.Name);
        return tenant;
    }

    public async Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {TenantsTable} WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapTenant(reader) : null;
    }

    public async Task<Tenant?> GetTenantByNameAsync(string name, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {TenantsTable} WHERE name = @name", conn);
        cmd.Parameters.AddWithValue("name", name);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapTenant(reader) : null;
    }

    public async Task<IReadOnlyList<Tenant>> ListTenantsAsync(
        TenantStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {TenantsTable} WHERE 1=1";
        var parameters = new List<NpgsqlParameter>();

        if (status.HasValue)
        {
            sql += " AND status = @status";
            parameters.Add(new NpgsqlParameter("status", (int)status.Value));
        }

        sql += " ORDER BY name OFFSET @offset LIMIT @limit";
        parameters.Add(new NpgsqlParameter("offset", offset));
        parameters.Add(new NpgsqlParameter("limit", limit));

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Tenant>();
        while (await reader.ReadAsync(ct))
            results.Add(MapTenant(reader));
        return results;
    }

    public async Task<bool> RemoveTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"DELETE FROM {TenantsTable} WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", tenantId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ════════════════════════════════════════════════════════════
    // ── Managed Workflows ──────────────────────────────────────
    // ════════════════════════════════════════════════════════════

    public async Task<ManagedWorkflow> UpsertWorkflowAsync(ManagedWorkflow workflow, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {WorkflowsTable}
                (id, tenant_id, name, description, status, strategy, step_count, metadata,
                 created_at_utc, last_executed_at_utc, execution_count, failure_count)
            VALUES
                (@id, @tenantId, @name, @description, @status, @strategy, @stepCount, @metadata::jsonb,
                 @createdAtUtc, @lastExecutedAtUtc, @executionCount, @failureCount)
            ON CONFLICT (id) DO UPDATE SET
                tenant_id = EXCLUDED.tenant_id,
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                status = EXCLUDED.status,
                strategy = EXCLUDED.strategy,
                step_count = EXCLUDED.step_count,
                metadata = EXCLUDED.metadata,
                last_executed_at_utc = EXCLUDED.last_executed_at_utc,
                execution_count = EXCLUDED.execution_count,
                failure_count = EXCLUDED.failure_count
        ", conn);

        cmd.Parameters.AddWithValue("id", workflow.Id);
        cmd.Parameters.AddWithValue("tenantId", workflow.TenantId);
        cmd.Parameters.AddWithValue("name", workflow.Name);
        cmd.Parameters.AddWithValue("description", workflow.Description);
        cmd.Parameters.AddWithValue("status", (int)workflow.Status);
        cmd.Parameters.AddWithValue("strategy", workflow.Strategy);
        cmd.Parameters.AddWithValue("stepCount", workflow.StepCount);
        cmd.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(workflow.Metadata, JsonOpts));
        cmd.Parameters.AddWithValue("createdAtUtc", workflow.CreatedAtUtc);
        cmd.Parameters.AddWithValue("lastExecutedAtUtc", (object?)workflow.LastExecutedAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("executionCount", workflow.ExecutionCount);
        cmd.Parameters.AddWithValue("failureCount", workflow.FailureCount);

        await cmd.ExecuteNonQueryAsync(ct);
        return workflow;
    }

    public async Task<ManagedWorkflow?> GetWorkflowAsync(Guid workflowId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {WorkflowsTable} WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", workflowId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapWorkflow(reader) : null;
    }

    public async Task<IReadOnlyList<ManagedWorkflow>> ListWorkflowsAsync(
        string? tenantId, ManagedWorkflowStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {WorkflowsTable} WHERE 1=1";
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrEmpty(tenantId))
        {
            sql += " AND tenant_id = @tenantId";
            parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        }

        if (status.HasValue)
        {
            sql += " AND status = @status";
            parameters.Add(new NpgsqlParameter("status", (int)status.Value));
        }

        sql += " ORDER BY name OFFSET @offset LIMIT @limit";
        parameters.Add(new NpgsqlParameter("offset", offset));
        parameters.Add(new NpgsqlParameter("limit", limit));

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ManagedWorkflow>();
        while (await reader.ReadAsync(ct))
            results.Add(MapWorkflow(reader));
        return results;
    }

    // ════════════════════════════════════════════════════════════
    // ── Managed Agents ─────────────────────────────────────────
    // ════════════════════════════════════════════════════════════

    public async Task<ManagedAgent> UpsertAgentAsync(ManagedAgent agent, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {AgentsTable}
                (id, tenant_id, name, version, status, capabilities, configuration,
                 registered_at_utc, last_active_at_utc, execution_count, failure_count)
            VALUES
                (@id, @tenantId, @name, @version, @status, @capabilities::jsonb, @configuration::jsonb,
                 @registeredAtUtc, @lastActiveAtUtc, @executionCount, @failureCount)
            ON CONFLICT (id) DO UPDATE SET
                tenant_id = EXCLUDED.tenant_id,
                name = EXCLUDED.name,
                version = EXCLUDED.version,
                status = EXCLUDED.status,
                capabilities = EXCLUDED.capabilities,
                configuration = EXCLUDED.configuration,
                last_active_at_utc = EXCLUDED.last_active_at_utc,
                execution_count = EXCLUDED.execution_count,
                failure_count = EXCLUDED.failure_count
        ", conn);

        cmd.Parameters.AddWithValue("id", agent.Id);
        cmd.Parameters.AddWithValue("tenantId", agent.TenantId);
        cmd.Parameters.AddWithValue("name", agent.Name);
        cmd.Parameters.AddWithValue("version", agent.Version);
        cmd.Parameters.AddWithValue("status", (int)agent.Status);
        cmd.Parameters.AddWithValue("capabilities", JsonSerializer.Serialize(agent.Capabilities, JsonOpts));
        cmd.Parameters.AddWithValue("configuration", JsonSerializer.Serialize(agent.Configuration, JsonOpts));
        cmd.Parameters.AddWithValue("registeredAtUtc", agent.RegisteredAtUtc);
        cmd.Parameters.AddWithValue("lastActiveAtUtc", (object?)agent.LastActiveAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("executionCount", agent.ExecutionCount);
        cmd.Parameters.AddWithValue("failureCount", agent.FailureCount);

        await cmd.ExecuteNonQueryAsync(ct);
        return agent;
    }

    public async Task<ManagedAgent?> GetAgentAsync(Guid agentId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {AgentsTable} WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", agentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapManagedAgent(reader) : null;
    }

    public async Task<IReadOnlyList<ManagedAgent>> ListAgentsAsync(
        string? tenantId, ManagedAgentStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {AgentsTable} WHERE 1=1";
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrEmpty(tenantId))
        {
            sql += " AND tenant_id = @tenantId";
            parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        }

        if (status.HasValue)
        {
            sql += " AND status = @status";
            parameters.Add(new NpgsqlParameter("status", (int)status.Value));
        }

        sql += " ORDER BY name OFFSET @offset LIMIT @limit";
        parameters.Add(new NpgsqlParameter("offset", offset));
        parameters.Add(new NpgsqlParameter("limit", limit));

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ManagedAgent>();
        while (await reader.ReadAsync(ct))
            results.Add(MapManagedAgent(reader));
        return results;
    }

    public async Task<bool> RemoveAgentAsync(Guid agentId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"DELETE FROM {AgentsTable} WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", agentId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ════════════════════════════════════════════════════════════
    // ── Policies ───────────────────────────────────────────────
    // ════════════════════════════════════════════════════════════

    public async Task<PlatformPolicy> UpsertPolicyAsync(PlatformPolicy policy, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {PoliciesTable}
                (id, tenant_id, name, description, policy_type, target_resource, rules,
                 is_enabled, priority, created_at_utc, updated_at_utc)
            VALUES
                (@id, @tenantId, @name, @description, @policyType, @targetResource, @rules::jsonb,
                 @isEnabled, @priority, @createdAtUtc, @updatedAtUtc)
            ON CONFLICT (id) DO UPDATE SET
                tenant_id = EXCLUDED.tenant_id,
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                policy_type = EXCLUDED.policy_type,
                target_resource = EXCLUDED.target_resource,
                rules = EXCLUDED.rules,
                is_enabled = EXCLUDED.is_enabled,
                priority = EXCLUDED.priority,
                updated_at_utc = EXCLUDED.updated_at_utc
        ", conn);

        cmd.Parameters.AddWithValue("id", policy.Id);
        cmd.Parameters.AddWithValue("tenantId", policy.TenantId);
        cmd.Parameters.AddWithValue("name", policy.Name);
        cmd.Parameters.AddWithValue("description", policy.Description);
        cmd.Parameters.AddWithValue("policyType", (int)policy.PolicyType);
        cmd.Parameters.AddWithValue("targetResource", policy.TargetResource);
        cmd.Parameters.AddWithValue("rules", JsonSerializer.Serialize(policy.Rules, JsonOpts));
        cmd.Parameters.AddWithValue("isEnabled", policy.IsEnabled);
        cmd.Parameters.AddWithValue("priority", policy.Priority);
        cmd.Parameters.AddWithValue("createdAtUtc", policy.CreatedAtUtc);
        cmd.Parameters.AddWithValue("updatedAtUtc", (object?)policy.UpdatedAtUtc ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        return policy;
    }

    public async Task<PlatformPolicy?> GetPolicyAsync(Guid policyId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {PoliciesTable} WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", policyId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapPolicy(reader) : null;
    }

    public async Task<IReadOnlyList<PlatformPolicy>> ListPoliciesAsync(
        string? tenantId, PlatformPolicyType? policyType, bool? isEnabled,
        int offset, int limit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {PoliciesTable} WHERE 1=1";
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrEmpty(tenantId))
        {
            sql += " AND tenant_id = @tenantId";
            parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        }

        if (policyType.HasValue)
        {
            sql += " AND policy_type = @policyType";
            parameters.Add(new NpgsqlParameter("policyType", (int)policyType.Value));
        }

        if (isEnabled.HasValue)
        {
            sql += " AND is_enabled = @isEnabled";
            parameters.Add(new NpgsqlParameter("isEnabled", isEnabled.Value));
        }

        sql += " ORDER BY priority DESC, name OFFSET @offset LIMIT @limit";
        parameters.Add(new NpgsqlParameter("offset", offset));
        parameters.Add(new NpgsqlParameter("limit", limit));

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<PlatformPolicy>();
        while (await reader.ReadAsync(ct))
            results.Add(MapPolicy(reader));
        return results;
    }

    public async Task<bool> RemovePolicyAsync(Guid policyId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"DELETE FROM {PoliciesTable} WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", policyId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ════════════════════════════════════════════════════════════
    // ── Configurations ─────────────────────────────────────────
    // ════════════════════════════════════════════════════════════

    public async Task<PlatformConfiguration> UpsertConfigurationAsync(
        PlatformConfiguration config, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {ConfigurationsTable}
                (id, tenant_id, scope, key, value, description, is_secret,
                 created_at_utc, updated_at_utc)
            VALUES
                (@id, @tenantId, @scope, @key, @value, @description, @isSecret,
                 @createdAtUtc, @updatedAtUtc)
            ON CONFLICT (tenant_id, scope, key) DO UPDATE SET
                value = EXCLUDED.value,
                description = EXCLUDED.description,
                is_secret = EXCLUDED.is_secret,
                updated_at_utc = EXCLUDED.updated_at_utc
        ", conn);

        cmd.Parameters.AddWithValue("id", config.Id);
        cmd.Parameters.AddWithValue("tenantId", config.TenantId);
        cmd.Parameters.AddWithValue("scope", config.Scope);
        cmd.Parameters.AddWithValue("key", config.Key);
        cmd.Parameters.AddWithValue("value", config.Value);
        cmd.Parameters.AddWithValue("description", (object?)config.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isSecret", config.IsSecret);
        cmd.Parameters.AddWithValue("createdAtUtc", config.CreatedAtUtc);
        cmd.Parameters.AddWithValue("updatedAtUtc", (object?)config.UpdatedAtUtc ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        return config;
    }

    public async Task<PlatformConfiguration?> GetConfigurationAsync(
        string tenantId, string scope, string key, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {ConfigurationsTable}
            WHERE tenant_id = @tenantId AND scope = @scope AND key = @key
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("scope", scope);
        cmd.Parameters.AddWithValue("key", key);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapConfiguration(reader) : null;
    }

    public async Task<IReadOnlyList<PlatformConfiguration>> ListConfigurationsAsync(
        string? tenantId, string? scope, int offset, int limit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {ConfigurationsTable} WHERE 1=1";
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrEmpty(tenantId))
        {
            sql += " AND tenant_id = @tenantId";
            parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        }

        if (!string.IsNullOrEmpty(scope))
        {
            sql += " AND scope = @scope";
            parameters.Add(new NpgsqlParameter("scope", scope));
        }

        sql += " ORDER BY scope, key OFFSET @offset LIMIT @limit";
        parameters.Add(new NpgsqlParameter("offset", offset));
        parameters.Add(new NpgsqlParameter("limit", limit));

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<PlatformConfiguration>();
        while (await reader.ReadAsync(ct))
            results.Add(MapConfiguration(reader));
        return results;
    }

    public async Task<bool> RemoveConfigurationAsync(
        string tenantId, string scope, string key, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            DELETE FROM {ConfigurationsTable}
            WHERE tenant_id = @tenantId AND scope = @scope AND key = @key
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("scope", scope);
        cmd.Parameters.AddWithValue("key", key);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ════════════════════════════════════════════════════════════
    // ── Counts ─────────────────────────────────────────────────
    // ════════════════════════════════════════════════════════════

    public async Task<int> CountTenantsAsync(TenantStatus? status = null, CancellationToken ct = default)
    {
        return await CountAsync(TenantsTable, status.HasValue ? ("status", (int)status.Value) : null, ct);
    }

    public async Task<int> CountWorkflowsAsync(string? tenantId = null, CancellationToken ct = default)
    {
        return await CountWithTenantAsync(WorkflowsTable, tenantId, ct);
    }

    public async Task<int> CountAgentsAsync(string? tenantId = null, CancellationToken ct = default)
    {
        return await CountWithTenantAsync(AgentsTable, tenantId, ct);
    }

    public async Task<int> CountPoliciesAsync(string? tenantId = null, CancellationToken ct = default)
    {
        return await CountWithTenantAsync(PoliciesTable, tenantId, ct);
    }

    public async Task<int> CountConfigurationsAsync(string? tenantId = null, CancellationToken ct = default)
    {
        return await CountWithTenantAsync(ConfigurationsTable, tenantId, ct);
    }

    private async Task<int> CountAsync(string table, (string col, int val)? filter, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT COUNT(*) FROM {table}";
        if (filter.HasValue)
            sql += $" WHERE {filter.Value.col} = @filterVal";

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (filter.HasValue)
            cmd.Parameters.AddWithValue("filterVal", filter.Value.val);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private async Task<int> CountWithTenantAsync(string table, string? tenantId, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT COUNT(*) FROM {table}";
        if (!string.IsNullOrEmpty(tenantId))
            sql += " WHERE tenant_id = @tenantId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (!string.IsNullOrEmpty(tenantId))
            cmd.Parameters.AddWithValue("tenantId", tenantId);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    // ════════════════════════════════════════════════════════════
    // ── Row Mappers ────────────────────────────────────────────
    // ════════════════════════════════════════════════════════════

    private static Tenant MapTenant(NpgsqlDataReader reader)
    {
        var activatedOrd = reader.GetOrdinal("activated_at_utc");
        var suspendedOrd = reader.GetOrdinal("suspended_at_utc");

        return new Tenant(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("name")),
            reader.GetString(reader.GetOrdinal("display_name")),
            (TenantStatus)reader.GetInt32(reader.GetOrdinal("status")),
            (TenantTier)reader.GetInt32(reader.GetOrdinal("tier")),
            JsonSerializer.Deserialize<TenantResourceQuota>(
                reader.GetString(reader.GetOrdinal("resource_quota")), JsonOpts)!,
            JsonSerializer.Deserialize<Dictionary<string, string>>(
                reader.GetString(reader.GetOrdinal("metadata")), JsonOpts) ?? new(),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at_utc")),
            reader.IsDBNull(activatedOrd) ? null : reader.GetFieldValue<DateTimeOffset>(activatedOrd),
            reader.IsDBNull(suspendedOrd) ? null : reader.GetFieldValue<DateTimeOffset>(suspendedOrd));
    }

    private static ManagedWorkflow MapWorkflow(NpgsqlDataReader reader)
    {
        var lastExecOrd = reader.GetOrdinal("last_executed_at_utc");

        return new ManagedWorkflow(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("tenant_id")),
            reader.GetString(reader.GetOrdinal("name")),
            reader.GetString(reader.GetOrdinal("description")),
            (ManagedWorkflowStatus)reader.GetInt32(reader.GetOrdinal("status")),
            reader.GetString(reader.GetOrdinal("strategy")),
            reader.GetInt32(reader.GetOrdinal("step_count")),
            JsonSerializer.Deserialize<Dictionary<string, string>>(
                reader.GetString(reader.GetOrdinal("metadata")), JsonOpts) ?? new(),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at_utc")),
            reader.IsDBNull(lastExecOrd) ? null : reader.GetFieldValue<DateTimeOffset>(lastExecOrd),
            reader.GetInt64(reader.GetOrdinal("execution_count")),
            reader.GetInt64(reader.GetOrdinal("failure_count")));
    }

    private static ManagedAgent MapManagedAgent(NpgsqlDataReader reader)
    {
        var lastActiveOrd = reader.GetOrdinal("last_active_at_utc");

        return new ManagedAgent(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("tenant_id")),
            reader.GetString(reader.GetOrdinal("name")),
            reader.GetString(reader.GetOrdinal("version")),
            (ManagedAgentStatus)reader.GetInt32(reader.GetOrdinal("status")),
            JsonSerializer.Deserialize<List<string>>(
                reader.GetString(reader.GetOrdinal("capabilities")), JsonOpts) ?? new(),
            JsonSerializer.Deserialize<Dictionary<string, string>>(
                reader.GetString(reader.GetOrdinal("configuration")), JsonOpts) ?? new(),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("registered_at_utc")),
            reader.IsDBNull(lastActiveOrd) ? null : reader.GetFieldValue<DateTimeOffset>(lastActiveOrd),
            reader.GetInt64(reader.GetOrdinal("execution_count")),
            reader.GetInt64(reader.GetOrdinal("failure_count")));
    }

    private static PlatformPolicy MapPolicy(NpgsqlDataReader reader)
    {
        var updatedOrd = reader.GetOrdinal("updated_at_utc");

        return new PlatformPolicy(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("tenant_id")),
            reader.GetString(reader.GetOrdinal("name")),
            reader.GetString(reader.GetOrdinal("description")),
            (PlatformPolicyType)reader.GetInt32(reader.GetOrdinal("policy_type")),
            reader.GetString(reader.GetOrdinal("target_resource")),
            JsonSerializer.Deserialize<Dictionary<string, string>>(
                reader.GetString(reader.GetOrdinal("rules")), JsonOpts) ?? new(),
            reader.GetBoolean(reader.GetOrdinal("is_enabled")),
            reader.GetInt32(reader.GetOrdinal("priority")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at_utc")),
            reader.IsDBNull(updatedOrd) ? null : reader.GetFieldValue<DateTimeOffset>(updatedOrd));
    }

    private static PlatformConfiguration MapConfiguration(NpgsqlDataReader reader)
    {
        var descOrd = reader.GetOrdinal("description");
        var updatedOrd = reader.GetOrdinal("updated_at_utc");

        return new PlatformConfiguration(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("tenant_id")),
            reader.GetString(reader.GetOrdinal("scope")),
            reader.GetString(reader.GetOrdinal("key")),
            reader.GetString(reader.GetOrdinal("value")),
            reader.IsDBNull(descOrd) ? null : reader.GetString(descOrd),
            reader.GetBoolean(reader.GetOrdinal("is_secret")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at_utc")),
            reader.IsDBNull(updatedOrd) ? null : reader.GetFieldValue<DateTimeOffset>(updatedOrd));
    }
}
