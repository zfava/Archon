using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresOidcLoginSessionStore : IOidcLoginSessionStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresOidcLoginSessionStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TableName => $"{_schema}.oidc_login_sessions";

    public PostgresOidcLoginSessionStore(IOptions<PersistenceOptions> options, ILogger<PostgresOidcLoginSessionStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new ArgumentNullException(nameof(options), "Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    public async Task CreateAsync(OidcLoginSession session, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {TableName} (state, nonce, code_verifier, organization_id, created_at_utc, expires_at_utc)
            VALUES (@state, @nonce, @codeVerifier, @organizationId, @createdAtUtc, @expiresAtUtc);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("state", session.State);
        cmd.Parameters.AddWithValue("nonce", session.Nonce);
        cmd.Parameters.AddWithValue("codeVerifier", session.CodeVerifier);
        cmd.Parameters.AddWithValue("organizationId", session.OrganizationId);
        cmd.Parameters.AddWithValue("createdAtUtc", session.CreatedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("expiresAtUtc", session.ExpiresAtUtc.UtcDateTime);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<OidcLoginSession?> ConsumeAsync(string state, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            DELETE FROM {TableName}
            WHERE state = @state AND expires_at_utc > now()
            RETURNING state, nonce, code_verifier, organization_id, created_at_utc, expires_at_utc;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("state", state);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new OidcLoginSession(
                reader.GetString(reader.GetOrdinal("state")),
                reader.GetString(reader.GetOrdinal("nonce")),
                reader.GetString(reader.GetOrdinal("code_verifier")),
                reader.GetGuid(reader.GetOrdinal("organization_id")),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("expires_at_utc")), TimeSpan.Zero));
        }

        return null;
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return Task.CompletedTask;
    }
}
