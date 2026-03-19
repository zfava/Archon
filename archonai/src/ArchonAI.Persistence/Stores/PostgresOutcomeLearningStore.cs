using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Decisions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresOutcomeLearningStore : IOutcomeLearningService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresOutcomeLearningStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TableName => $"{_schema}.outcome_records";

    public PostgresOutcomeLearningStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresOutcomeLearningStore> logger)
    {
        _connectionString = options.Value.ConnectionString
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
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
                    decision_id                 uuid NOT NULL UNIQUE,
                    tenant_id                   uuid NOT NULL,
                    expected_outcome_summary    text,
                    expected_value              numeric,
                    confidence_at_prediction    double precision NOT NULL,
                    expected_timeframe          text,
                    actual_outcome_summary      text,
                    actual_value                numeric,
                    outcome_observed_at_utc     timestamptz,
                    value_variance              numeric,
                    variance_percent            double precision,
                    direction                   int NOT NULL,
                    root_cause                  text,
                    notes                       text,
                    assessment                  int NOT NULL,
                    recalibration_signal        int NOT NULL,
                    recorded_by                 text NOT NULL,
                    created_at_utc              timestamptz NOT NULL,
                    updated_at_utc              timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_outcome_records_decision_id ON {TableName} (decision_id);
                CREATE INDEX IF NOT EXISTS idx_outcome_records_tenant_id   ON {TableName} (tenant_id);
            ";
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
            _logger.LogInformation("PostgresOutcomeLearningStore initialized (schema={Schema}).", _schema);
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── RecordExpectedOutcomeAsync ──────────────────────────────────

    public async Task<OutcomeRecord> RecordExpectedOutcomeAsync(
        Guid decisionId, Guid tenantId,
        string? expectedSummary, decimal? expectedValue,
        double confidenceAtPrediction, string? expectedTimeframe,
        string recordedBy, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var record = new OutcomeRecord(
            Id: Guid.NewGuid(),
            DecisionId: decisionId,
            TenantId: tenantId,
            ExpectedOutcomeSummary: expectedSummary,
            ExpectedValue: expectedValue,
            ConfidenceAtPrediction: confidenceAtPrediction,
            ExpectedTimeframe: expectedTimeframe,
            ActualOutcomeSummary: null,
            ActualValue: null,
            OutcomeObservedAtUtc: null,
            ValueVariance: null,
            VariancePercent: null,
            Direction: OutcomeDirection.Pending,
            RootCause: null,
            Notes: null,
            Assessment: OutcomeAssessment.Pending,
            RecalibrationSignal: RecalibrationSignal.None,
            RecordedBy: recordedBy,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {TableName}
                (id, decision_id, tenant_id, expected_outcome_summary, expected_value,
                 confidence_at_prediction, expected_timeframe, actual_outcome_summary, actual_value,
                 outcome_observed_at_utc, value_variance, variance_percent, direction,
                 root_cause, notes, assessment, recalibration_signal, recorded_by,
                 created_at_utc, updated_at_utc)
            VALUES
                (@id, @decisionId, @tenantId, @expectedSummary, @expectedValue,
                 @confidenceAtPrediction, @expectedTimeframe, @actualSummary, @actualValue,
                 @outcomeObservedAtUtc, @valueVariance, @variancePercent, @direction,
                 @rootCause, @notes, @assessment, @recalibrationSignal, @recordedBy,
                 @createdAtUtc, @updatedAtUtc)
        ", conn);

        AddParameters(cmd, record);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Expected outcome recorded for decision {DecisionId}.", decisionId);
        return record;
    }

    // ── RecordActualOutcomeAsync ────────────────────────────────────

    public async Task<OutcomeRecord> RecordActualOutcomeAsync(
        Guid decisionId, string? actualSummary, decimal? actualValue,
        string? rootCause, string? notes, string recordedBy,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var existing = await GetOutcomeAsync(decisionId, ct)
            ?? throw new InvalidOperationException($"No expected outcome record found for decision {decisionId}.");

        var now = DateTimeOffset.UtcNow;

        // Compute variance
        decimal? variance = null;
        double? variancePercent = null;
        var direction = OutcomeDirection.Pending;
        var assessment = OutcomeAssessment.Pending;
        var signal = RecalibrationSignal.None;

        if (actualValue.HasValue && existing.ExpectedValue.HasValue)
        {
            variance = actualValue.Value - existing.ExpectedValue.Value;
            variancePercent = existing.ExpectedValue.Value != 0m
                ? (double)(Math.Abs(variance.Value) / Math.Abs(existing.ExpectedValue.Value) * 100m)
                : 0.0;

            // Determine direction
            if (variancePercent <= 10.0)
            {
                direction = OutcomeDirection.OnTarget;
                assessment = OutcomeAssessment.AsExpected;
                signal = RecalibrationSignal.ConfidenceCalibrated;
            }
            else if (actualValue.Value > existing.ExpectedValue.Value)
            {
                direction = OutcomeDirection.Overperformed;
                assessment = OutcomeAssessment.BetterThanExpected;
                signal = existing.ConfidenceAtPrediction > 0.8
                    ? RecalibrationSignal.ConfidenceDeflated
                    : RecalibrationSignal.ConfidenceCalibrated;
            }
            else
            {
                direction = OutcomeDirection.Underperformed;
                assessment = variancePercent > 50.0
                    ? OutcomeAssessment.CompletelyMissed
                    : OutcomeAssessment.WorseThanExpected;
                signal = existing.ConfidenceAtPrediction > 0.5
                    ? RecalibrationSignal.ConfidenceInflated
                    : RecalibrationSignal.ValueModelDrift;
            }
        }

        var updated = existing with
        {
            ActualOutcomeSummary = actualSummary,
            ActualValue = actualValue,
            OutcomeObservedAtUtc = now,
            ValueVariance = variance,
            VariancePercent = variancePercent,
            Direction = direction,
            RootCause = rootCause,
            Notes = notes,
            Assessment = assessment,
            RecalibrationSignal = signal,
            RecordedBy = recordedBy,
            UpdatedAtUtc = now,
        };

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {TableName} SET
                actual_outcome_summary  = @actualSummary,
                actual_value            = @actualValue,
                outcome_observed_at_utc = @outcomeObservedAtUtc,
                value_variance          = @valueVariance,
                variance_percent        = @variancePercent,
                direction               = @direction,
                root_cause              = @rootCause,
                notes                   = @notes,
                assessment              = @assessment,
                recalibration_signal    = @recalibrationSignal,
                recorded_by             = @recordedBy,
                updated_at_utc          = @updatedAtUtc
            WHERE decision_id = @decisionId
        ", conn);

        cmd.Parameters.AddWithValue("actualSummary", (object?)updated.ActualOutcomeSummary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actualValue", (object?)updated.ActualValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("outcomeObservedAtUtc", (object?)updated.OutcomeObservedAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("valueVariance", (object?)updated.ValueVariance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("variancePercent", (object?)updated.VariancePercent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("direction", (int)updated.Direction);
        cmd.Parameters.AddWithValue("rootCause", (object?)updated.RootCause ?? DBNull.Value);
        cmd.Parameters.AddWithValue("notes", (object?)updated.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("assessment", (int)updated.Assessment);
        cmd.Parameters.AddWithValue("recalibrationSignal", (int)updated.RecalibrationSignal);
        cmd.Parameters.AddWithValue("recordedBy", updated.RecordedBy);
        cmd.Parameters.AddWithValue("updatedAtUtc", updated.UpdatedAtUtc);
        cmd.Parameters.AddWithValue("decisionId", decisionId);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Actual outcome recorded for decision {DecisionId}: direction={Direction}, assessment={Assessment}.",
            decisionId, updated.Direction, updated.Assessment);
        return updated;
    }

    // ── GetOutcomeAsync ─────────────────────────────────────────────

    public async Task<OutcomeRecord?> GetOutcomeAsync(Guid decisionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {TableName} WHERE decision_id = @decisionId", conn);
        cmd.Parameters.AddWithValue("decisionId", decisionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapOutcome(reader) : null;
    }

    // ── ListOutcomesAsync ───────────────────────────────────────────

    public async Task<IReadOnlyList<OutcomeRecord>> ListOutcomesAsync(
        Guid tenantId, int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {TableName}
            WHERE tenant_id = @tenantId
            ORDER BY created_at_utc DESC
            LIMIT @limit
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<OutcomeRecord>();
        while (await reader.ReadAsync(ct))
            results.Add(MapOutcome(reader));
        return results;
    }

    // ── GetCalibrationSummaryAsync ──────────────────────────────────

    public async Task<CalibrationSummary> GetCalibrationSummaryAsync(
        Guid tenantId, string? domain = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Only count records that have actual outcomes (direction != Pending)
        var whereClause = "WHERE tenant_id = @tenantId AND direction != @pending";
        if (domain is not null) whereClause += " AND EXISTS (SELECT 1)"; // domain is on decision table; filter in app

        await using var cmd = new NpgsqlCommand($@"
            SELECT
                COUNT(*) AS total,
                COUNT(*) FILTER (WHERE direction = @onTarget) AS on_target,
                COUNT(*) FILTER (WHERE direction = @overperformed) AS overperformed,
                COUNT(*) FILTER (WHERE direction = @underperformed) AS underperformed,
                COALESCE(AVG(confidence_at_prediction), 0) AS mean_confidence,
                COALESCE(AVG(variance_percent), 0) AS mean_variance_percent
            FROM {TableName}
            {whereClause}
        ", conn);

        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("pending", (int)OutcomeDirection.Pending);
        cmd.Parameters.AddWithValue("onTarget", (int)OutcomeDirection.OnTarget);
        cmd.Parameters.AddWithValue("overperformed", (int)OutcomeDirection.Overperformed);
        cmd.Parameters.AddWithValue("underperformed", (int)OutcomeDirection.Underperformed);

        int total = 0, onTarget = 0, overperformed = 0, underperformed = 0;
        double meanConfidence = 0, meanVariancePercent = 0;

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                total = reader.GetInt32(reader.GetOrdinal("total"));
                onTarget = reader.GetInt32(reader.GetOrdinal("on_target"));
                overperformed = reader.GetInt32(reader.GetOrdinal("overperformed"));
                underperformed = reader.GetInt32(reader.GetOrdinal("underperformed"));
                meanConfidence = reader.GetDouble(reader.GetOrdinal("mean_confidence"));
                meanVariancePercent = reader.GetDouble(reader.GetOrdinal("mean_variance_percent"));
            }
        }

        double hitRate = total > 0 ? (double)onTarget / total * 100.0 : 0.0;

        // Signal distribution
        await using var sigCmd = new NpgsqlCommand($@"
            SELECT recalibration_signal, COUNT(*) AS cnt
            FROM {TableName}
            WHERE tenant_id = @tenantId AND direction != @pending
            GROUP BY recalibration_signal
        ", conn);
        sigCmd.Parameters.AddWithValue("tenantId", tenantId);
        sigCmd.Parameters.AddWithValue("pending", (int)OutcomeDirection.Pending);

        var signalDistribution = new Dictionary<string, int>();
        await using (var sigReader = await sigCmd.ExecuteReaderAsync(ct))
        {
            while (await sigReader.ReadAsync(ct))
            {
                var signalValue = (RecalibrationSignal)sigReader.GetInt32(sigReader.GetOrdinal("recalibration_signal"));
                var count = sigReader.GetInt32(sigReader.GetOrdinal("cnt"));
                signalDistribution[signalValue.ToString()] = count;
            }
        }

        return new CalibrationSummary(
            TenantId: tenantId.ToString(),
            Domain: domain,
            TotalOutcomes: total,
            OnTarget: onTarget,
            Overperformed: overperformed,
            Underperformed: underperformed,
            MeanConfidenceAtPrediction: Math.Round(meanConfidence, 4),
            HitRate: Math.Round(hitRate, 2),
            MeanVariancePercent: Math.Round(meanVariancePercent, 2),
            SignalDistribution: signalDistribution);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void AddParameters(NpgsqlCommand cmd, OutcomeRecord r)
    {
        cmd.Parameters.AddWithValue("id", r.Id);
        cmd.Parameters.AddWithValue("decisionId", r.DecisionId);
        cmd.Parameters.AddWithValue("tenantId", r.TenantId);
        cmd.Parameters.AddWithValue("expectedSummary", (object?)r.ExpectedOutcomeSummary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("expectedValue", (object?)r.ExpectedValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("confidenceAtPrediction", r.ConfidenceAtPrediction);
        cmd.Parameters.AddWithValue("expectedTimeframe", (object?)r.ExpectedTimeframe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actualSummary", (object?)r.ActualOutcomeSummary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actualValue", (object?)r.ActualValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("outcomeObservedAtUtc", (object?)r.OutcomeObservedAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("valueVariance", (object?)r.ValueVariance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("variancePercent", (object?)r.VariancePercent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("direction", (int)r.Direction);
        cmd.Parameters.AddWithValue("rootCause", (object?)r.RootCause ?? DBNull.Value);
        cmd.Parameters.AddWithValue("notes", (object?)r.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("assessment", (int)r.Assessment);
        cmd.Parameters.AddWithValue("recalibrationSignal", (int)r.RecalibrationSignal);
        cmd.Parameters.AddWithValue("recordedBy", r.RecordedBy);
        cmd.Parameters.AddWithValue("createdAtUtc", r.CreatedAtUtc);
        cmd.Parameters.AddWithValue("updatedAtUtc", r.UpdatedAtUtc);
    }

    private static OutcomeRecord MapOutcome(NpgsqlDataReader r)
    {
        return new OutcomeRecord(
            Id: r.GetGuid(r.GetOrdinal("id")),
            DecisionId: r.GetGuid(r.GetOrdinal("decision_id")),
            TenantId: r.GetGuid(r.GetOrdinal("tenant_id")),
            ExpectedOutcomeSummary: r.IsDBNull(r.GetOrdinal("expected_outcome_summary")) ? null : r.GetString(r.GetOrdinal("expected_outcome_summary")),
            ExpectedValue: r.IsDBNull(r.GetOrdinal("expected_value")) ? null : r.GetDecimal(r.GetOrdinal("expected_value")),
            ConfidenceAtPrediction: r.GetDouble(r.GetOrdinal("confidence_at_prediction")),
            ExpectedTimeframe: r.IsDBNull(r.GetOrdinal("expected_timeframe")) ? null : r.GetString(r.GetOrdinal("expected_timeframe")),
            ActualOutcomeSummary: r.IsDBNull(r.GetOrdinal("actual_outcome_summary")) ? null : r.GetString(r.GetOrdinal("actual_outcome_summary")),
            ActualValue: r.IsDBNull(r.GetOrdinal("actual_value")) ? null : r.GetDecimal(r.GetOrdinal("actual_value")),
            OutcomeObservedAtUtc: r.IsDBNull(r.GetOrdinal("outcome_observed_at_utc")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("outcome_observed_at_utc")),
            ValueVariance: r.IsDBNull(r.GetOrdinal("value_variance")) ? null : r.GetDecimal(r.GetOrdinal("value_variance")),
            VariancePercent: r.IsDBNull(r.GetOrdinal("variance_percent")) ? null : r.GetDouble(r.GetOrdinal("variance_percent")),
            Direction: (OutcomeDirection)r.GetInt32(r.GetOrdinal("direction")),
            RootCause: r.IsDBNull(r.GetOrdinal("root_cause")) ? null : r.GetString(r.GetOrdinal("root_cause")),
            Notes: r.IsDBNull(r.GetOrdinal("notes")) ? null : r.GetString(r.GetOrdinal("notes")),
            Assessment: (OutcomeAssessment)r.GetInt32(r.GetOrdinal("assessment")),
            RecalibrationSignal: (RecalibrationSignal)r.GetInt32(r.GetOrdinal("recalibration_signal")),
            RecordedBy: r.GetString(r.GetOrdinal("recorded_by")),
            CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at_utc")),
            UpdatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("updated_at_utc")));
    }
}
