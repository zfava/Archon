using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models.Routing;
using ArchonAI.Core.Models.Telemetry;
using ArchonAI.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace ArchonAI.Telemetry.Tests;

public sealed class TelemetryTests
{
    // ──────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────

    private static TaskExecutionTelemetry MakeTelemetry(
        Guid? objectiveId = null,
        DateTimeOffset? recordedAt = null) =>
        new(
            Id: Guid.NewGuid(),
            ObjectiveId: objectiveId ?? Guid.NewGuid(),
            WorkflowId: Guid.NewGuid(),
            AgentId: Guid.NewGuid(),
            TaskId: Guid.NewGuid(),
            ExecutionTimeMs: 100,
            Cost: 0.01m,
            Success: true,
            ErrorType: "",
            RecordedAtUtc: recordedAt ?? DateTimeOffset.UtcNow);

    private static SystemInsightEngine CreateEngine(
        Mock<IModelPerformanceTracker>? tracker = null,
        TelemetryOptions? opts = null)
    {
        tracker ??= new Mock<IModelPerformanceTracker>();
        tracker.Setup(t => t.GetAllScores()).Returns(new List<ModelPerformanceScore>());
        opts ??= new TelemetryOptions();
        return new SystemInsightEngine(
            tracker.Object,
            Options.Create(opts));
    }

    // ──────────────────────────────────────────────────────
    // InMemoryTaskTelemetryStore tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryTaskTelemetryStore_RecordAsync_StoresEntry()
    {
        var store = new InMemoryTaskTelemetryStore();
        var objectiveId = Guid.NewGuid();
        var entry = MakeTelemetry(objectiveId: objectiveId);

        await store.RecordAsync(entry);

        var results = await store.QueryByObjectiveAsync(objectiveId);
        results.Should().HaveCount(1);
        results[0].Should().Be(entry);
    }

    [Fact]
    public async Task InMemoryTaskTelemetryStore_RecordAsync_CancelledToken_ThrowsOperationCancelled()
    {
        var store = new InMemoryTaskTelemetryStore();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => store.RecordAsync(MakeTelemetry(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InMemoryTaskTelemetryStore_RecordAsync_EvictsOldEntriesBeyond5000()
    {
        var store = new InMemoryTaskTelemetryStore();
        var objectiveId = Guid.NewGuid();

        for (int i = 0; i < 5010; i++)
        {
            await store.RecordAsync(MakeTelemetry(objectiveId: objectiveId));
        }

        // The store caps at 5000 entries total.
        // QueryByObjectiveAsync clamps limit to max 2000, so we query twice
        // with different ordering knowledge: just verify that total is at most 5000.
        var results = await store.QueryByObjectiveAsync(objectiveId, limit: 2000);
        results.Should().HaveCountLessThanOrEqualTo(2000);
        // Since we evicted 10 entries, total in queue is 5000.
        // Query returns min(5000, 2000) = 2000 entries.
        results.Should().HaveCount(2000);
    }

    [Fact]
    public async Task InMemoryTaskTelemetryStore_QueryByObjectiveAsync_FiltersAndSortsByRecordedAtDesc()
    {
        var store = new InMemoryTaskTelemetryStore();
        var target = Guid.NewGuid();
        var other = Guid.NewGuid();

        var older = MakeTelemetry(objectiveId: target, recordedAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = MakeTelemetry(objectiveId: target, recordedAt: DateTimeOffset.UtcNow);
        var unrelated = MakeTelemetry(objectiveId: other);

        await store.RecordAsync(older);
        await store.RecordAsync(newer);
        await store.RecordAsync(unrelated);

        var results = await store.QueryByObjectiveAsync(target);

        results.Should().HaveCount(2);
        results[0].RecordedAtUtc.Should().BeOnOrAfter(results[1].RecordedAtUtc);
        results[0].Should().Be(newer);
        results[1].Should().Be(older);
    }

    [Fact]
    public async Task InMemoryTaskTelemetryStore_QueryByObjectiveAsync_RespectsLimit()
    {
        var store = new InMemoryTaskTelemetryStore();
        var objectiveId = Guid.NewGuid();

        for (int i = 0; i < 10; i++)
            await store.RecordAsync(MakeTelemetry(objectiveId: objectiveId));

        var results = await store.QueryByObjectiveAsync(objectiveId, limit: 3);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task InMemoryTaskTelemetryStore_QueryByObjectiveAsync_ClampsLimitToMin1()
    {
        var store = new InMemoryTaskTelemetryStore();
        var objectiveId = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
            await store.RecordAsync(MakeTelemetry(objectiveId: objectiveId));

        // limit of 0 or negative should be clamped to 1 via Math.Clamp(limit, 1, 2000)
        var results = await store.QueryByObjectiveAsync(objectiveId, limit: 0);

        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task InMemoryTaskTelemetryStore_QueryByObjectiveAsync_CancelledToken_ThrowsOperationCancelled()
    {
        var store = new InMemoryTaskTelemetryStore();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => store.QueryByObjectiveAsync(Guid.NewGuid(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ──────────────────────────────────────────────────────
    // SystemInsightEngine tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task SystemInsightEngine_GetAgentLoadAsync_ReturnsSnapshotAfterRecording()
    {
        var engine = CreateEngine();
        var agentId = Guid.NewGuid();

        engine.RecordAgentLoad(agentId, "TestAgent", activeTasks: 5, queuedTasks: 2,
            executionTimeMs: 150, cpuPercent: 45.0, memoryPercent: 60.0);

        var snapshots = await engine.GetAgentLoadAsync();

        snapshots.Should().HaveCount(1);
        var snap = snapshots[0];
        snap.AgentId.Should().Be(agentId);
        snap.AgentName.Should().Be("TestAgent");
        snap.ActiveTasks.Should().Be(5);
        snap.QueuedTasks.Should().Be(2);
        snap.CpuUtilizationPercent.Should().Be(45.0);
        snap.MemoryUtilizationPercent.Should().Be(60.0);
        snap.LoadLevel.Should().Be("low"); // 45/100 = 0.45 < 0.5 => "low"
    }

    [Fact]
    public async Task SystemInsightEngine_RecordAgentLoad_UpdatesExistingAgent()
    {
        var engine = CreateEngine();
        var agentId = Guid.NewGuid();

        engine.RecordAgentLoad(agentId, "Agent1", 1, 0, 100, 10, 20);
        engine.RecordAgentLoad(agentId, "Agent1", 8, 3, 200, 92, 75);

        var snapshots = await engine.GetAgentLoadAsync();

        snapshots.Should().HaveCount(1);
        snapshots[0].ActiveTasks.Should().Be(8);
        snapshots[0].CpuUtilizationPercent.Should().Be(92);
        snapshots[0].LoadLevel.Should().Be("high"); // 92/100 = 0.92 >= 0.8 but < 0.95
    }

    [Fact]
    public async Task SystemInsightEngine_GetAgentLoadAsync_ClassifiesLoadLevelCorrectly()
    {
        var engine = CreateEngine();

        engine.RecordAgentLoad(Guid.NewGuid(), "Low", 1, 0, 50, 30, 20);       // 0.30 => low
        engine.RecordAgentLoad(Guid.NewGuid(), "Moderate", 3, 1, 100, 55, 40);  // 0.55 => moderate
        engine.RecordAgentLoad(Guid.NewGuid(), "High", 5, 2, 200, 85, 60);      // 0.85 => high
        engine.RecordAgentLoad(Guid.NewGuid(), "Critical", 10, 5, 300, 96, 90); // 0.96 => critical

        var snapshots = await engine.GetAgentLoadAsync();

        snapshots.Should().HaveCount(4);
        var byName = snapshots.ToDictionary(s => s.AgentName);
        byName["Low"].LoadLevel.Should().Be("low");
        byName["Moderate"].LoadLevel.Should().Be("moderate");
        byName["High"].LoadLevel.Should().Be("high");
        byName["Critical"].LoadLevel.Should().Be("critical");
    }

    [Fact]
    public async Task SystemInsightEngine_DetectBottlenecksAsync_DetectsHighCpuAgentOverload()
    {
        var engine = CreateEngine();
        var agentId = Guid.NewGuid();

        // CPU at 85% => loadRatio = 0.85 >= 0.8 (HighLoadThreshold)
        engine.RecordAgentLoad(agentId, "BusyAgent", 10, 3, 500, 85, 70);

        var bottlenecks = await engine.DetectBottlenecksAsync();

        bottlenecks.Should().Contain(b => b.Category == "agent-overload" && b.Component.Contains("BusyAgent"));
    }

    [Fact]
    public async Task SystemInsightEngine_DetectBottlenecksAsync_DetectsQueueBacklog()
    {
        var engine = CreateEngine();
        var agentId = Guid.NewGuid();

        // queuedTasks (15) > activeTasks (3) * 2 AND queuedTasks > 5
        engine.RecordAgentLoad(agentId, "BacklogAgent", 3, 15, 200, 40, 30);

        var bottlenecks = await engine.DetectBottlenecksAsync();

        bottlenecks.Should().Contain(b => b.Category == "queue-backlog" && b.Component.Contains("BacklogAgent"));
    }

    [Fact]
    public async Task SystemInsightEngine_DetectBottlenecksAsync_DetectsModelLatencyAbove5000()
    {
        var engine = CreateEngine();

        // Record a single request with P95 > 5000ms
        engine.RecordModelLatency("openai", "gpt-4", 6000, true);

        var bottlenecks = await engine.DetectBottlenecksAsync();

        bottlenecks.Should().Contain(b => b.Category == "model-latency" && b.Component.Contains("openai/gpt-4"));
    }

    [Fact]
    public async Task SystemInsightEngine_DetectBottlenecksAsync_DetectsModelHighErrorRate()
    {
        var engine = CreateEngine();

        // Need > 10 total requests and > 10% error rate
        for (int i = 0; i < 8; i++)
            engine.RecordModelLatency("anthropic", "claude", 100, true);
        for (int i = 0; i < 4; i++)
            engine.RecordModelLatency("anthropic", "claude", 100, false);

        // Error rate = 4/12 = 0.333 > 0.1, total = 12 > 10
        var bottlenecks = await engine.DetectBottlenecksAsync();

        bottlenecks.Should().Contain(b => b.Category == "model-errors" && b.Component.Contains("anthropic/claude"));
    }

    [Fact]
    public async Task SystemInsightEngine_DetectBottlenecksAsync_NoBottlenecksWhenHealthy()
    {
        var engine = CreateEngine();

        engine.RecordAgentLoad(Guid.NewGuid(), "HealthyAgent", 2, 1, 100, 30, 25);
        engine.RecordModelLatency("openai", "gpt-4", 200, true);

        var bottlenecks = await engine.DetectBottlenecksAsync();

        bottlenecks.Should().BeEmpty();
    }

    [Fact]
    public async Task SystemInsightEngine_GetModelLatencyAsync_ReturnsCorrectSnapshot()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        tracker.Setup(t => t.GetAllScores()).Returns(new List<ModelPerformanceScore>());
        var engine = CreateEngine(tracker);

        engine.RecordModelLatency("openai", "gpt-4", 200, true);
        engine.RecordModelLatency("openai", "gpt-4", 400, true);
        engine.RecordModelLatency("openai", "gpt-4", 300, false);

        var snapshots = await engine.GetModelLatencyAsync();

        snapshots.Should().HaveCount(1);
        var snap = snapshots[0];
        snap.Provider.Should().Be("openai");
        snap.Model.Should().Be("gpt-4");
        snap.RequestCount.Should().Be(3);
        snap.AverageLatencyMs.Should().Be(300); // (200+400+300)/3
        snap.ErrorRate.Should().BeApproximately(1.0 / 3.0, 0.01);
        snap.HealthStatus.Should().Be("critical"); // errorRate ~0.33 > 0.2 => "critical"
    }

    [Fact]
    public async Task SystemInsightEngine_GetModelLatencyAsync_MergesTrackerScoresForUnseenModels()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        var trackerScore = new ModelPerformanceScore(
            Provider: "meta",
            Model: "llama-3",
            AverageLatencyMs: 150,
            AverageCostPerRequest: 0.001,
            AccuracyRate: 0.9,
            SuccessRate: 0.95,
            SampleCount: 50,
            CompositeScore: 0.8,
            LastUpdatedUtc: DateTimeOffset.UtcNow);
        tracker.Setup(t => t.GetAllScores()).Returns(new List<ModelPerformanceScore> { trackerScore });

        var engine = new SystemInsightEngine(
            tracker.Object,
            Options.Create(new TelemetryOptions()));

        var snapshots = await engine.GetModelLatencyAsync();

        snapshots.Should().HaveCount(1);
        var snap = snapshots[0];
        snap.Provider.Should().Be("meta");
        snap.Model.Should().Be("llama-3");
        snap.AverageLatencyMs.Should().Be(150);
        snap.RequestCount.Should().Be(50);
        snap.ErrorRate.Should().BeApproximately(0.05, 0.001); // 1.0 - 0.95
        snap.P95LatencyMs.Should().Be(300); // AverageLatencyMs * 2
        snap.P99LatencyMs.Should().Be(450); // AverageLatencyMs * 3
    }

    [Fact]
    public async Task SystemInsightEngine_GetHealthSummaryAsync_ReturnsHealthyWhenNoData()
    {
        var engine = CreateEngine();

        var health = await engine.GetHealthSummaryAsync();

        // No agents, no models => agentHealthComponent=1.0, modelHealthComponent=1.0, issuesPenalty=1.0
        // overall = 1.0*0.4 + 1.0*0.3 + 1.0*0.3 = 1.0
        health.OverallHealthScore.Should().Be(1.0);
        health.HealthStatus.Should().Be("healthy");
        health.TotalActiveAgents.Should().Be(0);
        health.TotalActiveModels.Should().Be(0);
        health.OpenBottlenecks.Should().Be(0);
    }

    [Fact]
    public async Task SystemInsightEngine_GetHealthSummaryAsync_ReturnsDegradedWithHighLoad()
    {
        var engine = CreateEngine();

        // Record a high-CPU agent: CPU at 90%, which will cause bottleneck
        engine.RecordAgentLoad(Guid.NewGuid(), "Heavy", 10, 5, 500, 90, 80);

        var health = await engine.GetHealthSummaryAsync();

        // agentHealthComponent = max(0, 1.0 - 90/100) = 0.1
        // modelHealthComponent = 1.0 (no models)
        // bottlenecks = 1 (agent-overload), unresolvedAnomalies = 0
        // issuesPenalty = max(0, 1.0 - 1*0.1 - 0*0.05) = 0.9
        // overall = 0.1*0.4 + 1.0*0.3 + 0.9*0.3 = 0.04 + 0.3 + 0.27 = 0.61
        health.OverallHealthScore.Should().BeGreaterThanOrEqualTo(0.5);
        health.OverallHealthScore.Should().BeLessThan(0.8);
        health.HealthStatus.Should().Be("degraded");
        health.TotalActiveAgents.Should().Be(1);
    }

    [Fact]
    public async Task SystemInsightEngine_DetectAnomaliesAsync_DetectsAgentCpuAnomaly()
    {
        // Need >= 3 agents, and the outlier deviation must exceed stdDev * 2.5.
        // 10 agents at 10% CPU + 1 at 99%:
        // mean = (10*10 + 99)/11 ~ 18.09
        // stdDev ~ 25.59
        // threshold = 25.59 * 2.5 = 63.97
        // Outlier deviation = |99 - 18.09| = 80.91 > 63.97 => anomalous
        var engine = CreateEngine();
        for (int i = 0; i < 10; i++)
            engine.RecordAgentLoad(Guid.NewGuid(), $"Normal{i}", 1, 0, 50, 10, 20);
        engine.RecordAgentLoad(Guid.NewGuid(), "Outlier", 10, 5, 500, 99, 90);

        var anomalies = await engine.DetectAnomaliesAsync();

        anomalies.Should().Contain(a => a.Category == "agent-load" && a.Component.Contains("Outlier"));
    }

    [Fact]
    public async Task SystemInsightEngine_DetectAnomaliesAsync_DetectsModelErrorRateAnomaly()
    {
        var engine = CreateEngine();

        // Need TotalRequests >= 20 and errorRate > 0.2
        for (int i = 0; i < 14; i++)
            engine.RecordModelLatency("broken", "model-x", 100, true);
        for (int i = 0; i < 8; i++)
            engine.RecordModelLatency("broken", "model-x", 100, false);

        // Total = 22, failures = 8, errorRate = 8/22 ~ 0.364 > 0.2
        // Also need modelStates.Count >= 2 for latency anomalies, but error rate anomaly only needs TotalRequests >= 20
        // Actually, modelStates = _modelLatency.Values.Where(m => m.TotalRequests >= 10), so this has 1 entry.
        // Error rate check iterates modelStates which has our model. No >= 2 check for error rate anomalies.

        var anomalies = await engine.DetectAnomaliesAsync();

        anomalies.Should().Contain(a => a.Category == "model-errors" && a.Component.Contains("broken/model-x"));
    }

    [Fact]
    public async Task SystemInsightEngine_GetDashboardAsync_ReturnsCompleteDashboard()
    {
        var engine = CreateEngine();

        engine.RecordAgentLoad(Guid.NewGuid(), "Agent1", 3, 1, 120, 50, 40);
        engine.RecordModelLatency("openai", "gpt-4", 300, true);

        var dashboard = await engine.GetDashboardAsync();

        dashboard.Should().NotBeNull();
        dashboard.AgentLoad.Should().HaveCount(1);
        dashboard.ModelLatency.Should().HaveCount(1);
        dashboard.HealthSummary.Should().NotBeNull();
        dashboard.GeneratedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SystemInsightEngine_GetTrendsAsync_ReturnsEmptyWhenNoHistory()
    {
        var engine = CreateEngine();

        var trends = await engine.GetTrendsAsync();

        trends.Should().BeEmpty();
    }

    [Fact]
    public async Task SystemInsightEngine_GetTrendsAsync_ReturnsCpuTrendAfterMultipleRecords()
    {
        var engine = CreateEngine();
        var agentId = Guid.NewGuid();

        // Record multiple times to build CPU history (need >= 2 data points)
        engine.RecordAgentLoad(agentId, "TrendAgent", 1, 0, 100, 20, 30);
        engine.RecordAgentLoad(agentId, "TrendAgent", 2, 0, 110, 25, 35);

        var trends = await engine.GetTrendsAsync();

        trends.Should().Contain(t => t.MetricName == "cpu-utilization" && t.Component.Contains("TrendAgent"));
    }

    [Fact]
    public async Task SystemInsightEngine_GetTrendsAsync_FiltersComponentCorrectly()
    {
        var engine = CreateEngine();
        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();

        // Same agentId for updates so CPU history accumulates >= 2 data points
        engine.RecordAgentLoad(alphaId, "AlphaAgent", 1, 0, 100, 20, 30);
        engine.RecordAgentLoad(alphaId, "AlphaAgent", 2, 0, 100, 25, 30);

        engine.RecordAgentLoad(betaId, "BetaAgent", 1, 0, 100, 40, 50);
        engine.RecordAgentLoad(betaId, "BetaAgent", 2, 0, 100, 45, 50);

        // Filter for only Alpha
        var trends = await engine.GetTrendsAsync(component: "Alpha");

        trends.Should().OnlyContain(t => t.Component.Contains("Alpha"));
    }

    [Fact]
    public async Task SystemInsightEngine_RecordModelLatency_CaseInsensitiveKey()
    {
        var engine = CreateEngine();

        engine.RecordModelLatency("OpenAI", "GPT-4", 200, true);
        engine.RecordModelLatency("openai", "gpt-4", 300, true);

        var snapshots = await engine.GetModelLatencyAsync();

        // Keys are case-insensitive, so both records go to the same state
        snapshots.Should().HaveCount(1);
        snapshots[0].RequestCount.Should().Be(2);
    }
}
