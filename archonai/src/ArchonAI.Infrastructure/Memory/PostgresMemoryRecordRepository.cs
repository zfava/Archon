using System.Text.Json;
using ArchonAI.Core.Models;
using ArchonAI.Common.Observability;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

namespace ArchonAI.Infrastructure.Memory;

public sealed class PostgresMemoryRecordRepository : IMemoryRecordRepository
{
    private readonly string _connectionString;
    private readonly string _schema;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TableName => $"{_schema}.memory_records";

    public PostgresMemoryRecordRepository(IOptions<MemoryPersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString ?? throw new InvalidOperationException("Memory persistence connection string is not configured.");
        _schema = options.Value.Schema;
    }

    public async global::System.Threading.Tasks.Task SaveAsync(MemoryRecord record, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            INSERT INTO {TableName} (id, memory_type, scope, content, metadata, created_at_utc, expires_at_utc)
            VALUES (@id, @memoryType, @scope, @content, @metadata::jsonb, @createdAtUtc, @expiresAtUtc)
            ON CONFLICT (id) DO UPDATE
            SET memory_type = EXCLUDED.memory_type,
                scope = EXCLUDED.scope,
                content = EXCLUDED.content,
                metadata = EXCLUDED.metadata,
                created_at_utc = EXCLUDED.created_at_utc,
                expires_at_utc = EXCLUDED.expires_at_utc;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.AddWithValue("memoryType", record.MemoryType);
        command.Parameters.AddWithValue("scope", record.Scope);
        command.Parameters.AddWithValue("content", record.Content);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(record.Metadata));
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("expiresAtUtc", (object?)record.ExpiresAtUtc?.UtcDateTime ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async global::System.Threading.Tasks.Task SaveEmbeddingAsync(Guid memoryRecordId, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            UPDATE {TableName}
            SET embedding = @embedding
            WHERE id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("embedding", new Vector(embedding.ToArray()));
        command.Parameters.AddWithValue("id", memoryRecordId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> QueryByScopeAsync(string scope, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT id, memory_type, scope, content, metadata, created_at_utc, expires_at_utc
            FROM {TableName}
            WHERE scope = @scope
            ORDER BY created_at_utc DESC;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("scope", scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<MemoryRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadRecord(reader));
        }

        ArchonAI.Common.Observability.Telemetry.MemoryQueries.Add(1, new KeyValuePair<string, object?>("kind", "scope"));
        return results;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> SemanticSearchAsync(string scope, IReadOnlyList<float> queryEmbedding, int topK, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT id, memory_type, scope, content, metadata, created_at_utc, expires_at_utc
            FROM {TableName}
            WHERE scope = @scope AND embedding IS NOT NULL
            ORDER BY embedding <=> @queryEmbedding
            LIMIT @topK;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("scope", scope);
        command.Parameters.AddWithValue("queryEmbedding", new Vector(queryEmbedding.ToArray()));
        command.Parameters.AddWithValue("topK", Math.Max(1, topK));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<MemoryRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadRecord(reader));
        }

        ArchonAI.Common.Observability.Telemetry.MemoryQueries.Add(1, new KeyValuePair<string, object?>("kind", "semantic"));
        return results;
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

            var bootstrapSql = $"""
                CREATE EXTENSION IF NOT EXISTS vector;

                CREATE TABLE IF NOT EXISTS {TableName} (
                    id uuid PRIMARY KEY,
                    memory_type text NOT NULL,
                    scope text NOT NULL,
                    content text NOT NULL,
                    metadata jsonb NOT NULL DEFAULT jsonb_build_object(),
                    embedding vector(1536),
                    created_at_utc timestamptz NOT NULL,
                    expires_at_utc timestamptz NULL
                );

                CREATE INDEX IF NOT EXISTS idx_memory_records_scope ON {TableName}(scope);
                CREATE INDEX IF NOT EXISTS idx_memory_records_embedding ON {TableName} USING hnsw (embedding vector_cosine_ops);
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

    private static MemoryRecord ReadRecord(NpgsqlDataReader reader)
    {
        var metadataJson = reader.GetString(reader.GetOrdinal("metadata"));
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? new Dictionary<string, string>();

        DateTimeOffset? expiresAtUtc = reader.IsDBNull(reader.GetOrdinal("expires_at_utc"))
            ? null
            : new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("expires_at_utc")), TimeSpan.Zero);

        return new MemoryRecord(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            MemoryType: reader.GetString(reader.GetOrdinal("memory_type")),
            Scope: reader.GetString(reader.GetOrdinal("scope")),
            Content: reader.GetString(reader.GetOrdinal("content")),
            Metadata: metadata,
            CreatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero),
            ExpiresAtUtc: expiresAtUtc);
    }
}
