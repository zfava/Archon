using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Evaluation;
using ArchonAI.Core.Models.Knowledge;
using ArchonAI.Core.Models.Learning;
using ArchonAI.Core.Models.Patterns;
using ArchonAI.Learning;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace ArchonAI.Learning.Tests;

public class LearningTests
{
    // ══════════════════════════════════════════════════════════════
    //  Shared helpers
    // ══════════════════════════════════════════════════════════════

    private static OperationalPattern MakePattern(
        string patternType = "latency-spike",
        double score = 0.85,
        Guid? objectiveId = null)
    {
        return new OperationalPattern(
            Id: Guid.NewGuid(),
            ObjectiveId: objectiveId ?? Guid.NewGuid(),
            PatternType: patternType,
            Title: $"Test {patternType}",
            Description: $"A test pattern of type {patternType}",
            Score: score,
            Metadata: new Dictionary<string, string> { ["source"] = "unit-test" },
            DiscoveredAtUtc: DateTimeOffset.UtcNow);
    }

    private static OutcomeEvaluationResult MakeEvaluation(
        string strategy = "balanced",
        bool actualSuccess = true,
        double overallScore = 0.9,
        double durationAccuracy = 0.8,
        double costAccuracy = 0.85,
        double riskPredictionAccuracy = 0.7,
        string overallAssessment = "good",
        IReadOnlyList<NodeEvaluationResult>? nodeEvaluations = null)
    {
        return new OutcomeEvaluationResult(
            EvaluationId: Guid.NewGuid(),
            GraphId: Guid.NewGuid(),
            GoalId: Guid.NewGuid(),
            Strategy: strategy,
            SuccessMetrics: new SuccessMetrics(
                SuccessRate: actualSuccess ? 1.0 : 0.0,
                AccuracyScore: 0.8,
                DurationAccuracy: durationAccuracy,
                CostAccuracy: costAccuracy,
                RiskPredictionAccuracy: riskPredictionAccuracy,
                TotalNodes: 5,
                SucceededNodes: actualSuccess ? 5 : 2,
                FailedNodes: actualSuccess ? 0 : 3,
                OverallScore: overallScore),
            Comparison: new ExpectedVsActual(
                ExpectedSuccessProbability: 0.9,
                ActualSuccess: actualSuccess,
                ExpectedDurationHours: 2.0,
                ActualDurationHours: actualSuccess ? 2.1 : 4.0,
                DurationDeviationPercent: actualSuccess ? 5.0 : 100.0,
                ExpectedCost: 100m,
                ActualCost: actualSuccess ? 105m : 200m,
                CostDeviationPercent: actualSuccess ? 5.0 : 100.0,
                ExpectedRiskScore: 0.3,
                RiskPredictionCorrect: actualSuccess),
            NodeEvaluations: nodeEvaluations ?? Array.Empty<NodeEvaluationResult>(),
            Insights: new[] { "Test insight" },
            Recommendations: new[] { "Test recommendation" },
            OverallAssessment: overallAssessment,
            EvaluatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static NodeEvaluationResult MakeNodeEvaluation(
        string agentType = "data-analyst",
        bool actualSuccess = true,
        bool wasOnCriticalPath = false,
        double predictedSuccessProbability = 0.9,
        double predictedDurationHours = 1.0,
        double actualDurationHours = 1.1,
        decimal predictedCost = 50m,
        decimal actualCost = 55m)
    {
        return new NodeEvaluationResult(
            NodeId: Guid.NewGuid(),
            NodeName: $"node-{agentType}",
            AgentType: agentType,
            PredictedSuccessProbability: predictedSuccessProbability,
            ActualSuccess: actualSuccess,
            PredictedDurationHours: predictedDurationHours,
            ActualDurationHours: actualDurationHours,
            PredictedCost: predictedCost,
            ActualCost: actualCost,
            WasOnCriticalPath: wasOnCriticalPath,
            Assessment: actualSuccess ? "passed" : "failed");
    }

    // ══════════════════════════════════════════════════════════════
    //  LearningEngine fixture
    // ══════════════════════════════════════════════════════════════

    private readonly Mock<IMemoryStore> _memoryStore = new();
    private readonly Mock<IMultiTenantContext> _tenantContext = new();

    private LearningEngine CreateLearningEngine(LearningOptions? options = null)
    {
        options ??= new LearningOptions();
        _tenantContext
            .Setup(t => t.BeginTenantScope(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>());
        return new LearningEngine(
            _memoryStore.Object,
            _tenantContext.Object,
            Options.Create(options));
    }

    // ══════════════════════════════════════════════════════════════
    //  LearningEngine tests (10)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task LearningEngine_IngestPatternsAsync_EmptyPatterns_ReturnsWithoutSaving()
    {
        var engine = CreateLearningEngine();

        await engine.IngestPatternsAsync("tenant-1", Array.Empty<OperationalPattern>());

        _memoryStore.Verify(
            m => m.SaveAsync(It.IsAny<MemoryRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LearningEngine_IngestPatternsAsync_SingleTenant_NoInsightGenerated()
    {
        // MinTenantCoverageForGlobalPattern defaults to 2, so a single tenant should not produce insights
        var engine = CreateLearningEngine();
        var patterns = new[] { MakePattern("latency-spike", 0.9) };

        await engine.IngestPatternsAsync("tenant-1", patterns);

        _memoryStore.Verify(
            m => m.SaveAsync(It.IsAny<MemoryRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LearningEngine_IngestPatternsAsync_MultiTenant_InsightGenerated()
    {
        var engine = CreateLearningEngine();

        await engine.IngestPatternsAsync("tenant-1", new[] { MakePattern("latency-spike", 0.9) });
        await engine.IngestPatternsAsync("tenant-2", new[] { MakePattern("latency-spike", 0.7) });

        _memoryStore.Verify(
            m => m.SaveAsync(It.IsAny<MemoryRecord>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task LearningEngine_IngestPatternsAsync_MultiTenant_SetsCorrectTenantScope()
    {
        var engine = CreateLearningEngine(new LearningOptions { GlobalTenantId = "my-global" });

        await engine.IngestPatternsAsync("t1", new[] { MakePattern() });
        await engine.IngestPatternsAsync("t2", new[] { MakePattern() });

        _tenantContext.Verify(
            t => t.BeginTenantScope("my-global"),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task LearningEngine_IngestPatternsAsync_MultiTenant_InsightScoreIsAverage()
    {
        var engine = CreateLearningEngine();

        await engine.IngestPatternsAsync("tenant-A", new[] { MakePattern("spike", 0.6) });
        await engine.IngestPatternsAsync("tenant-B", new[] { MakePattern("spike", 0.8) });

        var insights = await engine.GetGlobalInsightsAsync();

        insights.Should().HaveCount(1);
        insights[0].Score.Should().BeApproximately(0.7, 0.001);
        insights[0].TenantCoverage.Should().Be(2);
    }

    [Fact]
    public async Task LearningEngine_GetGlobalInsightsAsync_NoPatterns_ReturnsEmpty()
    {
        var engine = CreateLearningEngine();

        var insights = await engine.GetGlobalInsightsAsync();

        insights.Should().BeEmpty();
    }

    [Fact]
    public async Task LearningEngine_GetGlobalInsightsAsync_WithData_ReturnsCorrectInsight()
    {
        var engine = CreateLearningEngine();

        await engine.IngestPatternsAsync("t1", new[] { MakePattern("cache-miss", 0.5) });
        await engine.IngestPatternsAsync("t2", new[] { MakePattern("cache-miss", 0.9) });

        var insights = await engine.GetGlobalInsightsAsync();

        insights.Should().ContainSingle();
        insights[0].InsightType.Should().Be("cache-miss");
        insights[0].Summary.Should().Contain("cache-miss");
        insights[0].Metadata.Should().ContainKey("occurrences");
    }

    [Fact]
    public async Task LearningEngine_GetGlobalInsightsAsync_RespectsMaxInsights()
    {
        var engine = CreateLearningEngine(new LearningOptions
        {
            MinTenantCoverageForGlobalPattern = 1,
            MaxInsights = 2
        });

        for (int i = 0; i < 5; i++)
        {
            await engine.IngestPatternsAsync("t1", new[] { MakePattern($"type-{i}", 0.5 + (i * 0.1)) });
        }

        var insights = await engine.GetGlobalInsightsAsync();

        insights.Should().HaveCount(2);
    }

    [Fact]
    public async Task LearningEngine_IngestPatternsAsync_CancellationRequested_Throws()
    {
        var engine = CreateLearningEngine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => engine.IngestPatternsAsync(
            "tenant-1",
            new[] { MakePattern() },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LearningEngine_GetGlobalInsightsAsync_CancellationRequested_Throws()
    {
        var engine = CreateLearningEngine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => engine.GetGlobalInsightsAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ══════════════════════════════════════════════════════════════
    //  StrategyLearningEngine fixture
    // ══════════════════════════════════════════════════════════════

    private readonly Mock<IOutcomeEvaluator> _outcomeEvaluator = new();
    private readonly Mock<IPlanningFeedbackStore> _feedbackStore = new();
    private readonly Mock<IKnowledgeGraphStore> _knowledgeStore = new();
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<ILogger<StrategyLearningEngine>> _logger = new();

    private StrategyLearningEngine CreateStrategyEngine()
    {
        return new StrategyLearningEngine(
            _outcomeEvaluator.Object,
            _feedbackStore.Object,
            _knowledgeStore.Object,
            _eventBus.Object,
            _logger.Object);
    }

    private void SetupNoEvaluations()
    {
        _outcomeEvaluator
            .Setup(e => e.GetEvaluationsForStrategyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutcomeEvaluationResult>());
    }

    private void SetupEvaluationsForStrategy(string strategy, params OutcomeEvaluationResult[] evals)
    {
        _outcomeEvaluator
            .Setup(e => e.GetEvaluationsForStrategyAsync(strategy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evals);
    }

    // ══════════════════════════════════════════════════════════════
    //  StrategyLearningEngine tests (10)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task StrategyLearningEngine_AnalyzeAndLearnAsync_NoEvaluations_ReturnsEmptyReport()
    {
        SetupNoEvaluations();
        var engine = CreateStrategyEngine();

        var report = await engine.AnalyzeAndLearnAsync();

        report.EvaluationsAnalyzed.Should().Be(0);
        report.StrategyProfiles.Should().BeEmpty();
        report.AgentTypeProfiles.Should().BeEmpty();
        report.StrategyRecommendations.Should().BeEmpty();
        report.BestOverallStrategy.Should().Be("balanced");
        report.WorstOverallStrategy.Should().Be("unknown");
    }

    [Fact]
    public async Task StrategyLearningEngine_AnalyzeAndLearnAsync_WithEvaluations_ProducesReport()
    {
        SetupNoEvaluations();
        SetupEvaluationsForStrategy("balanced",
            MakeEvaluation("balanced", true, 0.9),
            MakeEvaluation("balanced", true, 0.85),
            MakeEvaluation("balanced", false, 0.3));
        var engine = CreateStrategyEngine();

        var report = await engine.AnalyzeAndLearnAsync();

        report.EvaluationsAnalyzed.Should().Be(3);
        report.StrategyProfiles.Should().HaveCount(1);
        report.StrategyProfiles[0].Strategy.Should().Be("balanced");
        report.StrategyProfiles[0].TotalExecutions.Should().Be(3);
        report.StrategyProfiles[0].Successes.Should().Be(2);
        report.StrategyProfiles[0].Failures.Should().Be(1);
    }

    [Fact]
    public async Task StrategyLearningEngine_AnalyzeAndLearnAsync_WithEvaluations_PublishesEvent()
    {
        SetupNoEvaluations();
        SetupEvaluationsForStrategy("balanced", MakeEvaluation("balanced"));
        var engine = CreateStrategyEngine();

        await engine.AnalyzeAndLearnAsync();

        _eventBus.Verify(
            e => e.PublishAsync(
                It.Is<SystemEvent>(ev => ev.EventType == "learning.strategy.report.generated"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StrategyLearningEngine_AnalyzeAndLearnAsync_WithEvaluations_StoresInKnowledgeGraph()
    {
        SetupNoEvaluations();
        SetupEvaluationsForStrategy("balanced", MakeEvaluation("balanced"));
        var engine = CreateStrategyEngine();

        await engine.AnalyzeAndLearnAsync();

        _knowledgeStore.Verify(
            k => k.UpsertNodeAsync(
                It.Is<KnowledgeNode>(n => n.NodeType == "strategy_learning_report"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StrategyLearningEngine_GetStrategyProfilesAsync_NoEvaluations_ReturnsEmpty()
    {
        SetupNoEvaluations();
        var engine = CreateStrategyEngine();

        var profiles = await engine.GetStrategyProfilesAsync();

        profiles.Should().BeEmpty();
    }

    [Fact]
    public async Task StrategyLearningEngine_GetStrategyProfilesAsync_WithEvaluations_ReturnsProfiles()
    {
        SetupNoEvaluations();
        SetupEvaluationsForStrategy("safe-mode",
            MakeEvaluation("safe-mode", true, 0.95),
            MakeEvaluation("safe-mode", true, 0.88));
        var engine = CreateStrategyEngine();

        var profiles = await engine.GetStrategyProfilesAsync();

        profiles.Should().HaveCount(1);
        profiles[0].Strategy.Should().Be("safe-mode");
        profiles[0].SuccessRate.Should().Be(1.0);
    }

    [Fact]
    public async Task StrategyLearningEngine_RecommendStrategyAsync_NoProfiles_ReturnsBalanced()
    {
        SetupNoEvaluations();
        var engine = CreateStrategyEngine();

        var result = await engine.RecommendStrategyAsync("sales", "high");

        result.Should().Be("balanced");
    }

    [Fact]
    public async Task StrategyLearningEngine_RecommendStrategyAsync_WithProfiles_ReturnsBestStrategy()
    {
        SetupNoEvaluations();
        // Set up throughput-optimized with 4 successful evaluations (TotalExecutions >= 3)
        SetupEvaluationsForStrategy("throughput-optimized",
            MakeEvaluation("throughput-optimized", true, 0.95),
            MakeEvaluation("throughput-optimized", true, 0.92),
            MakeEvaluation("throughput-optimized", true, 0.90),
            MakeEvaluation("throughput-optimized", true, 0.88));
        var engine = CreateStrategyEngine();

        var result = await engine.RecommendStrategyAsync("operations", "medium");

        result.Should().Be("throughput-optimized");
    }

    [Fact]
    public async Task StrategyLearningEngine_GetAgentTypeProfilesAsync_WithNodeEvaluations_ReturnsProfiles()
    {
        SetupNoEvaluations();
        var nodes = new[]
        {
            MakeNodeEvaluation("data-analyst", true),
            MakeNodeEvaluation("data-analyst", true),
            MakeNodeEvaluation("executor", false),
        };
        SetupEvaluationsForStrategy("balanced",
            MakeEvaluation("balanced", true, nodeEvaluations: nodes));
        var engine = CreateStrategyEngine();

        var profiles = await engine.GetAgentTypeProfilesAsync();

        profiles.Should().HaveCount(2);
        profiles.Should().Contain(p => p.AgentType == "data-analyst");
        profiles.Should().Contain(p => p.AgentType == "executor");
    }

    [Fact]
    public async Task StrategyLearningEngine_AnalyzeAndLearnAsync_CancellationRequested_Throws()
    {
        var engine = CreateStrategyEngine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => engine.AnalyzeAndLearnAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ══════════════════════════════════════════════════════════════
    //  LearningOptions tests (2)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void LearningOptions_Defaults_GlobalTenantIdIsCorrect()
    {
        var options = new LearningOptions();

        options.GlobalTenantId.Should().Be("global-intelligence");
    }

    [Fact]
    public void LearningOptions_Defaults_NumericDefaultsAreCorrect()
    {
        var options = new LearningOptions();

        options.MinTenantCoverageForGlobalPattern.Should().Be(2);
        options.MaxInsights.Should().Be(50);
    }
}
