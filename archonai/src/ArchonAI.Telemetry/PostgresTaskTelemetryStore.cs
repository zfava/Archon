using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Telemetry;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Telemetry;

public sealed class PostgresTaskTelemetryStore : ITaskTelemetryStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TableName => $"{_schema}.task_telemetry";

    public PostgresTaskTelemetryStore(IOptions<TelemetryOptions> options)
    {
        _connectionString = options.Value.ConnectionString ?? throw new InvalidOperationException("Telemetry connection string is not configured.");
        _schema = options.Value.Schema;
    }

    public async global::System.Threading.Tasks.Task RecordAsync(TaskExecutionTelemetry telemetry, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            INSERT INTO {TableName}
            (id, objective_id, workflow_id, agent_id, task_id, execution_time_ms, cost, success, error_type, recorded_at_utc)
            VALUES
            (@id, @objectiveId, @workflowId, @agentId, @taskId, @executionTimeMs, @cost, @success, @errorType, @recordedAtUtc);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", telemetry.Id);
        command.Parameters.AddWithValue("objectiveId", telemetry.ObjectiveId);
        command.Parameters.AddWithValue("workflowId", telemetry.WorkflowId);
        command.Parameters.AddWithValue("agentId", telemetry.AgentId);
        command.Parameters.AddWithValue("taskId", telemetry.TaskId);
        command.Parameters.AddWithValue("executionTimeMs", telemetry.ExecutionTimeMs);
        command.Parameters.AddWithValue("cost", telemetry.Cost);
        command.Parameters.AddWithValue("success", telemetry.Success);
        command.Parameters.AddWithValue("errorType", telemetry.ErrorType);
        command.Parameters.AddWithValue("recordedAtUtc", telemetry.RecordedAtUtc.UtcDateTime);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<TaskExecutionTelemetry>> QueryByObjectiveAsync(
        Guid objectiveId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT id, objective_id, workflow_id, agent_id, task_id, execution_time_ms, cost, success, error_type, recorded_at_utc
            FROM {TableName}
            WHERE objective_id = @objectiveId
            ORDER BY recorded_at_utc DESC
            LIMIT @limit;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("objectiveId", objectiveId);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 2000));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<TaskExecutionTelemetry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TaskExecutionTelemetry(
                Id: reader.GetGuid(reader.GetOrdinal("id")),
                ObjectiveId: reader.GetGuid(reader.GetOrdinal("objective_id")),
                WorkflowId: reader.GetGuid(reader.GetOrdinal("workflow_id")),
                AgentId: reader.GetGuid(reader.GetOrdinal("agent_id")),
                TaskId: reader.GetGuid(reader.GetOrdinal("task_id")),
                ExecutionTimeMs: reader.GetDouble(reader.GetOrdinal("execution_time_ms")),
                Cost: reader.GetDecimal(reader.GetOrdinal("cost")),
                Success: reader.GetBoolean(reader.GetOrdinal("success")),
                ErrorType: reader.GetString(reader.GetOrdinal("error_type")),
                RecordedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("recorded_at_utc")), TimeSpan.Zero)
            ));
        }

        return result;
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
                CREATE TABLE IF NOT EXISTS {TableName} (
                    id uuid PRIMARY KEY,
                    objective_id uuid NOT NULL,
                    workflow_id uuid NOT NULL,
                    agent_id uuid NOT NULL,
                    task_id uuid NOT NULL,
                    execution_time_ms double precision NOT NULL,
                    cost numeric(18,6) NOT NULL,
                    success boolean NOT NULL,
                    error_type text NOT NULL,
                    recorded_at_utc timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_task_telemetry_objective_id ON {TableName}(objective_id);
                CREATE INDEX IF NOT EXISTS idx_task_telemetry_task_id ON {TableName}(task_id);
                CREATE INDEX IF NOT EXISTS idx_task_telemetry_recorded_at_utc ON {TableName}(recorded_at_utc DESC);
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
