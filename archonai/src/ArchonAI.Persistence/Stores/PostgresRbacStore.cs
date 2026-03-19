using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Rbac;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresRbacStore : IRbacService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly IEventBus _eventBus;
    private readonly ILogger<PostgresRbacStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private long _accessChecks;
    private long _accessDenials;

    private string RolesTable => $"{_schema}.rbac_roles";
    private string AssignmentsTable => $"{_schema}.rbac_assignments";
    private string PoliciesTable => $"{_schema}.rbac_policies";

    private static readonly IReadOnlyList<string> AllPermissions =
    [
        "agents:read", "agents:write", "agents:execute",
        "workflows:read", "workflows:write", "workflows:execute",
        "connectors:read", "connectors:write", "connectors:execute",
        "admin:read", "admin:write",
        "policy:read", "policy:write",
        "monitoring:read",
        "rbac:read", "rbac:write"
    ];

    private static readonly IReadOnlyList<string> OperatorPermissions =
    [
        "agents:read", "agents:execute",
        "workflows:read", "workflows:execute",
        "connectors:read", "connectors:execute",
        "monitoring:read",
        "policy:read",
        "rbac:read"
    ];

    private static readonly IReadOnlyList<string> ViewerPermissions =
    [
        "agents:read",
        "workflows:read",
        "connectors:read",
        "monitoring:read",
        "policy:read",
        "rbac:read"
    ];

    public PostgresRbacStore(IOptions<PersistenceOptions> options, IEventBus eventBus, ILogger<PostgresRbacStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new InvalidOperationException("Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<RbacRole>> GetRolesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT id, name, description, permissions, is_system, created_at_utc
            FROM {RolesTable}
            ORDER BY name;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<RbacRole>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadRole(reader));
        }

        return result;
    }

    public async global::System.Threading.Tasks.Task<RbacRole?> GetRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT id, name, description, permissions, is_system, created_at_utc
            FROM {RolesTable}
            WHERE id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", roleId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadRole(reader);
        }

        return null;
    }

    public async global::System.Threading.Tasks.Task<RbacRole> CreateRoleAsync(string name, string description, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var role = new RbacRole(Guid.NewGuid(), name, description, permissions, false, DateTimeOffset.UtcNow);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {RolesTable}
            (id, name, description, permissions, is_system, created_at_utc)
            VALUES
            (@id, @name, @description, @permissions::jsonb, @isSystem, @createdAtUtc);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", role.Id);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue("permissions", JsonSerializer.Serialize(permissions));
        command.Parameters.AddWithValue("isSystem", false);
        command.Parameters.AddWithValue("createdAtUtc", role.CreatedAtUtc.UtcDateTime);

        await command.ExecuteNonQueryAsync(ct);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.role.created",
            "PostgresRbacStore",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["roleId"] = role.Id.ToString(),
                ["roleName"] = name,
                ["permissionCount"] = permissions.Count.ToString()
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        return role;
    }

    public async global::System.Threading.Tasks.Task UpdateRoleAsync(Guid roleId, string description, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Check if role exists and is not a system role
        var checkSql = $"SELECT is_system, name FROM {RolesTable} WHERE id = @id;";
        await using var checkCmd = new NpgsqlCommand(checkSql, connection);
        checkCmd.Parameters.AddWithValue("id", roleId);
        await using var checkReader = await checkCmd.ExecuteReaderAsync(ct);

        if (!await checkReader.ReadAsync(ct))
            throw new KeyNotFoundException($"Role {roleId} not found.");

        var isSystem = checkReader.GetBoolean(0);
        var roleName = checkReader.GetString(1);
        await checkReader.CloseAsync();

        if (isSystem)
            throw new InvalidOperationException("System roles cannot be modified.");

        var sql = $"""
            UPDATE {RolesTable}
            SET description = @description, permissions = @permissions::jsonb
            WHERE id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", roleId);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue("permissions", JsonSerializer.Serialize(permissions));

        await command.ExecuteNonQueryAsync(ct);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.role.updated",
            "PostgresRbacStore",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["roleId"] = roleId.ToString(),
                ["roleName"] = roleName
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);
    }

    public async global::System.Threading.Tasks.Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Check if role exists and is not a system role
        var checkSql = $"SELECT is_system FROM {RolesTable} WHERE id = @id;";
        await using var checkCmd = new NpgsqlCommand(checkSql, connection);
        checkCmd.Parameters.AddWithValue("id", roleId);
        var checkResult = await checkCmd.ExecuteScalarAsync(ct);

        if (checkResult is null)
            throw new KeyNotFoundException($"Role {roleId} not found.");

        if ((bool)checkResult)
            throw new InvalidOperationException("System roles cannot be deleted.");

        // Remove all assignments for this role
        var deleteAssignmentsSql = $"DELETE FROM {AssignmentsTable} WHERE role_id = @roleId;";
        await using var deleteAssignmentsCmd = new NpgsqlCommand(deleteAssignmentsSql, connection);
        deleteAssignmentsCmd.Parameters.AddWithValue("roleId", roleId);
        await deleteAssignmentsCmd.ExecuteNonQueryAsync(ct);

        // Delete the role
        var sql = $"DELETE FROM {RolesTable} WHERE id = @id;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", roleId);
        var rowsAffected = await command.ExecuteNonQueryAsync(ct);

        if (rowsAffected == 0)
            throw new KeyNotFoundException($"Role {roleId} not found.");

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.role.deleted",
            "PostgresRbacStore",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["roleId"] = roleId.ToString()
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<RoleAssignment>> GetAssignmentsAsync(string? subjectId = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var whereClause = string.IsNullOrEmpty(subjectId) ? "" : "WHERE subject_id = @subjectId";
        var sql = $"""
            SELECT id, subject_id, subject_type, role_id, assigned_by, assigned_at_utc
            FROM {AssignmentsTable}
            {whereClause}
            ORDER BY assigned_at_utc DESC;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        if (!string.IsNullOrEmpty(subjectId))
        {
            command.Parameters.AddWithValue("subjectId", subjectId);
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<RoleAssignment>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(new RoleAssignment(
                Id: reader.GetGuid(reader.GetOrdinal("id")),
                SubjectId: reader.GetString(reader.GetOrdinal("subject_id")),
                SubjectType: reader.GetString(reader.GetOrdinal("subject_type")),
                RoleId: reader.GetGuid(reader.GetOrdinal("role_id")),
                AssignedBy: reader.GetString(reader.GetOrdinal("assigned_by")),
                AssignedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("assigned_at_utc")), TimeSpan.Zero)));
        }

        return result;
    }

    public async global::System.Threading.Tasks.Task<RoleAssignment> AssignRoleAsync(string subjectId, string subjectType, Guid roleId, string assignedBy, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Verify role exists
        var checkSql = $"SELECT COUNT(*) FROM {RolesTable} WHERE id = @roleId;";
        await using var checkCmd = new NpgsqlCommand(checkSql, connection);
        checkCmd.Parameters.AddWithValue("roleId", roleId);
        var exists = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(ct)) > 0;

        if (!exists)
            throw new KeyNotFoundException($"Role {roleId} not found.");

        var assignment = new RoleAssignment(Guid.NewGuid(), subjectId, subjectType, roleId, assignedBy, DateTimeOffset.UtcNow);

        var sql = $"""
            INSERT INTO {AssignmentsTable}
            (id, subject_id, subject_type, role_id, assigned_by, assigned_at_utc)
            VALUES
            (@id, @subjectId, @subjectType, @roleId, @assignedBy, @assignedAtUtc);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", assignment.Id);
        command.Parameters.AddWithValue("subjectId", subjectId);
        command.Parameters.AddWithValue("subjectType", subjectType);
        command.Parameters.AddWithValue("roleId", roleId);
        command.Parameters.AddWithValue("assignedBy", assignedBy);
        command.Parameters.AddWithValue("assignedAtUtc", assignment.AssignedAtUtc.UtcDateTime);

        await command.ExecuteNonQueryAsync(ct);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.role.assigned",
            "PostgresRbacStore",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["assignmentId"] = assignment.Id.ToString(),
                ["subjectId"] = subjectId,
                ["subjectType"] = subjectType,
                ["roleId"] = roleId.ToString(),
                ["assignedBy"] = assignedBy
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        return assignment;
    }

    public async global::System.Threading.Tasks.Task RevokeRoleAsync(Guid assignmentId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Fetch assignment details before deleting (for event payload)
        var fetchSql = $"SELECT subject_id, role_id FROM {AssignmentsTable} WHERE id = @id;";
        await using var fetchCmd = new NpgsqlCommand(fetchSql, connection);
        fetchCmd.Parameters.AddWithValue("id", assignmentId);
        await using var fetchReader = await fetchCmd.ExecuteReaderAsync(ct);

        if (!await fetchReader.ReadAsync(ct))
            throw new KeyNotFoundException($"Assignment {assignmentId} not found.");

        var subjectId = fetchReader.GetString(0);
        var roleId = fetchReader.GetGuid(1);
        await fetchReader.CloseAsync();

        var sql = $"DELETE FROM {AssignmentsTable} WHERE id = @id;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", assignmentId);

        await command.ExecuteNonQueryAsync(ct);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.role.revoked",
            "PostgresRbacStore",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["assignmentId"] = assignmentId.ToString(),
                ["subjectId"] = subjectId,
                ["roleId"] = roleId.ToString()
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<PermissionPolicy>> GetPoliciesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT id, name, description, required_permissions, resource, effect, conditions, is_enabled, created_at_utc
            FROM {PoliciesTable}
            ORDER BY name;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<PermissionPolicy>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadPolicy(reader));
        }

        return result;
    }

    public async global::System.Threading.Tasks.Task<PermissionPolicy> CreatePolicyAsync(string name, string description, IReadOnlyList<string> requiredPermissions, string resource, string effect, IReadOnlyDictionary<string, string> conditions, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var policy = new PermissionPolicy(Guid.NewGuid(), name, description, requiredPermissions, resource, effect, conditions, true, DateTimeOffset.UtcNow);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {PoliciesTable}
            (id, name, description, required_permissions, resource, effect, conditions, is_enabled, created_at_utc)
            VALUES
            (@id, @name, @description, @requiredPermissions::jsonb, @resource, @effect, @conditions::jsonb, @isEnabled, @createdAtUtc);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", policy.Id);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue("requiredPermissions", JsonSerializer.Serialize(requiredPermissions));
        command.Parameters.AddWithValue("resource", resource);
        command.Parameters.AddWithValue("effect", effect);
        command.Parameters.AddWithValue("conditions", JsonSerializer.Serialize(conditions));
        command.Parameters.AddWithValue("isEnabled", true);
        command.Parameters.AddWithValue("createdAtUtc", policy.CreatedAtUtc.UtcDateTime);

        await command.ExecuteNonQueryAsync(ct);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.policy.created",
            "PostgresRbacStore",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["policyId"] = policy.Id.ToString(),
                ["policyName"] = name,
                ["effect"] = effect
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        return policy;
    }

    public async global::System.Threading.Tasks.Task UpdatePolicyAsync(Guid policyId, bool isEnabled, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"UPDATE {PoliciesTable} SET is_enabled = @isEnabled WHERE id = @id;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", policyId);
        command.Parameters.AddWithValue("isEnabled", isEnabled);

        var rowsAffected = await command.ExecuteNonQueryAsync(ct);
        if (rowsAffected == 0)
            throw new KeyNotFoundException($"Policy {policyId} not found.");

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.policy.updated",
            "PostgresRbacStore",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["policyId"] = policyId.ToString(),
                ["isEnabled"] = isEnabled.ToString()
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);
    }

    public async global::System.Threading.Tasks.Task DeletePolicyAsync(Guid policyId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"DELETE FROM {PoliciesTable} WHERE id = @id;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", policyId);

        var rowsAffected = await command.ExecuteNonQueryAsync(ct);
        if (rowsAffected == 0)
            throw new KeyNotFoundException($"Policy {policyId} not found.");

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.policy.deleted",
            "PostgresRbacStore",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["policyId"] = policyId.ToString()
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);
    }

    public async global::System.Threading.Tasks.Task<AccessDecision> EvaluateAccessAsync(string subjectId, string resource, string action, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        Interlocked.Increment(ref _accessChecks);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Collect all permissions for this subject via role assignments
        var permSql = $"""
            SELECT DISTINCT r.permissions
            FROM {AssignmentsTable} a
            INNER JOIN {RolesTable} r ON r.id = a.role_id
            WHERE a.subject_id = @subjectId;
            """;

        await using var permCmd = new NpgsqlCommand(permSql, connection);
        permCmd.Parameters.AddWithValue("subjectId", subjectId);

        var effectivePermissions = new HashSet<string>();
        await using var permReader = await permCmd.ExecuteReaderAsync(ct);
        while (await permReader.ReadAsync(ct))
        {
            var permissionsJson = permReader.GetString(0);
            var permissions = JsonSerializer.Deserialize<List<string>>(permissionsJson);
            if (permissions is not null)
            {
                foreach (var perm in permissions)
                    effectivePermissions.Add(perm);
            }
        }
        await permReader.CloseAsync();

        // Evaluate against policies
        var policySql = $"""
            SELECT id, name, description, required_permissions, resource, effect, conditions, is_enabled, created_at_utc
            FROM {PoliciesTable}
            WHERE is_enabled = true;
            """;

        await using var policyCmd = new NpgsqlCommand(policySql, connection);
        await using var policyReader = await policyCmd.ExecuteReaderAsync(ct);

        var matchedPolicies = new List<string>();
        bool? policyDecision = null;

        while (await policyReader.ReadAsync(ct))
        {
            var policy = ReadPolicy(policyReader);

            bool resourceMatches = string.Equals(policy.Resource, resource, StringComparison.OrdinalIgnoreCase)
                || policy.Resource == "*";

            if (!resourceMatches) continue;

            bool hasRequiredPermissions = policy.RequiredPermissions.All(rp => effectivePermissions.Contains(rp));

            if (hasRequiredPermissions)
            {
                matchedPolicies.Add(policy.Name);

                if (string.Equals(policy.Effect, "deny", StringComparison.OrdinalIgnoreCase))
                {
                    policyDecision = false;
                    break; // deny takes precedence
                }

                if (string.Equals(policy.Effect, "allow", StringComparison.OrdinalIgnoreCase))
                    policyDecision = true;
            }
        }

        // If no policy matched, fall back to permission check
        bool isAllowed;
        string reason;

        if (policyDecision.HasValue)
        {
            isAllowed = policyDecision.Value;
            reason = isAllowed
                ? $"Access granted by policies: {string.Join(", ", matchedPolicies)}"
                : $"Access denied by policy: {matchedPolicies.LastOrDefault() ?? "unknown"}";
        }
        else
        {
            isAllowed = effectivePermissions.Contains(action);
            reason = isAllowed
                ? $"Access granted via permission '{action}'"
                : $"Access denied: subject '{subjectId}' lacks permission '{action}'";
        }

        if (!isAllowed)
        {
            Interlocked.Increment(ref _accessDenials);
        }

        return new AccessDecision(
            isAllowed,
            subjectId,
            resource,
            action,
            reason,
            matchedPolicies.AsReadOnly(),
            DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(string subjectId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT DISTINCT r.permissions
            FROM {AssignmentsTable} a
            INNER JOIN {RolesTable} r ON r.id = a.role_id
            WHERE a.subject_id = @subjectId;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("subjectId", subjectId);

        var effectivePermissions = new HashSet<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var permissionsJson = reader.GetString(0);
            var permissions = JsonSerializer.Deserialize<List<string>>(permissionsJson);
            if (permissions is not null)
            {
                foreach (var perm in permissions)
                    effectivePermissions.Add(perm);
            }
        }

        IReadOnlyList<string> result = effectivePermissions.ToList().AsReadOnly();
        return result;
    }

    public RbacStatus GetStatus()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();

        int totalRoles = 0, totalAssignments = 0, totalPolicies = 0;

        using (var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {RolesTable};", connection))
        {
            totalRoles = Convert.ToInt32(cmd.ExecuteScalar());
        }

        using (var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {AssignmentsTable};", connection))
        {
            totalAssignments = Convert.ToInt32(cmd.ExecuteScalar());
        }

        using (var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {PoliciesTable};", connection))
        {
            totalPolicies = Convert.ToInt32(cmd.ExecuteScalar());
        }

        return new RbacStatus(
            IsActive: true,
            TotalRoles: totalRoles,
            TotalAssignments: totalAssignments,
            TotalPolicies: totalPolicies,
            AccessChecks: Interlocked.Read(ref _accessChecks),
            AccessDenials: Interlocked.Read(ref _accessDenials),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private static RbacRole ReadRole(NpgsqlDataReader reader)
    {
        var permissionsJson = reader.GetString(reader.GetOrdinal("permissions"));
        var permissions = JsonSerializer.Deserialize<List<string>>(permissionsJson) ?? new List<string>();

        return new RbacRole(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            Name: reader.GetString(reader.GetOrdinal("name")),
            Description: reader.GetString(reader.GetOrdinal("description")),
            Permissions: permissions.AsReadOnly(),
            IsSystem: reader.GetBoolean(reader.GetOrdinal("is_system")),
            CreatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero));
    }

    private static PermissionPolicy ReadPolicy(NpgsqlDataReader reader)
    {
        var requiredPermissionsJson = reader.GetString(reader.GetOrdinal("required_permissions"));
        var requiredPermissions = JsonSerializer.Deserialize<List<string>>(requiredPermissionsJson) ?? new List<string>();

        var conditionsJson = reader.GetString(reader.GetOrdinal("conditions"));
        var conditions = JsonSerializer.Deserialize<Dictionary<string, string>>(conditionsJson) ?? new Dictionary<string, string>();

        return new PermissionPolicy(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            Name: reader.GetString(reader.GetOrdinal("name")),
            Description: reader.GetString(reader.GetOrdinal("description")),
            RequiredPermissions: requiredPermissions.AsReadOnly(),
            Resource: reader.GetString(reader.GetOrdinal("resource")),
            Effect: reader.GetString(reader.GetOrdinal("effect")),
            Conditions: conditions.AsReadOnly(),
            IsEnabled: reader.GetBoolean(reader.GetOrdinal("is_enabled")),
            CreatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero));
    }

    private async global::System.Threading.Tasks.Task SeedSystemRolesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var countSql = $"SELECT COUNT(*) FROM {RolesTable};";
        await using var countCmd = new NpgsqlCommand(countSql, connection);
        var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync(cancellationToken));

        if (count > 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var roles = new[]
        {
            new RbacRole(Guid.NewGuid(), "Admin", "Full administrative access to all resources", AllPermissions, true, now),
            new RbacRole(Guid.NewGuid(), "Operator", "Read and execute access to operational resources", OperatorPermissions, true, now),
            new RbacRole(Guid.NewGuid(), "Viewer", "Read-only access to all resources", ViewerPermissions, true, now),
        };

        foreach (var role in roles)
        {
            var sql = $"""
                INSERT INTO {RolesTable}
                (id, name, description, permissions, is_system, created_at_utc)
                VALUES
                (@id, @name, @description, @permissions::jsonb, @isSystem, @createdAtUtc);
                """;

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", role.Id);
            command.Parameters.AddWithValue("name", role.Name);
            command.Parameters.AddWithValue("description", role.Description);
            command.Parameters.AddWithValue("permissions", JsonSerializer.Serialize(role.Permissions));
            command.Parameters.AddWithValue("isSystem", role.IsSystem);
            command.Parameters.AddWithValue("createdAtUtc", role.CreatedAtUtc.UtcDateTime);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("Seeded 3 default system roles (Admin, Operator, Viewer)");
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
                CREATE TABLE IF NOT EXISTS {{RolesTable}} (
                    id uuid PRIMARY KEY,
                    name text NOT NULL,
                    description text NOT NULL,
                    permissions jsonb NOT NULL DEFAULT '[]'::jsonb,
                    is_system boolean NOT NULL DEFAULT false,
                    created_at_utc timestamptz NOT NULL
                );

                CREATE TABLE IF NOT EXISTS {{AssignmentsTable}} (
                    id uuid PRIMARY KEY,
                    subject_id text NOT NULL,
                    subject_type text NOT NULL,
                    role_id uuid NOT NULL,
                    assigned_by text NOT NULL,
                    assigned_at_utc timestamptz NOT NULL
                );

                CREATE TABLE IF NOT EXISTS {{PoliciesTable}} (
                    id uuid PRIMARY KEY,
                    name text NOT NULL,
                    description text NOT NULL,
                    required_permissions jsonb NOT NULL DEFAULT '[]'::jsonb,
                    resource text NOT NULL,
                    effect text NOT NULL,
                    conditions jsonb NOT NULL DEFAULT '{}'::jsonb,
                    is_enabled boolean NOT NULL DEFAULT true,
                    created_at_utc timestamptz NOT NULL
                );
                """;

            await using var bootstrapCmd = new NpgsqlCommand(bootstrapSql, connection);
            await bootstrapCmd.ExecuteNonQueryAsync(cancellationToken);

            await SeedSystemRolesAsync(connection, cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
