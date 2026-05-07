using ArchonAI.Agents.Operations;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.DataFabric;
using ArchonAI.Core.Models.Operations;
using ArchonAI.Core.Models.Patterns;
using ArchonAI.Core.Models.Planning;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Agents.Operations.Tests;

public sealed class OperationsEngineTests
{
    private readonly IDataFabricEngine _dataFabric = Substitute.For<IDataFabricEngine>();
    private readonly IStrategyStore _strategyStore = Substitute.For<IStrategyStore>();
    private readonly IPatternAnalyzer _patternAnalyzer = Substitute.For<IPatternAnalyzer>();
    private readonly IReasoner _reasoner = Substitute.For<IReasoner>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<OperationsEngine> _logger = Substitute.For<ILogger<OperationsEngine>>();
    private readonly OperationsOptions _options = new();

    private OperationsEngine CreateEngine()
    {
        return new OperationsEngine(
            _dataFabric, _strategyStore, _patternAnalyzer, _reasoner,
            _eventBus, _logger, Options.Create(_options));
    }

    [Fact]
    public async Task AnalyzeWorkflowAsync_ReturnsAnalysisResult()
    {
        var objectiveId = Guid.NewGuid();

        _dataFabric.QueryEnterpriseDataAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataFabricQueryResult(true, "ok",
                new IReadOnlyDictionary<string, string>[]
                {
                    new Dictionary<string, string> { ["isSuccess"] = "True" },
                    new Dictionary<string, string> { ["isSuccess"] = "True" },
                    new Dictionary<string, string> { ["isSuccess"] = "False" }
                },
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        _patternAnalyzer.AnalyzeObjectiveAsync(objectiveId, Arg.Any<CancellationToken>())
            .Returns(new List<OperationalPattern>
            {
                new(Guid.NewGuid(), objectiveId, "failure-recurring", "Recurring API timeout",
                    "API calls timeout frequently", 0.8, new Dictionary<string, string>(), DateTimeOffset.UtcNow)
            });

        _strategyStore.QueryByObjectiveTypeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<OperationalStrategy>
            {
                new(Guid.NewGuid(), "default", "balanced", new[] { "workflow-orchestration" },
                    new Dictionary<string, string> { ["successRate"] = "0.80" },
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            });

        var engine = CreateEngine();
        var result = await engine.AnalyzeWorkflowAsync(objectiveId, new Dictionary<string, string>());

        result.IsSuccess.Should().BeTrue();
        result.ObjectiveId.Should().Be(objectiveId);
        result.TotalSteps.Should().Be(3);
        result.SuccessfulSteps.Should().Be(2);
        result.FailedSteps.Should().Be(1);
        result.EfficiencyScore.Should().BeApproximately(0.667, 0.01);
        result.Bottlenecks.Should().NotBeEmpty();
        result.Recommendations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AnalyzeWorkflowAsync_EmitsAuditEvent()
    {
        var objectiveId = Guid.NewGuid();
        SetupDefaultMocks(objectiveId);

        var engine = CreateEngine();
        await engine.AnalyzeWorkflowAsync(objectiveId, new Dictionary<string, string>());

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "operations.workflow.analyzed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IdentifyInefficienciesAsync_DetectsHighFailureRate()
    {
        _dataFabric.QueryEnterpriseDataAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataFabricQueryResult(true, "ok",
                new IReadOnlyDictionary<string, string>[]
                {
                    new Dictionary<string, string> { ["isSuccess"] = "False" },
                    new Dictionary<string, string> { ["isSuccess"] = "False" },
                    new Dictionary<string, string> { ["isSuccess"] = "True" }
                },
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var engine = CreateEngine();
        var insights = await engine.IdentifyInefficienciesAsync("crm", 20);

        insights.Should().NotBeEmpty();
        insights.Should().Contain(i => i.InsightType == "high-failure-rate");
    }

    [Fact]
    public async Task IdentifyInefficienciesAsync_DetectsSlowExecution()
    {
        _dataFabric.QueryEnterpriseDataAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataFabricQueryResult(true, "ok",
                new IReadOnlyDictionary<string, string>[]
                {
                    new Dictionary<string, string> { ["executionMs"] = "2000", ["isSuccess"] = "True" },
                    new Dictionary<string, string> { ["executionMs"] = "3000", ["isSuccess"] = "True" }
                },
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var engine = CreateEngine();
        var insights = await engine.IdentifyInefficienciesAsync("*", 20);

        insights.Should().Contain(i => i.InsightType == "slow-execution");
    }

    [Fact]
    public async Task IdentifyInefficienciesAsync_ReturnsEmptyWhenNoIssues()
    {
        _dataFabric.QueryEnterpriseDataAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataFabricQueryResult(true, "ok",
                Array.Empty<IReadOnlyDictionary<string, string>>(),
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var engine = CreateEngine();
        var insights = await engine.IdentifyInefficienciesAsync("*");

        insights.Should().BeEmpty();
    }

    [Fact]
    public async Task RecommendImprovementsAsync_GeneratesRecommendations()
    {
        var objectiveId = Guid.NewGuid();

        _patternAnalyzer.AnalyzeObjectiveAsync(objectiveId, Arg.Any<CancellationToken>())
            .Returns(new List<OperationalPattern>
            {
                new(Guid.NewGuid(), objectiveId, "failure-recurring", "Recurring failure",
                    "Steps keep failing", 0.9, new Dictionary<string, string>(), DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), objectiveId, "latency-high", "High latency",
                    "Slow responses", 0.7, new Dictionary<string, string>(), DateTimeOffset.UtcNow)
            });

        _strategyStore.QueryByObjectiveTypeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<OperationalStrategy>
            {
                new(Guid.NewGuid(), "default", "safe-mode", new[] { "workflow-orchestration" },
                    new Dictionary<string, string>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            });

        var engine = CreateEngine();
        var recs = await engine.RecommendImprovementsAsync(objectiveId);

        recs.Should().HaveCount(2);
        recs[0].RecommendationType.Should().Be("failure-mitigation");
        recs[1].RecommendationType.Should().Be("performance-optimization");
    }

    [Fact]
    public async Task RecommendImprovementsAsync_GeneratesBaselineWhenNoPatterns()
    {
        var objectiveId = Guid.NewGuid();

        _patternAnalyzer.AnalyzeObjectiveAsync(objectiveId, Arg.Any<CancellationToken>())
            .Returns(new List<OperationalPattern>());

        _strategyStore.QueryByObjectiveTypeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<OperationalStrategy>());

        var engine = CreateEngine();
        var recs = await engine.RecommendImprovementsAsync(objectiveId);

        recs.Should().HaveCount(1);
        recs[0].RecommendationType.Should().Be("baseline-optimization");
    }

    [Fact]
    public async Task RecommendImprovementsAsync_EmitsAuditEvent()
    {
        var objectiveId = Guid.NewGuid();

        _patternAnalyzer.AnalyzeObjectiveAsync(objectiveId, Arg.Any<CancellationToken>())
            .Returns(new List<OperationalPattern>());

        _strategyStore.QueryByObjectiveTypeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<OperationalStrategy>());

        var engine = CreateEngine();
        await engine.RecommendImprovementsAsync(objectiveId);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "operations.recommendations.generated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CoordinateWorkflowAsync_CompletesSuccessfully()
    {
        _strategyStore.QueryByObjectiveTypeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<OperationalStrategy>
            {
                new(Guid.NewGuid(), "default", "balanced", new[] { "workflow-orchestration" },
                    new Dictionary<string, string>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            });

        _dataFabric.QueryEnterpriseDataAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataFabricQueryResult(true, "ok",
                Array.Empty<IReadOnlyDictionary<string, string>>(),
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var engine = CreateEngine();
        var result = await engine.CoordinateWorkflowAsync(
            "balanced",
            new[] { "workflow-orchestration", "tooling-execution" },
            new Dictionary<string, string>());

        result.IsSuccess.Should().BeTrue();
        result.WorkflowTemplate.Should().Be("balanced");
        result.AgentsInvolved.Should().Be(2);
        result.StepsCompleted.Should().Be(2);
        result.StepsFailed.Should().Be(0);
    }

    [Fact]
    public async Task CoordinateWorkflowAsync_HandlesAccessDenied()
    {
        _strategyStore.QueryByObjectiveTypeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<OperationalStrategy>());

        _dataFabric.QueryEnterpriseDataAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataFabricQueryResult(false, "Access denied",
                Array.Empty<IReadOnlyDictionary<string, string>>(),
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var engine = CreateEngine();
        var result = await engine.CoordinateWorkflowAsync(
            "balanced", new[] { "workflow-orchestration" }, new Dictionary<string, string>());

        result.IsSuccess.Should().BeFalse();
        result.StepsFailed.Should().Be(1);
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CoordinateWorkflowAsync_EmitsAuditEvent()
    {
        _strategyStore.QueryByObjectiveTypeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<OperationalStrategy>());

        _dataFabric.QueryEnterpriseDataAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataFabricQueryResult(true, "ok",
                Array.Empty<IReadOnlyDictionary<string, string>>(),
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var engine = CreateEngine();
        await engine.CoordinateWorkflowAsync("balanced", new[] { "ops" }, new Dictionary<string, string>());

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "operations.workflow.coordinated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetStatus_ReturnsCurrentStatus()
    {
        var engine = CreateEngine();
        var status = engine.GetStatus();

        status.IsActive.Should().BeTrue();
        status.WorkflowsAnalyzed.Should().Be(0);
        status.InefficienciesDetected.Should().Be(0);
        status.RecommendationsGenerated.Should().Be(0);
        status.WorkflowsCoordinated.Should().Be(0);
        status.ReasoningCycles.Should().Be(0);
        status.DataFabricQueries.Should().Be(0);
        status.StrategyLookups.Should().Be(0);
        status.StatusAsOfUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    private void SetupDefaultMocks(Guid objectiveId)
    {
        _dataFabric.QueryEnterpriseDataAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataFabricQueryResult(true, "ok",
                Array.Empty<IReadOnlyDictionary<string, string>>(),
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        _patternAnalyzer.AnalyzeObjectiveAsync(objectiveId, Arg.Any<CancellationToken>())
            .Returns(new List<OperationalPattern>());

        _strategyStore.QueryByObjectiveTypeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<OperationalStrategy>());
    }
}
