using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ProofAnalytics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresProofAnalyticsStore : IProofAnalyticsService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresProofAnalyticsStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PostgresProofAnalyticsStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresProofAnalyticsStore> logger)
    {
        _connectionString = options.Value.ConnectionString
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    private string EventsTable => $"{_schema}.proof_events";

    // ── Initialization ──────────────────────────────────────────

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

                CREATE TABLE IF NOT EXISTS {EventsTable} (
                    id                  uuid PRIMARY KEY,
                    tenant_id           uuid NOT NULL,
                    decision_id         uuid NOT NULL,
                    workflow_id         uuid,
                    event_type          int NOT NULL,
                    actor               text NOT NULL,
                    detail              text,
                    expected_value      numeric,
                    actual_value        numeric,
                    variance            numeric,
                    variance_percent    double precision,
                    action_type         text,
                    is_success          bool,
                    override_reason     text,
                    economic_impact     numeric,
                    impact_attribution  text,
                    occurred_at_utc     timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_proof_events_decision_id   ON {EventsTable} (decision_id);
                CREATE INDEX IF NOT EXISTS idx_proof_events_workflow_id   ON {EventsTable} (workflow_id);
                CREATE INDEX IF NOT EXISTS idx_proof_events_tenant_id     ON {EventsTable} (tenant_id);
                CREATE INDEX IF NOT EXISTS idx_proof_events_event_type    ON {EventsTable} (event_type);
                CREATE INDEX IF NOT EXISTS idx_proof_events_occurred_at   ON {EventsTable} (occurred_at_utc DESC);
            ";
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
            _logger.LogInformation("PostgresProofAnalyticsStore initialized (schema={Schema}).", _schema);
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── RecordEventAsync ────────────────────────────────────────

    public async Task<ProofEvent> RecordEventAsync(ProofEvent proofEvent, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {EventsTable}
                (id, tenant_id, decision_id, workflow_id, event_type, actor, detail,
                 expected_value, actual_value, variance, variance_percent,
                 action_type, is_success, override_reason, economic_impact, impact_attribution, occurred_at_utc)
            VALUES
                (@id, @tenantId, @decisionId, @workflowId, @eventType, @actor, @detail,
                 @expectedValue, @actualValue, @variance, @variancePercent,
                 @actionType, @isSuccess, @overrideReason, @economicImpact, @impactAttribution, @occurredAtUtc)
        ", conn);

        cmd.Parameters.AddWithValue("id", proofEvent.Id);
        cmd.Parameters.AddWithValue("tenantId", proofEvent.TenantId);
        cmd.Parameters.AddWithValue("decisionId", proofEvent.DecisionId);
        cmd.Parameters.AddWithValue("workflowId", (object?)proofEvent.WorkflowId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eventType", (int)proofEvent.EventType);
        cmd.Parameters.AddWithValue("actor", proofEvent.Actor);
        cmd.Parameters.AddWithValue("detail", (object?)proofEvent.Detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("expectedValue", (object?)proofEvent.ExpectedValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actualValue", (object?)proofEvent.ActualValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("variance", (object?)proofEvent.Variance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("variancePercent", (object?)proofEvent.VariancePercent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actionType", (object?)proofEvent.ActionType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isSuccess", (object?)proofEvent.IsSuccess ?? DBNull.Value);
        cmd.Parameters.AddWithValue("overrideReason", (object?)proofEvent.OverrideReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("economicImpact", (object?)proofEvent.EconomicImpact ?? DBNull.Value);
        cmd.Parameters.AddWithValue("impactAttribution", (object?)proofEvent.ImpactAttribution ?? DBNull.Value);
        cmd.Parameters.AddWithValue("occurredAtUtc", proofEvent.OccurredAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Proof event {EventId} recorded for decision {DecisionId}.", proofEvent.Id, proofEvent.DecisionId);
        return proofEvent;
    }

    // ── GetTimelineAsync ────────────────────────────────────────

    public async Task<ProofTimeline?> GetTimelineAsync(Guid decisionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {EventsTable}
            WHERE decision_id = @decisionId
            ORDER BY occurred_at_utc ASC
        ", conn);
        cmd.Parameters.AddWithValue("decisionId", decisionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var events = new List<ProofEvent>();
        while (await reader.ReadAsync(ct))
            events.Add(MapEvent(reader));

        if (events.Count == 0) return null;

        var summary = BuildTimelineSummary(events);
        var first = events[0];

        return new ProofTimeline(
            decisionId, first.TenantId,
            first.Detail ?? "Decision",
            first.ActionType ?? "unknown",
            events, summary);
    }

    // ── GetWorkflowTimelinesAsync ───────────────────────────────

    public async Task<IReadOnlyList<ProofTimeline>> GetWorkflowTimelinesAsync(Guid workflowId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Find all distinct decision_ids for this workflow
        await using var decIdCmd = new NpgsqlCommand($@"
            SELECT DISTINCT decision_id FROM {EventsTable}
            WHERE workflow_id = @workflowId
        ", conn);
        decIdCmd.Parameters.AddWithValue("workflowId", workflowId);

        var decisionIds = new List<Guid>();
        await using (var reader = await decIdCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                decisionIds.Add(reader.GetGuid(0));
        }

        var timelines = new List<ProofTimeline>();
        foreach (var decId in decisionIds)
        {
            var timeline = await GetTimelineAsync(decId, ct);
            if (timeline is not null)
                timelines.Add(timeline);
        }

        return timelines;
    }

    // ── GetPredictedVsActualAsync ───────────────────────────────

    public async Task<PredictedVsActualSummary> GetPredictedVsActualAsync(
        Guid tenantId, string? domain = null, int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Get DecisionCreated events
        var sql = $@"
            SELECT * FROM {EventsTable}
            WHERE tenant_id = @tenantId
              AND event_type IN (@created, @actual)
        ";
        if (domain is not null) sql += " AND action_type = @domain";
        sql += " ORDER BY occurred_at_utc ASC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("created", (int)ProofEventType.DecisionCreated);
        cmd.Parameters.AddWithValue("actual", (int)ProofEventType.ActualOutcomeRecorded);
        if (domain is not null) cmd.Parameters.AddWithValue("domain", domain);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var eventsByDecision = new Dictionary<Guid, List<ProofEvent>>();
        while (await reader.ReadAsync(ct))
        {
            var ev = MapEvent(reader);
            if (!eventsByDecision.ContainsKey(ev.DecisionId))
                eventsByDecision[ev.DecisionId] = new List<ProofEvent>();
            eventsByDecision[ev.DecisionId].Add(ev);
        }

        var entries = new List<PredictedVsActualEntry>();
        int withOutcomes = 0, onTarget = 0, overperformed = 0, underperformed = 0;
        decimal totalPredicted = 0, totalActual = 0, totalVariance = 0;
        var variancePercents = new List<double>();

        foreach (var (decId, evts) in eventsByDecision.Take(limit))
        {
            var created = evts.FirstOrDefault(e => e.EventType == ProofEventType.DecisionCreated);
            var outcome = evts.FirstOrDefault(e => e.EventType == ProofEventType.ActualOutcomeRecorded);

            var predicted = created?.ExpectedValue;
            var actual = outcome?.ActualValue;
            decimal? variance = null;
            double? variancePct = null;
            var direction = "Pending";

            if (predicted.HasValue && actual.HasValue)
            {
                withOutcomes++;
                variance = actual.Value - predicted.Value;
                variancePct = predicted.Value != 0
                    ? (double)(variance.Value / predicted.Value) * 100.0
                    : 0;

                totalPredicted += predicted.Value;
                totalActual += actual.Value;
                totalVariance += variance.Value;
                variancePercents.Add(variancePct.Value);

                if (Math.Abs(variancePct.Value) <= 10)
                {
                    direction = "OnTarget";
                    onTarget++;
                }
                else if (variancePct.Value > 10)
                {
                    direction = "Overperformed";
                    overperformed++;
                }
                else
                {
                    direction = "Underperformed";
                    underperformed++;
                }
            }
            else if (outcome is not null)
            {
                withOutcomes++;
                direction = "Observed";
            }

            entries.Add(new PredictedVsActualEntry(
                decId,
                created?.Detail ?? "Decision",
                created?.ActionType ?? domain ?? "unknown",
                predicted, actual, variance, variancePct, direction,
                created?.OccurredAtUtc ?? DateTimeOffset.MinValue,
                outcome?.OccurredAtUtc));
        }

        var meanVariance = variancePercents.Count > 0 ? variancePercents.Average() : 0;
        var medianVariance = variancePercents.Count > 0
            ? variancePercents.OrderBy(v => v).ElementAt(variancePercents.Count / 2)
            : 0;
        var accuracyRate = withOutcomes > 0 ? (double)onTarget / withOutcomes : 0;

        return new PredictedVsActualSummary(
            tenantId, domain, eventsByDecision.Count, withOutcomes,
            onTarget, overperformed, underperformed,
            meanVariance, medianVariance,
            totalPredicted, totalActual, totalVariance,
            accuracyRate, entries);
    }

    // ── GetApprovalConversionAsync ──────────────────────────────

    public async Task<ApprovalConversionSummary> GetApprovalConversionAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {EventsTable}
            WHERE tenant_id = @tenantId
              AND event_type IN (@requested, @granted, @denied, @executed)
            ORDER BY occurred_at_utc ASC
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("requested", (int)ProofEventType.ApprovalRequested);
        cmd.Parameters.AddWithValue("granted", (int)ProofEventType.ApprovalGranted);
        cmd.Parameters.AddWithValue("denied", (int)ProofEventType.ApprovalDenied);
        cmd.Parameters.AddWithValue("executed", (int)ProofEventType.ActionExecuted);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var events = new List<ProofEvent>();
        while (await reader.ReadAsync(ct))
            events.Add(MapEvent(reader));

        var requested = events.Count(e => e.EventType == ProofEventType.ApprovalRequested);
        var granted = events.Count(e => e.EventType == ProofEventType.ApprovalGranted);
        var denied = events.Count(e => e.EventType == ProofEventType.ApprovalDenied);
        var executedAfter = events.Count(e => e.EventType == ProofEventType.ActionExecuted);
        var pending = granted - executedAfter;
        if (pending < 0) pending = 0;

        var approvalRate = requested > 0 ? (double)granted / requested : 0;
        var executionConversion = granted > 0 ? (double)executedAfter / granted : 0;

        // Mean approval latency
        TimeSpan? meanLatency = null;
        var latencies = new List<TimeSpan>();
        var requestedByDecision = events.Where(e => e.EventType == ProofEventType.ApprovalRequested)
            .ToDictionary(e => e.DecisionId, e => e.OccurredAtUtc);
        foreach (var grantedEvt in events.Where(e => e.EventType == ProofEventType.ApprovalGranted))
        {
            if (requestedByDecision.TryGetValue(grantedEvt.DecisionId, out var reqTime))
                latencies.Add(grantedEvt.OccurredAtUtc - reqTime);
        }
        if (latencies.Count > 0)
            meanLatency = TimeSpan.FromTicks((long)latencies.Average(l => l.Ticks));

        // By action type
        var byType = new Dictionary<string, ApprovalConversionByType>();
        foreach (var group in events.GroupBy(e => e.ActionType ?? "unknown"))
        {
            var gRequested = group.Count(e => e.EventType == ProofEventType.ApprovalRequested);
            var gGranted = group.Count(e => e.EventType == ProofEventType.ApprovalGranted);
            var gDenied = group.Count(e => e.EventType == ProofEventType.ApprovalDenied);
            var gExecuted = group.Count(e => e.EventType == ProofEventType.ActionExecuted);
            byType[group.Key] = new ApprovalConversionByType(
                group.Key, gRequested, gGranted, gDenied, gExecuted,
                gRequested > 0 ? (double)gGranted / gRequested : 0,
                gGranted > 0 ? (double)gExecuted / gGranted : 0);
        }

        return new ApprovalConversionSummary(
            tenantId, requested, granted, denied, executedAfter, pending,
            approvalRate, executionConversion, meanLatency, byType);
    }

    // ── GetExecutionTrendsAsync ─────────────────────────────────

    public async Task<ExecutionTrendSummary> GetExecutionTrendsAsync(
        Guid tenantId, int bucketCount = 10, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {EventsTable}
            WHERE tenant_id = @tenantId
              AND event_type = @executed
            ORDER BY occurred_at_utc ASC
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("executed", (int)ProofEventType.ActionExecuted);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var events = new List<ProofEvent>();
        while (await reader.ReadAsync(ct))
            events.Add(MapEvent(reader));

        var totalExecutions = events.Count;
        var successes = events.Count(e => e.IsSuccess == true);
        var failures = events.Count(e => e.IsSuccess == false);
        var successRate = totalExecutions > 0 ? (double)successes / totalExecutions : 0;

        var buckets = new List<ExecutionTrendBucket>();
        if (events.Count > 0)
        {
            var minTime = events.Min(e => e.OccurredAtUtc);
            var maxTime = events.Max(e => e.OccurredAtUtc);
            var span = maxTime - minTime;
            var bucketSize = span.Ticks > 0
                ? TimeSpan.FromTicks(span.Ticks / bucketCount)
                : TimeSpan.FromHours(1);

            for (int i = 0; i < bucketCount; i++)
            {
                var start = minTime + TimeSpan.FromTicks(bucketSize.Ticks * i);
                var end = i < bucketCount - 1
                    ? minTime + TimeSpan.FromTicks(bucketSize.Ticks * (i + 1))
                    : maxTime.AddSeconds(1);

                var bucketEvents = events.Where(e => e.OccurredAtUtc >= start && e.OccurredAtUtc < end).ToList();
                var bSuccesses = bucketEvents.Count(e => e.IsSuccess == true);
                var bFailures = bucketEvents.Count(e => e.IsSuccess == false);
                var bRate = bucketEvents.Count > 0 ? (double)bSuccesses / bucketEvents.Count : 0;

                buckets.Add(new ExecutionTrendBucket(start, end, bucketEvents.Count, bSuccesses, bFailures, bRate));
            }
        }

        return new ExecutionTrendSummary(tenantId, totalExecutions, successes, failures, successRate, buckets);
    }

    // ── GetOverrideRatesAsync ───────────────────────────────────

    public async Task<OverrideRateSummary> GetOverrideRatesAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Total decisions
        await using var totalCmd = new NpgsqlCommand($@"
            SELECT COUNT(DISTINCT decision_id) FROM {EventsTable}
            WHERE tenant_id = @tenantId AND event_type = @created
        ", conn);
        totalCmd.Parameters.AddWithValue("tenantId", tenantId);
        totalCmd.Parameters.AddWithValue("created", (int)ProofEventType.DecisionCreated);
        var totalDecisions = Convert.ToInt32(await totalCmd.ExecuteScalarAsync(ct));

        // Override and reversal events
        await using var orCmd = new NpgsqlCommand($@"
            SELECT * FROM {EventsTable}
            WHERE tenant_id = @tenantId
              AND event_type IN (@override, @reversal)
        ", conn);
        orCmd.Parameters.AddWithValue("tenantId", tenantId);
        orCmd.Parameters.AddWithValue("override", (int)ProofEventType.OverrideApplied);
        orCmd.Parameters.AddWithValue("reversal", (int)ProofEventType.ReversalApplied);

        await using var reader = await orCmd.ExecuteReaderAsync(ct);
        var overrideEvents = new List<ProofEvent>();
        while (await reader.ReadAsync(ct))
            overrideEvents.Add(MapEvent(reader));

        var overrides = overrideEvents.Count(e => e.EventType == ProofEventType.OverrideApplied);
        var reversals = overrideEvents.Count(e => e.EventType == ProofEventType.ReversalApplied);
        var overrideRate = totalDecisions > 0 ? (double)overrides / totalDecisions : 0;
        var reversalRate = totalDecisions > 0 ? (double)reversals / totalDecisions : 0;

        var reasonDistribution = overrideEvents
            .Where(e => e.OverrideReason is not null)
            .GroupBy(e => e.OverrideReason!)
            .ToDictionary(g => g.Key, g => g.Count());

        return new OverrideRateSummary(tenantId, totalDecisions, overrides, reversals,
            overrideRate, reversalRate, reasonDistribution);
    }

    // ── GetTrustAnalyticsAsync ──────────────────────────────────

    public async Task<TrustAnalyticsSummary> GetTrustAnalyticsAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {EventsTable}
            WHERE tenant_id = @tenantId
            ORDER BY occurred_at_utc ASC
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var allEvents = new List<ProofEvent>();
        while (await reader.ReadAsync(ct))
            allEvents.Add(MapEvent(reader));

        var byActionType = allEvents
            .GroupBy(e => e.ActionType ?? "unknown")
            .Select(group =>
            {
                var decisions = group.Where(e => e.EventType == ProofEventType.DecisionCreated).ToList();
                var outcomes = group.Where(e => e.EventType == ProofEventType.ActualOutcomeRecorded).ToList();
                var overrides = group.Count(e => e.EventType == ProofEventType.OverrideApplied);
                var totalDec = decisions.Count;
                var withOutcomes = outcomes.Count;

                // Accuracy: decisions with on-target outcomes
                var onTarget = outcomes.Count(e =>
                    e.VariancePercent.HasValue && Math.Abs(e.VariancePercent.Value) <= 10);
                var accuracyRate = withOutcomes > 0 ? (double)onTarget / withOutcomes : 0;
                var overrideRate = totalDec > 0 ? (double)overrides / totalDec : 0;
                var meanConfidence = decisions.Count > 0
                    ? decisions.Where(e => e.ExpectedValue.HasValue).Select(e => (double)e.ExpectedValue!.Value).DefaultIfEmpty(0).Average()
                    : 0;
                var meanVariancePct = outcomes
                    .Where(e => e.VariancePercent.HasValue)
                    .Select(e => e.VariancePercent!.Value)
                    .DefaultIfEmpty(0)
                    .Average();

                var grade = ComputeGrade(accuracyRate, overrideRate);

                return new TrustByActionType(
                    group.Key, totalDec, withOutcomes,
                    accuracyRate, overrideRate, meanConfidence, meanVariancePct, grade);
            })
            .ToList();

        return new TrustAnalyticsSummary(tenantId, byActionType);
    }

    // ── GetDashboardAsync ───────────────────────────────────────

    public async Task<ProofDashboard> GetDashboardAsync(
        Guid tenantId, string? domain = null, CancellationToken ct = default)
    {
        var predictedVsActual = await GetPredictedVsActualAsync(tenantId, domain, ct: ct);
        var approvalConversion = await GetApprovalConversionAsync(tenantId, ct);
        var executionTrends = await GetExecutionTrendsAsync(tenantId, ct: ct);
        var overrideRates = await GetOverrideRatesAsync(tenantId, ct);
        var trustAnalytics = await GetTrustAnalyticsAsync(tenantId, ct);

        return new ProofDashboard(
            tenantId, predictedVsActual, approvalConversion,
            executionTrends, overrideRates, trustAnalytics,
            DateTimeOffset.UtcNow);
    }

    // ── Grade Logic ─────────────────────────────────────────────

    private static string ComputeGrade(double accuracy, double overrideRate)
    {
        if (accuracy >= 0.9 && overrideRate <= 0.05) return "A";
        if (accuracy >= 0.75 && overrideRate <= 0.1) return "B";
        if (accuracy >= 0.6) return "C";
        if (accuracy >= 0.4) return "D";
        return "F";
    }

    // ── Timeline Summary Builder ────────────────────────────────

    private static ProofTimelineSummary BuildTimelineSummary(List<ProofEvent> events)
    {
        var hasOutcome = events.Any(e => e.EventType == ProofEventType.ActualOutcomeRecorded);
        var wasOverridden = events.Any(e => e.EventType == ProofEventType.OverrideApplied);
        var wasReversed = events.Any(e => e.EventType == ProofEventType.ReversalApplied);

        var created = events.FirstOrDefault(e => e.EventType == ProofEventType.DecisionCreated);
        var outcome = events.FirstOrDefault(e => e.EventType == ProofEventType.ActualOutcomeRecorded);

        decimal? predicted = created?.ExpectedValue;
        decimal? actual = outcome?.ActualValue;
        decimal? variance = (predicted.HasValue && actual.HasValue) ? actual.Value - predicted.Value : null;
        double? variancePct = (predicted.HasValue && actual.HasValue && predicted.Value != 0)
            ? (double)(variance!.Value / predicted.Value) * 100.0
            : null;

        TimeSpan? duration = (created is not null && outcome is not null)
            ? outcome.OccurredAtUtc - created.OccurredAtUtc
            : null;

        string? assessment = null;
        if (hasOutcome && variancePct.HasValue)
        {
            assessment = Math.Abs(variancePct.Value) <= 10
                ? "On target"
                : variancePct.Value > 0 ? "Overperformed" : "Underperformed";
        }

        return new ProofTimelineSummary(
            events.Count, hasOutcome, wasOverridden, wasReversed,
            predicted, actual, variance, variancePct,
            assessment, duration);
    }

    // ── Row Mapper ──────────────────────────────────────────────

    private static ProofEvent MapEvent(NpgsqlDataReader reader)
    {
        return new ProofEvent(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetGuid(reader.GetOrdinal("tenant_id")),
            reader.GetGuid(reader.GetOrdinal("decision_id")),
            reader.IsDBNull(reader.GetOrdinal("workflow_id")) ? null : reader.GetGuid(reader.GetOrdinal("workflow_id")),
            (ProofEventType)reader.GetInt32(reader.GetOrdinal("event_type")),
            reader.GetString(reader.GetOrdinal("actor")),
            reader.IsDBNull(reader.GetOrdinal("detail")) ? null : reader.GetString(reader.GetOrdinal("detail")),
            reader.IsDBNull(reader.GetOrdinal("expected_value")) ? null : reader.GetDecimal(reader.GetOrdinal("expected_value")),
            reader.IsDBNull(reader.GetOrdinal("actual_value")) ? null : reader.GetDecimal(reader.GetOrdinal("actual_value")),
            reader.IsDBNull(reader.GetOrdinal("variance")) ? null : reader.GetDecimal(reader.GetOrdinal("variance")),
            reader.IsDBNull(reader.GetOrdinal("variance_percent")) ? null : reader.GetDouble(reader.GetOrdinal("variance_percent")),
            reader.IsDBNull(reader.GetOrdinal("action_type")) ? null : reader.GetString(reader.GetOrdinal("action_type")),
            reader.IsDBNull(reader.GetOrdinal("is_success")) ? null : reader.GetBoolean(reader.GetOrdinal("is_success")),
            reader.IsDBNull(reader.GetOrdinal("override_reason")) ? null : reader.GetString(reader.GetOrdinal("override_reason")),
            reader.IsDBNull(reader.GetOrdinal("economic_impact")) ? null : reader.GetDecimal(reader.GetOrdinal("economic_impact")),
            reader.IsDBNull(reader.GetOrdinal("impact_attribution")) ? null : reader.GetString(reader.GetOrdinal("impact_attribution")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("occurred_at_utc")));
    }
}
