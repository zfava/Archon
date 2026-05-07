using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Patterns;
using ArchonAI.Core.Models.Telemetry;
using ArchonAI.PatternDiscovery;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace ArchonAI.PatternDiscovery.Tests;

public sealed class PatternDiscoveryTests
{
    private readonly Mock<ITaskTelemetryStore> _telemetryStore = new();
    private readonly Mock<IMemoryStore> _memoryStore = new();
    private readonly Mock<ILearningEngine> _learningEngine = new();
    private readonly Mock<IMultiTenantContext> _tenantContext = new();
    private readonly PatternDiscoveryOptions _options = new();

    public PatternDiscoveryTests()
    {
        _tenantContext.Setup(x => x.CurrentTenantId).Returns("test-tenant");
    }

    private PatternDiscoveryEngine CreateEngine() =>
        new(
            _telemetryStore.Object,
            _memoryStore.Object,
            _learningEngine.Object,
            _tenantContext.Object,
            Options.Create(_options));

    private static TaskExecutionTelemetry MakeTelemetry(
        Guid? objectiveId = null,
        Guid? workflowId = null,
        Guid? agentId = null,
        double executionTimeMs = 100,
        decimal cost = 0.01m,
        bool success = true,
        string errorType = "none") =>
        new(
            Id: Guid.NewGuid(),
            ObjectiveId: objectiveId ?? Guid.NewGuid(),
            WorkflowId: workflowId ?? Guid.NewGuid(),
            AgentId: agentId ?? Guid.NewGuid(),
            TaskId: Guid.NewGuid(),
            ExecutionTimeMs: executionTimeMs,
            Cost: cost,
            Success: success,
            ErrorType: errorType,
            RecordedAtUtc: DateTimeOffset.UtcNow);

    private void SetupTelemetry(Guid objectiveId, IReadOnlyList<TaskExecutionTelemetry> telemetry)
    {
        _telemetryStore
            .Setup(x => x.QueryByObjectiveAsync(objectiveId, 2000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(telemetry);
    }

    // --- PatternDiscoveryOptions tests ---

    [Fact]
    public void PatternDiscoveryOptions_Defaults_HaveExpectedValues()
    {
        var options = new PatternDiscoveryOptions();

        options.MinRecurringFailures.Should().Be(3);
        options.HighExecutionLatencyThresholdMs.Should().Be(1800);
        options.MinExecutionsForRanking.Should().Be(3);
        options.StrategyCandidateMinSuccesses.Should().Be(3);
        options.IntelligenceScopePrefix.Should().Be("intelligence:pattern-discovery");
    }

    [Fact]
    public void PatternDiscoveryOptions_SetProperties_RetainsValues()
    {
        var options = new PatternDiscoveryOptions
        {
            MinRecurringFailures = 10,
            HighExecutionLatencyThresholdMs = 5000,
            MinExecutionsForRanking = 7,
            StrategyCandidateMinSuccesses = 12,
            IntelligenceScopePrefix = "custom-prefix"
        };

        options.MinRecurringFailures.Should().Be(10);
        options.HighExecutionLatencyThresholdMs.Should().Be(5000);
        options.MinExecutionsForRanking.Should().Be(7);
        options.StrategyCandidateMinSuccesses.Should().Be(12);
        options.IntelligenceScopePrefix.Should().Be("custom-prefix");
    }

    // --- DiscoverObjectivePatternsAsync: empty telemetry ---

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_NoTelemetry_ReturnsEmpty()
    {
        var objectiveId = Guid.NewGuid();
        SetupTelemetry(objectiveId, Array.Empty<TaskExecutionTelemetry>());

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        result.Should().BeEmpty();
        _memoryStore.Verify(
            x => x.SaveAsync(It.IsAny<MemoryRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _learningEngine.Verify(
            x => x.IngestPatternsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<OperationalPattern>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- DetectFailures ---

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_RecurringFailures_DetectsFailurePattern()
    {
        var objectiveId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // Create 3 failures with the same ErrorType (meets default MinRecurringFailures=3)
        var telemetry = Enumerable.Range(0, 3)
            .Select(_ => MakeTelemetry(
                objectiveId: objectiveId,
                workflowId: workflowId,
                agentId: agentId,
                success: false,
                errorType: "TimeoutError",
                executionTimeMs: 50,
                cost: 0.01m))
            .ToList();

        // Add a success to avoid only failures (agent ranking needs data)
        telemetry.AddRange(Enumerable.Range(0, 3).Select(_ =>
            MakeTelemetry(objectiveId: objectiveId, workflowId: workflowId, agentId: agentId,
                success: true, errorType: "none", executionTimeMs: 50, cost: 0.01m)));

        SetupTelemetry(objectiveId, telemetry);

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        var failurePatterns = result.Where(p => p.PatternType == "failure-detection").ToList();
        failurePatterns.Should().HaveCount(1);

        var pattern = failurePatterns[0];
        pattern.ObjectiveId.Should().Be(objectiveId);
        pattern.Title.Should().Be("Recurring failure pattern detected");
        pattern.Description.Should().Contain("TimeoutError");
        pattern.Description.Should().Contain("3");
        pattern.Score.Should().Be(3);
        pattern.Metadata["errorType"].Should().Be("TimeoutError");
        pattern.Metadata["occurrences"].Should().Be("3");
    }

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_FailuresBelowThreshold_NoFailurePattern()
    {
        var objectiveId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // Only 2 failures with same ErrorType (below default MinRecurringFailures=3)
        var telemetry = new List<TaskExecutionTelemetry>
        {
            MakeTelemetry(objectiveId: objectiveId, workflowId: workflowId, agentId: agentId,
                success: false, errorType: "TimeoutError"),
            MakeTelemetry(objectiveId: objectiveId, workflowId: workflowId, agentId: agentId,
                success: false, errorType: "TimeoutError"),
            MakeTelemetry(objectiveId: objectiveId, workflowId: workflowId, agentId: agentId,
                success: true, errorType: "none")
        };

        SetupTelemetry(objectiveId, telemetry);

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        result.Where(p => p.PatternType == "failure-detection").Should().BeEmpty();
    }

    // --- WorkflowOptimization ---

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_HighLatencyWorkflow_DetectsOptimizationOpportunity()
    {
        var objectiveId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // All successful with high latency (>=1800ms default) and success rate >= 0.7
        var telemetry = Enumerable.Range(0, 5)
            .Select(_ => MakeTelemetry(
                objectiveId: objectiveId,
                workflowId: workflowId,
                agentId: agentId,
                success: true,
                errorType: "none",
                executionTimeMs: 2500,
                cost: 0.05m))
            .ToList();

        SetupTelemetry(objectiveId, telemetry);

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        var optPatterns = result.Where(p => p.PatternType == "workflow-optimization-opportunity").ToList();
        optPatterns.Should().HaveCount(1);

        var pattern = optPatterns[0];
        pattern.ObjectiveId.Should().Be(objectiveId);
        pattern.Title.Should().Be("Workflow optimization opportunity identified");
        pattern.Description.Should().Contain("2500");
        pattern.Description.Should().Contain("100.0%");
        // Score = avgLatency / max(1, threshold) = 2500 / 1800
        pattern.Score.Should().BeApproximately(2500.0 / 1800.0, 0.001);
        pattern.Metadata["workflowId"].Should().Be(workflowId.ToString());
        pattern.Metadata["sampleSize"].Should().Be("5");
        pattern.Metadata["successRate"].Should().Be("1.0000");
    }

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_LowLatencyWorkflow_NoOptimizationPattern()
    {
        var objectiveId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // Below latency threshold (default 1800ms)
        var telemetry = Enumerable.Range(0, 5)
            .Select(_ => MakeTelemetry(
                objectiveId: objectiveId,
                workflowId: workflowId,
                agentId: agentId,
                success: true,
                errorType: "none",
                executionTimeMs: 500,
                cost: 0.01m))
            .ToList();

        SetupTelemetry(objectiveId, telemetry);

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        result.Where(p => p.PatternType == "workflow-optimization-opportunity").Should().BeEmpty();
    }

    // --- RankAgentPerformance ---

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_MultipleAgents_RanksAgentsByScore()
    {
        var objectiveId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentA = Guid.NewGuid();
        var agentB = Guid.NewGuid();

        // Agent A: 3 successes, low latency, low cost => high score
        var telemetryA = Enumerable.Range(0, 3)
            .Select(_ => MakeTelemetry(
                objectiveId: objectiveId,
                workflowId: workflowId,
                agentId: agentA,
                success: true,
                errorType: "none",
                executionTimeMs: 100,
                cost: 0.01m))
            .ToList();

        // Agent B: 3 executions, 1 failure, higher latency, higher cost => lower score
        var telemetryB = new List<TaskExecutionTelemetry>
        {
            MakeTelemetry(objectiveId: objectiveId, workflowId: workflowId, agentId: agentB,
                success: true, errorType: "none", executionTimeMs: 500, cost: 0.10m),
            MakeTelemetry(objectiveId: objectiveId, workflowId: workflowId, agentId: agentB,
                success: true, errorType: "none", executionTimeMs: 500, cost: 0.10m),
            MakeTelemetry(objectiveId: objectiveId, workflowId: workflowId, agentId: agentB,
                success: false, errorType: "Error", executionTimeMs: 500, cost: 0.10m)
        };

        var allTelemetry = telemetryA.Concat(telemetryB).ToList();
        SetupTelemetry(objectiveId, allTelemetry);

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        var rankPatterns = result
            .Where(p => p.PatternType == "agent-performance-ranking")
            .OrderBy(p => int.Parse(p.Metadata["rank"]))
            .ToList();

        rankPatterns.Should().HaveCount(2);

        // Agent A should be rank 1 (better score)
        // Score A = (1.0 * 100) - (100/250) - (0.01 * 10) = 100 - 0.4 - 0.1 = 99.5
        rankPatterns[0].Metadata["rank"].Should().Be("1");
        rankPatterns[0].Metadata["agentId"].Should().Be(agentA.ToString());
        rankPatterns[0].Score.Should().BeApproximately(99.5, 0.01);

        // Agent B should be rank 2
        // Score B = (2/3 * 100) - (500/250) - (0.10 * 10) = 66.667 - 2.0 - 1.0 = 63.667
        rankPatterns[1].Metadata["rank"].Should().Be("2");
        rankPatterns[1].Metadata["agentId"].Should().Be(agentB.ToString());
        rankPatterns[1].Score.Should().BeApproximately(63.667, 0.01);
    }

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_AgentBelowMinExecutions_NotRanked()
    {
        var objectiveId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // Only 2 executions (below default MinExecutionsForRanking=3)
        var telemetry = Enumerable.Range(0, 2)
            .Select(_ => MakeTelemetry(
                objectiveId: objectiveId,
                workflowId: workflowId,
                agentId: agentId,
                success: true,
                errorType: "none",
                executionTimeMs: 100,
                cost: 0.01m))
            .ToList();

        SetupTelemetry(objectiveId, telemetry);

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        result.Where(p => p.PatternType == "agent-performance-ranking").Should().BeEmpty();
    }

    // --- ExtractStrategies ---

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_SuccessfulWorkflows_ExtractsStrategies()
    {
        var objectiveId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // 4 successes on same workflow (meets default StrategyCandidateMinSuccesses=3)
        var telemetry = Enumerable.Range(0, 4)
            .Select(_ => MakeTelemetry(
                objectiveId: objectiveId,
                workflowId: workflowId,
                agentId: agentId,
                success: true,
                errorType: "none",
                executionTimeMs: 200,
                cost: 0.02m))
            .ToList();

        SetupTelemetry(objectiveId, telemetry);

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        var strategyPatterns = result.Where(p => p.PatternType == "strategy-extraction").ToList();
        strategyPatterns.Should().HaveCount(1);

        var pattern = strategyPatterns[0];
        pattern.ObjectiveId.Should().Be(objectiveId);
        pattern.Title.Should().Be("Reusable strategy extracted");
        pattern.Description.Should().Contain(workflowId.ToString());
        pattern.Description.Should().Contain("4 successes");
        // Score = successCount / max(1.0, avgLatency / 1000.0) = 4 / max(1.0, 200/1000) = 4 / 1.0 = 4.0
        pattern.Score.Should().BeApproximately(4.0, 0.001);
        pattern.Metadata["workflowId"].Should().Be(workflowId.ToString());
        pattern.Metadata["successCount"].Should().Be("4");
        pattern.Metadata["avgLatencyMs"].Should().Be("200.00");
        pattern.Metadata["avgCost"].Should().Be("0.0200");
    }

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_StrategiesLimitedToTopThree()
    {
        var objectiveId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // Create 5 different workflows each with enough successes
        var telemetry = new List<TaskExecutionTelemetry>();
        for (int i = 0; i < 5; i++)
        {
            var wfId = Guid.NewGuid();
            int successCount = 10 - i; // 10, 9, 8, 7, 6
            for (int j = 0; j < successCount; j++)
            {
                telemetry.Add(MakeTelemetry(
                    objectiveId: objectiveId,
                    workflowId: wfId,
                    agentId: agentId,
                    success: true,
                    errorType: "none",
                    executionTimeMs: 100,
                    cost: 0.01m));
            }
        }

        SetupTelemetry(objectiveId, telemetry);

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        var strategyPatterns = result.Where(p => p.PatternType == "strategy-extraction").ToList();
        // Take(3) in ExtractStrategies limits to 3
        strategyPatterns.Should().HaveCount(3);
    }

    // --- Memory and Learning integration ---

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_PatternsFound_SavesMemoryAndIngestsPatterns()
    {
        var objectiveId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // 3 failures to trigger failure-detection pattern
        var telemetry = Enumerable.Range(0, 3)
            .Select(_ => MakeTelemetry(
                objectiveId: objectiveId,
                workflowId: workflowId,
                agentId: agentId,
                success: false,
                errorType: "NullRef",
                executionTimeMs: 50,
                cost: 0.01m))
            .ToList();

        SetupTelemetry(objectiveId, telemetry);

        var savedRecords = new List<MemoryRecord>();
        _memoryStore
            .Setup(x => x.SaveAsync(It.IsAny<MemoryRecord>(), It.IsAny<CancellationToken>()))
            .Callback<MemoryRecord, CancellationToken>((record, _) => savedRecords.Add(record))
            .Returns(Task.CompletedTask);

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        // Verify MemoryStore.SaveAsync called once per pattern
        result.Should().NotBeEmpty();
        savedRecords.Should().HaveCount(result.Count);

        // Verify saved memory records have correct structure
        foreach (var record in savedRecords)
        {
            record.MemoryType.Should().Be("intelligence-pattern");
            record.Scope.Should().Be($"{_options.IntelligenceScopePrefix}:{objectiveId}");
            record.Metadata.Should().ContainKey("patternId");
            record.Metadata.Should().ContainKey("patternType");
            record.Metadata.Should().ContainKey("title");
            record.Metadata.Should().ContainKey("score");
            record.ExpiresAtUtc.Should().BeNull();
        }

        // Verify IngestPatternsAsync called with correct tenant and patterns
        _learningEngine.Verify(
            x => x.IngestPatternsAsync("test-tenant", result, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- Failure detection picks highest count ---

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_MultipleErrorTypes_PicksHighestCount()
    {
        var objectiveId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        var telemetry = new List<TaskExecutionTelemetry>();

        // 3 TimeoutErrors
        for (int i = 0; i < 3; i++)
        {
            telemetry.Add(MakeTelemetry(
                objectiveId: objectiveId, workflowId: workflowId, agentId: agentId,
                success: false, errorType: "TimeoutError", executionTimeMs: 50, cost: 0.01m));
        }

        // 5 NullRefErrors (should be selected as it has highest count)
        for (int i = 0; i < 5; i++)
        {
            telemetry.Add(MakeTelemetry(
                objectiveId: objectiveId, workflowId: workflowId, agentId: agentId,
                success: false, errorType: "NullRefError", executionTimeMs: 50, cost: 0.01m));
        }

        SetupTelemetry(objectiveId, telemetry);

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        var failurePatterns = result.Where(p => p.PatternType == "failure-detection").ToList();
        // Only one failure pattern (FirstOrDefault picks top after OrderByDescending)
        failurePatterns.Should().HaveCount(1);
        failurePatterns[0].Metadata["errorType"].Should().Be("NullRefError");
        failurePatterns[0].Score.Should().Be(5);
        failurePatterns[0].Metadata["occurrences"].Should().Be("5");
    }

    // --- Workflow optimization filters low success rate ---

    [Fact]
    public async Task PatternDiscoveryEngine_DiscoverObjectivePatternsAsync_HighLatencyLowSuccessRate_NoOptimizationPattern()
    {
        var objectiveId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // High latency but only 60% success rate (below 0.7 threshold)
        var telemetry = new List<TaskExecutionTelemetry>();
        for (int i = 0; i < 6; i++)
        {
            telemetry.Add(MakeTelemetry(
                objectiveId: objectiveId, workflowId: workflowId, agentId: agentId,
                success: true, errorType: "none", executionTimeMs: 3000, cost: 0.05m));
        }
        for (int i = 0; i < 4; i++)
        {
            telemetry.Add(MakeTelemetry(
                objectiveId: objectiveId, workflowId: workflowId, agentId: agentId,
                success: false, errorType: "SomeError", executionTimeMs: 3000, cost: 0.05m));
        }

        SetupTelemetry(objectiveId, telemetry);

        var engine = CreateEngine();
        var result = await engine.DiscoverObjectivePatternsAsync(objectiveId);

        result.Where(p => p.PatternType == "workflow-optimization-opportunity").Should().BeEmpty();
    }
}
