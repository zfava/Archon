using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Knowledge;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Knowledge;

public sealed class PostgresKnowledgeGraphStore : IKnowledgeGraphStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string NodesTable => $"{_schema}.knowledge_nodes";
    private string EdgesTable => $"{_schema}.knowledge_relationships";

    public PostgresKnowledgeGraphStore(IOptions<KnowledgeGraphOptions> options)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new InvalidOperationException("Knowledge graph connection string is not configured.");
        _schema = options.Value.Schema;
    }

    public async global::System.Threading.Tasks.Task UpsertNodeAsync(KnowledgeNode node, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = $"""
            INSERT INTO {NodesTable} (node_id, node_type, display_name, properties, updated_at_utc)
            VALUES (@nodeId, @nodeType, @displayName, @properties::jsonb, @updatedAtUtc)
            ON CONFLICT (node_id) DO UPDATE
            SET node_type = EXCLUDED.node_type,
                display_name = EXCLUDED.display_name,
                properties = EXCLUDED.properties,
                updated_at_utc = EXCLUDED.updated_at_utc;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("nodeId", node.NodeId);
        cmd.Parameters.AddWithValue("nodeType", node.NodeType);
        cmd.Parameters.AddWithValue("displayName", node.DisplayName);
        cmd.Parameters.AddWithValue("properties", JsonSerializer.Serialize(node.Properties));
        cmd.Parameters.AddWithValue("updatedAtUtc", node.UpdatedAtUtc.UtcDateTime);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async global::System.Threading.Tasks.Task UpsertRelationshipAsync(KnowledgeRelationship relationship, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = $"""
            INSERT INTO {EdgesTable} (relationship_id, from_node_id, relationship_type, to_node_id, properties, updated_at_utc)
            VALUES (@relationshipId, @fromNodeId, @relationshipType, @toNodeId, @properties::jsonb, @updatedAtUtc)
            ON CONFLICT (relationship_id) DO UPDATE
            SET from_node_id = EXCLUDED.from_node_id,
                relationship_type = EXCLUDED.relationship_type,
                to_node_id = EXCLUDED.to_node_id,
                properties = EXCLUDED.properties,
                updated_at_utc = EXCLUDED.updated_at_utc;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("relationshipId", relationship.RelationshipId);
        cmd.Parameters.AddWithValue("fromNodeId", relationship.FromNodeId);
        cmd.Parameters.AddWithValue("relationshipType", relationship.RelationshipType);
        cmd.Parameters.AddWithValue("toNodeId", relationship.ToNodeId);
        cmd.Parameters.AddWithValue("properties", JsonSerializer.Serialize(relationship.Properties));
        cmd.Parameters.AddWithValue("updatedAtUtc", relationship.UpdatedAtUtc.UtcDateTime);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeRelationship>> QueryRelationshipsAsync(string? fromNodeId = null, string? relationshipType = null, string? toNodeId = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = $"""
            SELECT relationship_id, from_node_id, relationship_type, to_node_id, properties, updated_at_utc
            FROM {EdgesTable}
            WHERE (@fromNodeId IS NULL OR from_node_id = @fromNodeId)
              AND (@relationshipType IS NULL OR relationship_type = @relationshipType)
              AND (@toNodeId IS NULL OR to_node_id = @toNodeId)
            ORDER BY updated_at_utc DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("fromNodeId", (object?)fromNodeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("relationshipType", (object?)relationshipType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("toNodeId", (object?)toNodeId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var rows = new List<KnowledgeRelationship>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadRelationship(reader));
        }

        return rows;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> QueryRelatedNodesAsync(string nodeId, string? relationshipType = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = $"""
            SELECT n.node_id, n.node_type, n.display_name, n.properties, n.updated_at_utc
            FROM {NodesTable} n
            INNER JOIN {EdgesTable} e ON n.node_id = e.to_node_id
            WHERE e.from_node_id = @nodeId
              AND (@relationshipType IS NULL OR e.relationship_type = @relationshipType)
            ORDER BY n.updated_at_utc DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("nodeId", nodeId);
        cmd.Parameters.AddWithValue("relationshipType", (object?)relationshipType ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var rows = new List<KnowledgeNode>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadNode(reader));
        }

        return rows;
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

            string bootstrap = $"""
                CREATE SCHEMA IF NOT EXISTS {_schema};

                CREATE TABLE IF NOT EXISTS {NodesTable} (
                    node_id text PRIMARY KEY,
                    node_type text NOT NULL,
                    display_name text NOT NULL,
                    properties jsonb NOT NULL DEFAULT jsonb_build_object(),
                    updated_at_utc timestamptz NOT NULL
                );

                CREATE TABLE IF NOT EXISTS {EdgesTable} (
                    relationship_id text PRIMARY KEY,
                    from_node_id text NOT NULL,
                    relationship_type text NOT NULL,
                    to_node_id text NOT NULL,
                    properties jsonb NOT NULL DEFAULT jsonb_build_object(),
                    updated_at_utc timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_knowledge_edges_from_type ON {EdgesTable}(from_node_id, relationship_type);
                CREATE INDEX IF NOT EXISTS idx_knowledge_edges_to ON {EdgesTable}(to_node_id);
                """;

            await using var cmd = new NpgsqlCommand(bootstrap, connection);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static KnowledgeNode ReadNode(NpgsqlDataReader reader)
    {
        string propertiesJson = reader.GetString(reader.GetOrdinal("properties"));
        var properties = JsonSerializer.Deserialize<Dictionary<string, string>>(propertiesJson) ?? new Dictionary<string, string>();

        return new KnowledgeNode(
            NodeId: reader.GetString(reader.GetOrdinal("node_id")),
            NodeType: reader.GetString(reader.GetOrdinal("node_type")),
            DisplayName: reader.GetString(reader.GetOrdinal("display_name")),
            Properties: properties,
            UpdatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("updated_at_utc")), TimeSpan.Zero));
    }

    private static KnowledgeRelationship ReadRelationship(NpgsqlDataReader reader)
    {
        string propertiesJson = reader.GetString(reader.GetOrdinal("properties"));
        var properties = JsonSerializer.Deserialize<Dictionary<string, string>>(propertiesJson) ?? new Dictionary<string, string>();

        return new KnowledgeRelationship(
            RelationshipId: reader.GetString(reader.GetOrdinal("relationship_id")),
            FromNodeId: reader.GetString(reader.GetOrdinal("from_node_id")),
            RelationshipType: reader.GetString(reader.GetOrdinal("relationship_type")),
            ToNodeId: reader.GetString(reader.GetOrdinal("to_node_id")),
            Properties: properties,
            UpdatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("updated_at_utc")), TimeSpan.Zero));
    }
}
