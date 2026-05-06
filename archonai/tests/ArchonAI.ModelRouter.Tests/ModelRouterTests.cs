using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Models.Routing;
using ArchonAI.Core.Models.Trace;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace ArchonAI.ModelRouter.Tests;

public sealed class ModelRouterTests
{
    // ══════════════════════════════════════════════════════════════
    //  Shared helpers
    // ══════════════════════════════════════════════════════════════

    private static ModelRouterOptions DefaultOptions() => new();

    private static IOptions<ModelRouterOptions> Opts(ModelRouterOptions? opts = null) =>
        Options.Create(opts ?? DefaultOptions());

    private static ModelRequest MakeRequest(
        string? model = null,
        Dictionary<string, string>? parameters = null)
    {
        return new ModelRequest(
            Model: model ?? string.Empty,
            Prompt: "test prompt",
            Parameters: parameters ?? new Dictionary<string, string>(),
            RequestedBy: "unit-test",
            RequestedAtUtc: DateTimeOffset.UtcNow);
    }

    private static ModelPerformanceScore MakeScore(
        string provider,
        string model,
        double successRate = 0.95,
        double compositeScore = 0.8,
        double avgLatency = 200,
        double avgCost = 0.01,
        double accuracy = 0.9,
        int sampleCount = 50)
    {
        return new ModelPerformanceScore(
            Provider: provider,
            Model: model,
            AverageLatencyMs: avgLatency,
            AverageCostPerRequest: avgCost,
            AccuracyRate: accuracy,
            SuccessRate: successRate,
            SampleCount: sampleCount,
            CompositeScore: compositeScore,
            LastUpdatedUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  ModelRouter tests
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void ModelRouter_Route_ExplicitModel_ReturnsExplicitModelDecision()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);

        var request = MakeRequest(model: "openai.gpt-4.1");
        var decision = router.Route(request);

        decision.Provider.Should().Be("openai");
        decision.Model.Should().Be("openai.gpt-4.1");
        decision.Reason.Should().Be("explicit-model-requested");
    }

    [Fact]
    public void ModelRouter_Route_ExplicitAzureModel_ResolvesAzureProvider()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);

        var request = MakeRequest(model: "azure.gpt-4o-mini");
        var decision = router.Route(request);

        decision.Provider.Should().Be("azure-openai");
        decision.Model.Should().Be("azure.gpt-4o-mini");
    }

    [Fact]
    public void ModelRouter_Route_ExplicitAnthropicModel_ResolvesAnthropicProvider()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);

        var request = MakeRequest(model: "anthropic.claude-sonnet-4-6");
        var decision = router.Route(request);

        decision.Provider.Should().Be("anthropic");
    }

    [Fact]
    public void ModelRouter_Route_ExplicitUnknownModel_ResolvesLocalProvider()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);

        var request = MakeRequest(model: "some-custom-model");
        var decision = router.Route(request);

        decision.Provider.Should().Be("local");
    }

    [Fact]
    public void ModelRouter_Route_CostStrategy_RoutesCostOptimized()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        // SelectByWeight returns null to fall through to static strategy
        tracker.Setup(t => t.SelectByWeight(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((ModelPerformanceScore?)null);
        tracker.Setup(t => t.GetBestModelForStrategy(It.IsAny<string>()))
            .Returns((ModelPerformanceScore?)null);

        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);
        var request = MakeRequest(parameters: new Dictionary<string, string> { ["optimize"] = "cost" });

        var decision = router.Route(request);

        decision.Provider.Should().Be("openai");
        decision.Model.Should().Be("openai.gpt-4.1-nano");
        decision.Reason.Should().Be("cost-optimization");
        decision.CostOptimized.Should().BeTrue();
        decision.LatencyOptimized.Should().BeFalse();
    }

    [Fact]
    public void ModelRouter_Route_LatencyStrategy_RoutesLatencyOptimized()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        tracker.Setup(t => t.SelectByWeight(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((ModelPerformanceScore?)null);
        tracker.Setup(t => t.GetBestModelForStrategy(It.IsAny<string>()))
            .Returns((ModelPerformanceScore?)null);

        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);
        var request = MakeRequest(parameters: new Dictionary<string, string> { ["optimize"] = "latency" });

        var decision = router.Route(request);

        decision.Provider.Should().Be("azure-openai");
        decision.Model.Should().Be("azure.gpt-4o-mini");
        decision.Reason.Should().Be("latency-optimization");
        decision.LatencyOptimized.Should().BeTrue();
        decision.CostOptimized.Should().BeFalse();
    }

    [Fact]
    public void ModelRouter_Route_QualityStrategy_RoutesQualityOptimized()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        tracker.Setup(t => t.SelectByWeight(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((ModelPerformanceScore?)null);
        tracker.Setup(t => t.GetBestModelForStrategy(It.IsAny<string>()))
            .Returns((ModelPerformanceScore?)null);

        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);
        var request = MakeRequest(parameters: new Dictionary<string, string> { ["optimize"] = "quality" });

        var decision = router.Route(request);

        decision.Provider.Should().Be("openai");
        decision.Model.Should().Be("openai.gpt-4.1");
        decision.Reason.Should().Be("quality-optimization");
    }

    [Fact]
    public void ModelRouter_Route_DefaultStrategy_RoutesDefault()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        tracker.Setup(t => t.SelectByWeight(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((ModelPerformanceScore?)null);
        tracker.Setup(t => t.GetBestModelForStrategy(It.IsAny<string>()))
            .Returns((ModelPerformanceScore?)null);

        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);
        var request = MakeRequest();

        var decision = router.Route(request);

        decision.Provider.Should().Be("openai");
        decision.Model.Should().Be("openai.gpt-4.1-mini");
        decision.Reason.Should().Be("default-route");
    }

    [Fact]
    public void ModelRouter_Route_TaskTypeWithWeightedResult_UsesWeightedSelection()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        var weightedScore = MakeScore("openai", "openai.gpt-4.1", compositeScore: 0.85);

        tracker.Setup(t => t.SelectByWeight("default", "analysis"))
            .Returns(weightedScore);
        tracker.Setup(t => t.GetRoutingWeight("openai", "openai.gpt-4.1"))
            .Returns(new ModelRoutingWeight("openai", "openai.gpt-4.1", 0.9, 0.5, "top-performer", DateTimeOffset.UtcNow));

        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);
        var request = MakeRequest(parameters: new Dictionary<string, string> { ["taskType"] = "analysis" });

        var decision = router.Route(request);

        decision.Provider.Should().Be("openai");
        decision.Model.Should().Be("openai.gpt-4.1");
        decision.Reason.Should().Contain("weighted-task-type:analysis");
        decision.Reason.Should().Contain("w=0.90");
    }

    [Fact]
    public void ModelRouter_Route_TaskTypeNoWeights_FallsBackToStaticMap()
    {
        var opts = DefaultOptions();
        opts.EnableAdaptiveRouting = false; // disable adaptive so it skips weighted selection

        var tracker = new Mock<IModelPerformanceTracker>();
        var router = new ModelRouter.ModelRouter(Opts(opts), tracker.Object);

        var request = MakeRequest(parameters: new Dictionary<string, string> { ["taskType"] = "extraction" });
        var decision = router.Route(request);

        decision.Model.Should().Be("anthropic.claude-sonnet-4-6");
        decision.Provider.Should().Be("anthropic");
        decision.Reason.Should().Contain("task-type-route:extraction");
    }

    [Fact]
    public void ModelRouter_Route_AdaptiveWithGlobalWeightedResult_UsesWeightedAdaptive()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        var weightedScore = MakeScore("azure-openai", "azure.gpt-4o-mini", compositeScore: 0.75);

        // No task type in request, so task-type branch not hit
        // SelectByWeight with no taskType returns a result
        tracker.Setup(t => t.SelectByWeight("default", null))
            .Returns(weightedScore);
        tracker.Setup(t => t.GetRoutingWeight("azure-openai", "azure.gpt-4o-mini"))
            .Returns((ModelRoutingWeight?)null);

        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);
        var request = MakeRequest();

        var decision = router.Route(request);

        decision.Provider.Should().Be("azure-openai");
        decision.Model.Should().Be("azure.gpt-4o-mini");
        decision.Reason.Should().Contain("weighted-adaptive:default");
    }

    [Fact]
    public void ModelRouter_GetFallbackChain_ExistingChain_ReturnsOrderedEntries()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);

        var chain = router.GetFallbackChain("default");

        chain.Should().NotBeNull();
        chain!.Name.Should().Be("default");
        chain.Entries.Should().HaveCount(4);
        chain.Entries[0].Priority.Should().Be(1);
        chain.Entries[1].Priority.Should().Be(2);
        chain.Entries[2].Priority.Should().Be(3);
        chain.Entries[3].Priority.Should().Be(4);
    }

    [Fact]
    public void ModelRouter_GetFallbackChain_NonexistentChain_ReturnsNull()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);

        var chain = router.GetFallbackChain("nonexistent");

        chain.Should().BeNull();
    }

    [Fact]
    public void ModelRouter_RouteWithFallback_SkipsFailedModel_ReturnsNext()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        tracker.Setup(t => t.GetScore(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((ModelPerformanceScore?)null);

        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);
        var request = MakeRequest(parameters: new Dictionary<string, string> { ["optimize"] = "cost" });

        // Skip the first entry in the cost chain (openai / openai.gpt-4.1-nano)
        var decision = router.RouteWithFallback(request, failedProvider: "openai", failedModel: "openai.gpt-4.1-nano");

        decision.Should().NotBeNull();
        decision!.Provider.Should().Be("azure-openai");
        decision.Model.Should().Be("azure.gpt-4o-mini");
        decision.Reason.Should().Contain("fallback-chain:cost");
    }

    [Fact]
    public void ModelRouter_RouteWithFallback_SkipsModelWithLowSuccessRate()
    {
        var tracker = new Mock<IModelPerformanceTracker>();
        // First model in default chain has terrible success rate and enough samples
        tracker.Setup(t => t.GetScore("openai", "openai.gpt-4.1"))
            .Returns(MakeScore("openai", "openai.gpt-4.1", successRate: 0.05, sampleCount: 50));
        tracker.Setup(t => t.GetScore("azure-openai", "azure.gpt-4o-mini"))
            .Returns((ModelPerformanceScore?)null);

        var router = new ModelRouter.ModelRouter(Opts(), tracker.Object);
        var request = MakeRequest(); // "default" strategy -> "default" chain

        var decision = router.RouteWithFallback(request);

        decision.Should().NotBeNull();
        // Should skip openai.gpt-4.1 (success < 0.1 and samples >= 10) and pick azure
        decision!.Provider.Should().Be("azure-openai");
        decision.Model.Should().Be("azure.gpt-4o-mini");
    }

    [Fact]
    public void ModelRouter_RouteWithFallback_NoChain_ReturnsNull()
    {
        var opts = DefaultOptions();
        opts.FallbackChains = new Dictionary<string, List<FallbackEntryOptions>>();

        var tracker = new Mock<IModelPerformanceTracker>();
        var router = new ModelRouter.ModelRouter(Opts(opts), tracker.Object);
        var request = MakeRequest();

        var decision = router.RouteWithFallback(request);

        decision.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════
    //  ModelPerformanceTracker tests
    // ══════════════════════════════════════════════════════════════

    private static ModelPerformanceTracker CreateTracker(ModelRouterOptions? opts = null)
    {
        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(t => t.RecordAsync(It.IsAny<TraceEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new ModelPerformanceTracker(traceStore.Object, Opts(opts));
    }

    [Fact]
    public void ModelPerformanceTracker_RecordOutcome_SingleSuccess_ScoreReflectsIt()
    {
        var tracker = CreateTracker();

        tracker.RecordOutcome("openai", "gpt-4", true, 150.0, 0.02, 0.95);

        var score = tracker.GetScore("openai", "gpt-4");

        score.Should().NotBeNull();
        score!.Provider.Should().Be("openai");
        score.Model.Should().Be("gpt-4");
        score.SuccessRate.Should().Be(1.0);
        score.AverageLatencyMs.Should().Be(150.0);
        score.AverageCostPerRequest.Should().Be(0.02);
        score.AccuracyRate.Should().Be(0.95);
        score.SampleCount.Should().Be(1);
    }

    [Fact]
    public void ModelPerformanceTracker_RecordOutcome_MixedResults_ComputesAverages()
    {
        var tracker = CreateTracker();

        tracker.RecordOutcome("openai", "gpt-4", true, 100.0, 0.01, 0.9);
        tracker.RecordOutcome("openai", "gpt-4", false, 200.0, 0.03, 0.8);

        var score = tracker.GetScore("openai", "gpt-4");

        score.Should().NotBeNull();
        score!.SampleCount.Should().Be(2);
        score.SuccessRate.Should().Be(0.5);
        score.AverageLatencyMs.Should().Be(150.0);
        score.AverageCostPerRequest.Should().Be(0.02);
        score.AccuracyRate.Should().Be(0.85);
    }

    [Fact]
    public void ModelPerformanceTracker_RecordOutcome_WithTaskType_RecordsBothKeys()
    {
        var tracker = CreateTracker();

        tracker.RecordOutcome("openai", "gpt-4", "analysis", true, 100.0, 0.01, 0.9);

        // Should be available through both GetScore (global) and GetAllScores (which includes task-specific keys)
        var score = tracker.GetScore("openai", "gpt-4");
        score.Should().NotBeNull();
        score!.SampleCount.Should().Be(1);

        var allScores = tracker.GetAllScores();
        // Global key + task-type key = at least 2 entries
        allScores.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void ModelPerformanceTracker_GetScore_NonexistentModel_ReturnsNull()
    {
        var tracker = CreateTracker();

        var score = tracker.GetScore("nonexistent", "model");

        score.Should().BeNull();
    }

    [Fact]
    public void ModelPerformanceTracker_GetAllScores_Empty_ReturnsEmptyList()
    {
        var tracker = CreateTracker();

        var scores = tracker.GetAllScores();

        scores.Should().BeEmpty();
    }

    [Fact]
    public void ModelPerformanceTracker_GetBestModelForStrategy_Cost_ReturnsCheapest()
    {
        var opts = new ModelRouterOptions { MinSamplesForAdaptive = 1 };
        var tracker = CreateTracker(opts);

        // Cheap model
        tracker.RecordOutcome("openai", "cheap-model", true, 300.0, 0.001, 0.8);
        // Expensive model
        tracker.RecordOutcome("openai", "expensive-model", true, 100.0, 0.05, 0.95);

        var best = tracker.GetBestModelForStrategy("cost");

        best.Should().NotBeNull();
        best!.Model.Should().Be("cheap-model");
    }

    [Fact]
    public void ModelPerformanceTracker_GetBestModelForStrategy_Latency_ReturnsFastest()
    {
        var opts = new ModelRouterOptions { MinSamplesForAdaptive = 1 };
        var tracker = CreateTracker(opts);

        tracker.RecordOutcome("openai", "slow-model", true, 500.0, 0.01, 0.9);
        tracker.RecordOutcome("azure", "fast-model", true, 50.0, 0.01, 0.9);

        var best = tracker.GetBestModelForStrategy("latency");

        best.Should().NotBeNull();
        best!.Model.Should().Be("fast-model");
    }

    [Fact]
    public void ModelPerformanceTracker_GetBestModelForStrategy_Quality_ReturnsMostAccurate()
    {
        var opts = new ModelRouterOptions { MinSamplesForAdaptive = 1 };
        var tracker = CreateTracker(opts);

        tracker.RecordOutcome("openai", "low-quality", true, 100.0, 0.01, 0.5);
        tracker.RecordOutcome("openai", "high-quality", true, 100.0, 0.01, 0.99);

        var best = tracker.GetBestModelForStrategy("quality");

        best.Should().NotBeNull();
        best!.Model.Should().Be("high-quality");
    }

    [Fact]
    public void ModelPerformanceTracker_GetBestModelForStrategy_Default_ReturnsBestComposite()
    {
        var opts = new ModelRouterOptions { MinSamplesForAdaptive = 1 };
        var tracker = CreateTracker(opts);

        // Model with better all-round composite
        tracker.RecordOutcome("openai", "balanced", true, 100.0, 0.01, 0.95);
        // Model that is fast but fails often
        tracker.RecordOutcome("azure", "flaky", false, 50.0, 0.001, 0.2);

        var best = tracker.GetBestModelForStrategy("default");

        best.Should().NotBeNull();
        best!.Model.Should().Be("balanced");
    }

    [Fact]
    public void ModelPerformanceTracker_GetBestModelForStrategy_NoSamples_ReturnsNull()
    {
        // Default MinSamplesForAdaptive = 10, so 1 sample is not enough
        var tracker = CreateTracker();

        tracker.RecordOutcome("openai", "gpt-4", true, 100.0, 0.01, 0.9);

        var best = tracker.GetBestModelForStrategy("cost");

        best.Should().BeNull();
    }

    [Fact]
    public void ModelPerformanceTracker_SetAndGetRoutingWeight_RoundTrips()
    {
        var tracker = CreateTracker();

        tracker.SetRoutingWeight("openai", "gpt-4", 0.85, "top-performer");

        var weight = tracker.GetRoutingWeight("openai", "gpt-4");

        weight.Should().NotBeNull();
        weight!.Weight.Should().Be(0.85);
        weight.PreviousWeight.Should().Be(0.5); // default when first set
        weight.AdjustmentReason.Should().Be("top-performer");
    }

    [Fact]
    public void ModelPerformanceTracker_SetRoutingWeight_UpdatePreservesPreviousWeight()
    {
        var tracker = CreateTracker();

        tracker.SetRoutingWeight("openai", "gpt-4", 0.7, "first");
        tracker.SetRoutingWeight("openai", "gpt-4", 0.9, "second");

        var weight = tracker.GetRoutingWeight("openai", "gpt-4");

        weight.Should().NotBeNull();
        weight!.Weight.Should().Be(0.9);
        weight.PreviousWeight.Should().Be(0.7);
    }

    [Fact]
    public void ModelPerformanceTracker_GetRoutingWeights_ReturnsAll()
    {
        var tracker = CreateTracker();

        tracker.SetRoutingWeight("openai", "gpt-4", 0.8, "reason-a");
        tracker.SetRoutingWeight("azure", "gpt-4o", 0.6, "reason-b");

        var weights = tracker.GetRoutingWeights();

        weights.Should().HaveCount(2);
    }

    [Fact]
    public void ModelPerformanceTracker_SetAndGetTaskTypeWeights_RoundTrips()
    {
        var tracker = CreateTracker();

        // Record an outcome first so GetScore inside SetTaskTypeWeight can find it
        tracker.RecordOutcome("openai", "gpt-4", true, 100.0, 0.01, 0.9);

        tracker.SetTaskTypeWeight("analysis", "openai", "gpt-4", 0.75);

        var weights = tracker.GetTaskTypeWeights("analysis");

        weights.Should().HaveCount(1);
        weights[0].TaskType.Should().Be("analysis");
        weights[0].Provider.Should().Be("openai");
        weights[0].Model.Should().Be("gpt-4");
        weights[0].Weight.Should().Be(0.75);
    }

    [Fact]
    public void ModelPerformanceTracker_GetTaskTypeWeights_FiltersByTaskType()
    {
        var tracker = CreateTracker();

        tracker.SetTaskTypeWeight("analysis", "openai", "gpt-4", 0.7);
        tracker.SetTaskTypeWeight("classification", "azure", "gpt-4o", 0.6);

        var analysisWeights = tracker.GetTaskTypeWeights("analysis");
        var classificationWeights = tracker.GetTaskTypeWeights("classification");

        analysisWeights.Should().HaveCount(1);
        analysisWeights[0].TaskType.Should().Be("analysis");

        classificationWeights.Should().HaveCount(1);
        classificationWeights[0].TaskType.Should().Be("classification");
    }

    [Fact]
    public void ModelPerformanceTracker_SelectByWeight_NoEligible_ReturnsNull()
    {
        var tracker = CreateTracker();

        var result = tracker.SelectByWeight("cost");

        result.Should().BeNull();
    }

    [Fact]
    public void ModelPerformanceTracker_SelectByWeight_WithTaskTypeWeights_ReturnsEligibleModel()
    {
        var opts = new ModelRouterOptions { MinSamplesForAdaptive = 1 };
        var tracker = CreateTracker(opts);

        tracker.RecordOutcome("openai", "gpt-4", true, 100.0, 0.01, 0.9);
        tracker.SetTaskTypeWeight("analysis", "openai", "gpt-4", 0.9);

        var result = tracker.SelectByWeight("default", "analysis");

        result.Should().NotBeNull();
        result!.Provider.Should().Be("openai");
        result.Model.Should().Be("gpt-4");
    }

    [Fact]
    public void ModelPerformanceTracker_SelectByWeight_WithGlobalWeights_ReturnsEligibleModel()
    {
        var opts = new ModelRouterOptions { MinSamplesForAdaptive = 1 };
        var tracker = CreateTracker(opts);

        tracker.RecordOutcome("openai", "gpt-4", true, 100.0, 0.01, 0.9);
        tracker.SetRoutingWeight("openai", "gpt-4", 0.9, "top");

        var result = tracker.SelectByWeight("default");

        result.Should().NotBeNull();
        result!.Provider.Should().Be("openai");
        result.Model.Should().Be("gpt-4");
    }

    [Fact]
    public void ModelPerformanceTracker_SelectByWeight_FallsBackToStrategy_WhenNoWeights()
    {
        var opts = new ModelRouterOptions { MinSamplesForAdaptive = 1 };
        var tracker = CreateTracker(opts);

        // Record two models, no routing weights set
        tracker.RecordOutcome("openai", "cheap", true, 300.0, 0.001, 0.8);
        tracker.RecordOutcome("openai", "expensive", true, 100.0, 0.05, 0.95);

        var result = tracker.SelectByWeight("cost");

        // Falls back to GetBestModelForStrategy("cost") which picks cheapest
        result.Should().NotBeNull();
        result!.Model.Should().Be("cheap");
    }

    [Fact]
    public void ModelPerformanceTracker_CompositeScore_HighPerformer_HigherThanLow()
    {
        var opts = new ModelRouterOptions { MinSamplesForAdaptive = 1 };
        var tracker = CreateTracker(opts);

        tracker.RecordOutcome("openai", "great", true, 100.0, 0.01, 0.95);
        tracker.RecordOutcome("openai", "poor", false, 5000.0, 0.50, 0.1);

        var greatScore = tracker.GetScore("openai", "great");
        var poorScore = tracker.GetScore("openai", "poor");

        greatScore.Should().NotBeNull();
        poorScore.Should().NotBeNull();
        greatScore!.CompositeScore.Should().BeGreaterThan(poorScore!.CompositeScore);
    }

    // ══════════════════════════════════════════════════════════════
    //  AdaptiveRoutingWeightEngine tests
    // ══════════════════════════════════════════════════════════════

    private static AdaptiveRoutingWeightEngine CreateEngine(
        Mock<IModelPerformanceTracker>? trackerMock = null,
        Mock<IEventBus>? eventBusMock = null,
        ModelRouterOptions? opts = null)
    {
        trackerMock ??= new Mock<IModelPerformanceTracker>();
        eventBusMock ??= new Mock<IEventBus>();
        eventBusMock.Setup(e => e.PublishAsync(It.IsAny<SystemEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var logger = new Mock<ILogger<AdaptiveRoutingWeightEngine>>();

        return new AdaptiveRoutingWeightEngine(
            trackerMock.Object,
            eventBusMock.Object,
            Opts(opts),
            logger.Object);
    }

    [Fact]
    public async Task AdaptiveRoutingWeightEngine_AdjustWeightsAsync_NoEligible_ReturnsEmptyReport()
    {
        var trackerMock = new Mock<IModelPerformanceTracker>();
        trackerMock.Setup(t => t.GetAllScores()).Returns(new List<ModelPerformanceScore>());

        var engine = CreateEngine(trackerMock);

        var report = await engine.AdjustWeightsAsync();

        report.ModelsEvaluated.Should().Be(0);
        report.GlobalWeights.Should().BeEmpty();
        report.TaskTypeWeights.Should().BeEmpty();
        report.Adjustments.Should().BeEmpty();
        report.TopModel.Should().Be("unknown");
        report.BottomModel.Should().Be("unknown");
    }

    [Fact]
    public async Task AdaptiveRoutingWeightEngine_AdjustWeightsAsync_InsufficientSamples_ReturnsEmptyReport()
    {
        var opts = new ModelRouterOptions { MinSamplesForAdaptive = 100 };
        var trackerMock = new Mock<IModelPerformanceTracker>();
        trackerMock.Setup(t => t.GetAllScores()).Returns(new List<ModelPerformanceScore>
        {
            MakeScore("openai", "gpt-4", sampleCount: 5) // below min 100
        });

        var engine = CreateEngine(trackerMock, opts: opts);

        var report = await engine.AdjustWeightsAsync();

        report.ModelsEvaluated.Should().Be(0);
    }

    [Fact]
    public async Task AdaptiveRoutingWeightEngine_AdjustWeightsAsync_SevereFailure_DemotesAggressively()
    {
        var opts = DefaultOptions();
        var trackerMock = new Mock<IModelPerformanceTracker>();
        var score = MakeScore("openai", "gpt-4", successRate: 0.2, compositeScore: 0.3, sampleCount: 50);

        trackerMock.Setup(t => t.GetAllScores()).Returns(new List<ModelPerformanceScore> { score });
        trackerMock.Setup(t => t.GetRoutingWeight("openai", "gpt-4"))
            .Returns(new ModelRoutingWeight("openai", "gpt-4", 0.5, 0.5, "initial", DateTimeOffset.UtcNow));
        trackerMock.Setup(t => t.GetTaskTypeWeights(It.IsAny<string>()))
            .Returns(new List<TaskTypeModelWeight>());

        var engine = CreateEngine(trackerMock, opts: opts);

        var report = await engine.AdjustWeightsAsync();

        report.ModelsEvaluated.Should().Be(1);
        report.GlobalWeights.Should().HaveCount(1);
        // Severe failure: newWeight = max(0.05, 0.5 - 0.15*2) = max(0.05, 0.2) = 0.2
        report.GlobalWeights[0].Weight.Should().Be(0.2);
        report.GlobalWeights[0].AdjustmentReason.Should().Contain("severe-failure");
    }

    [Fact]
    public async Task AdaptiveRoutingWeightEngine_AdjustWeightsAsync_TopPerformer_Promotes()
    {
        var opts = DefaultOptions();
        var trackerMock = new Mock<IModelPerformanceTracker>();

        // Two models: one top performer, one okay model to create range
        var topScore = MakeScore("openai", "top-model", successRate: 0.95, compositeScore: 0.9, sampleCount: 50);
        var lowScore = MakeScore("azure", "low-model", successRate: 0.95, compositeScore: 0.3, sampleCount: 50);

        trackerMock.Setup(t => t.GetAllScores()).Returns(new List<ModelPerformanceScore> { topScore, lowScore });
        trackerMock.Setup(t => t.GetRoutingWeight("openai", "top-model"))
            .Returns(new ModelRoutingWeight("openai", "top-model", 0.5, 0.5, "initial", DateTimeOffset.UtcNow));
        trackerMock.Setup(t => t.GetRoutingWeight("azure", "low-model"))
            .Returns(new ModelRoutingWeight("azure", "low-model", 0.5, 0.5, "initial", DateTimeOffset.UtcNow));
        trackerMock.Setup(t => t.GetTaskTypeWeights(It.IsAny<string>()))
            .Returns(new List<TaskTypeModelWeight>());

        var engine = CreateEngine(trackerMock, opts: opts);

        var report = await engine.AdjustWeightsAsync();

        report.ModelsEvaluated.Should().Be(2);
        // Top model (relativePosition = 1.0 >= 0.7, successRate 0.95 >= 0.9): promoted by +0.10
        var topWeight = report.GlobalWeights.First(w => w.Model == "top-model");
        topWeight.Weight.Should().Be(0.6);
        topWeight.AdjustmentReason.Should().Contain("top-performer");
    }

    [Fact]
    public async Task AdaptiveRoutingWeightEngine_AdjustWeightsAsync_PublishesEvent()
    {
        var opts = DefaultOptions();
        var trackerMock = new Mock<IModelPerformanceTracker>();
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.Setup(e => e.PublishAsync(It.IsAny<SystemEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var score = MakeScore("openai", "gpt-4", successRate: 0.95, compositeScore: 0.8, sampleCount: 50);
        trackerMock.Setup(t => t.GetAllScores()).Returns(new List<ModelPerformanceScore> { score });
        trackerMock.Setup(t => t.GetRoutingWeight(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((ModelRoutingWeight?)null);
        trackerMock.Setup(t => t.GetTaskTypeWeights(It.IsAny<string>()))
            .Returns(new List<TaskTypeModelWeight>());

        var engine = CreateEngine(trackerMock, eventBusMock, opts);

        await engine.AdjustWeightsAsync();

        eventBusMock.Verify(
            e => e.PublishAsync(
                It.Is<SystemEvent>(ev => ev.EventType == "model-router.weights.adjusted"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AdaptiveRoutingWeightEngine_AdjustWeightsAsync_CancellationToken_ThrowsWhenCancelled()
    {
        var engine = CreateEngine();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => engine.AdjustWeightsAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AdaptiveRoutingWeightEngine_AdjustWeightsAsync_PoorSuccess_Demotes()
    {
        var opts = DefaultOptions();
        var trackerMock = new Mock<IModelPerformanceTracker>();
        var score = MakeScore("openai", "gpt-4", successRate: 0.5, compositeScore: 0.4, sampleCount: 50);

        trackerMock.Setup(t => t.GetAllScores()).Returns(new List<ModelPerformanceScore> { score });
        trackerMock.Setup(t => t.GetRoutingWeight("openai", "gpt-4"))
            .Returns(new ModelRoutingWeight("openai", "gpt-4", 0.6, 0.5, "prev", DateTimeOffset.UtcNow));
        trackerMock.Setup(t => t.GetTaskTypeWeights(It.IsAny<string>()))
            .Returns(new List<TaskTypeModelWeight>());

        var engine = CreateEngine(trackerMock, opts: opts);

        var report = await engine.AdjustWeightsAsync();

        // Poor success (0.3 <= 0.5 < 0.6): newWeight = max(0.05, 0.6 - 0.15) = 0.45
        var weight = report.GlobalWeights.First(w => w.Model == "gpt-4");
        weight.Weight.Should().Be(0.45);
        weight.AdjustmentReason.Should().Contain("poor-success");
    }

    [Fact]
    public async Task AdaptiveRoutingWeightEngine_AdjustWeightsAsync_Recovery_PromotesSlowly()
    {
        var opts = DefaultOptions();
        var trackerMock = new Mock<IModelPerformanceTracker>();

        // Single model so relativePosition = 0.5.
        // successRate 0.85 >= 0.8, but not >= 0.9 so top-performer branch not hit.
        // successRate 0.85 >= 0.7 but relativePosition 0.5 >= 0.3 so underperforming branch not hit.
        // oldWeight 0.4 < 0.5 and successRate 0.85 >= 0.8 -> recovery branch
        var score = MakeScore("openai", "gpt-4", successRate: 0.85, compositeScore: 0.7, sampleCount: 50);

        trackerMock.Setup(t => t.GetAllScores()).Returns(new List<ModelPerformanceScore> { score });
        trackerMock.Setup(t => t.GetRoutingWeight("openai", "gpt-4"))
            .Returns(new ModelRoutingWeight("openai", "gpt-4", 0.4, 0.5, "demoted", DateTimeOffset.UtcNow));
        trackerMock.Setup(t => t.GetTaskTypeWeights(It.IsAny<string>()))
            .Returns(new List<TaskTypeModelWeight>());

        var engine = CreateEngine(trackerMock, opts: opts);

        var report = await engine.AdjustWeightsAsync();

        // Recovery: newWeight = min(1.0, 0.4 + 0.05) = 0.45
        var weight = report.GlobalWeights.First(w => w.Model == "gpt-4");
        weight.Weight.Should().Be(0.45);
        weight.AdjustmentReason.Should().Contain("recovery");
    }

    [Fact]
    public async Task AdaptiveRoutingWeightEngine_AdjustWeightsAsync_TaskTypeWeights_AdjustsForConfiguredModel()
    {
        var opts = DefaultOptions();
        var trackerMock = new Mock<IModelPerformanceTracker>();

        // "analysis" maps to "openai.gpt-4.1" in default options - that model gets a +0.1 affinity bonus
        var score = MakeScore("openai", "openai.gpt-4.1", successRate: 0.9, compositeScore: 0.7, sampleCount: 50);

        trackerMock.Setup(t => t.GetAllScores()).Returns(new List<ModelPerformanceScore> { score });
        trackerMock.Setup(t => t.GetRoutingWeight(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((ModelRoutingWeight?)null);
        trackerMock.Setup(t => t.GetTaskTypeWeights(It.IsAny<string>()))
            .Returns(new List<TaskTypeModelWeight>());

        var engine = CreateEngine(trackerMock, opts: opts);

        var report = await engine.AdjustWeightsAsync();

        // For "analysis" task type, openai.gpt-4.1 is the configured default => gets affinity bonus 0.1
        // taskWeight = clamp((0.7*0.8) + (0.9*0.2) + 0.1, 0.05, 1.0) = clamp(0.56 + 0.18 + 0.1, 0.05, 1.0) = 0.84
        var analysisWeights = report.TaskTypeWeights.Where(w => w.TaskType == "analysis").ToList();
        analysisWeights.Should().HaveCount(1);
        analysisWeights[0].Weight.Should().Be(0.84);

        // For "classification" task type, openai.gpt-4.1 is NOT the configured model (azure.gpt-4o-mini is)
        // taskWeight = clamp((0.7*0.8) + (0.9*0.2) + 0, 0.05, 1.0) = clamp(0.74, 0.05, 1.0) = 0.74
        var classificationWeights = report.TaskTypeWeights.Where(w => w.TaskType == "classification").ToList();
        classificationWeights.Should().HaveCount(1);
        classificationWeights[0].Weight.Should().Be(0.74);
    }

    // ══════════════════════════════════════════════════════════════
    //  ModelRouterOptions tests
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void ModelRouterOptions_Defaults_HaveExpectedValues()
    {
        var opts = new ModelRouterOptions();

        opts.DefaultProvider.Should().Be("openai");
        opts.DefaultModel.Should().Be("openai.gpt-4.1-mini");
        opts.CostOptimizedProvider.Should().Be("openai");
        opts.CostOptimizedModel.Should().Be("openai.gpt-4.1-nano");
        opts.LatencyOptimizedProvider.Should().Be("azure-openai");
        opts.LatencyOptimizedModel.Should().Be("azure.gpt-4o-mini");
        opts.QualityOptimizedProvider.Should().Be("openai");
        opts.QualityOptimizedModel.Should().Be("openai.gpt-4.1");
        opts.EnableAdaptiveRouting.Should().BeTrue();
        opts.MinSamplesForAdaptive.Should().Be(10);
        opts.TaskTypeModelMap.Should().HaveCount(5);
        opts.FallbackChains.Should().HaveCount(4);
    }
}
