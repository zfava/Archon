using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Evaluation;
using ArchonAI.Core.Models.Explanation;
using ArchonAI.Core.Models.Knowledge;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Reasoning;
using ArchonAI.Core.Models.Simulation;
using ArchonAI.Reasoner;
using ArchonAI.Registry;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace ArchonAI.Reasoner.Tests;

#region ReasoningEngine Tests

public sealed class ReasoningEngineTests
{
    private readonly Mock<IPlanningFeedbackStore> _feedbackStore = new();
    private readonly ReasoningEngine _sut;

    public ReasoningEngineTests()
    {
        _sut = new ReasoningEngine(_feedbackStore.Object);
    }

    private static Objective CreateObjective(string title = "Test Objective") =>
        new(Guid.NewGuid(), title, "desc", new Dictionary<string, string>(), DateTimeOffset.UtcNow, null);

    private static ExecutionResult CreateResult(bool success) =>
        new(Guid.NewGuid(), success, success ? "ok" : "fail",
            new Dictionary<string, string>(),
            Array.Empty<string>(),
            success ? Array.Empty<string>() : new[] { "error" },
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task ReasoningEngine_EvaluateAsync_AllSucceed_ReturnsStandardStrategy()
    {
        // Arrange
        var objective = CreateObjective("Deploy Service");
        var results = new List<ExecutionResult>
        {
            CreateResult(true),
            CreateResult(true),
            CreateResult(true)
        };

        _feedbackStore
            .Setup(x => x.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var summary = await _sut.EvaluateAsync(objective, results);

        // Assert
        summary.Should().Contain("StrategyHint=standard");
        summary.Should().Contain("Successes=3");
        summary.Should().Contain("Failures=0");
        summary.Should().Contain("Deploy Service");

        _feedbackStore.Verify(x => x.AddAsync(
            It.Is<PlanningFeedback>(f =>
                f.Strategy == "standard" &&
                f.WasSuccessful == true &&
                f.Capability == "operation-execution"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReasoningEngine_EvaluateAsync_SomeFail_ReturnsSafeModeStrategy()
    {
        // Arrange
        var objective = CreateObjective("Risky Deploy");
        var results = new List<ExecutionResult>
        {
            CreateResult(true),
            CreateResult(false),
            CreateResult(true)
        };

        _feedbackStore
            .Setup(x => x.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var summary = await _sut.EvaluateAsync(objective, results);

        // Assert
        summary.Should().Contain("StrategyHint=safe-mode");
        summary.Should().Contain("Successes=2");
        summary.Should().Contain("Failures=1");

        _feedbackStore.Verify(x => x.AddAsync(
            It.Is<PlanningFeedback>(f =>
                f.Strategy == "safe-mode" &&
                f.WasSuccessful == false &&
                f.Capability == "workflow-orchestration"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReasoningEngine_EvaluateAsync_EmptyResults_ReturnsStandardWithZeroCounts()
    {
        // Arrange
        var objective = CreateObjective("Empty Run");
        var results = new List<ExecutionResult>();

        _feedbackStore
            .Setup(x => x.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var summary = await _sut.EvaluateAsync(objective, results);

        // Assert
        summary.Should().Contain("StrategyHint=standard");
        summary.Should().Contain("Successes=0");
        summary.Should().Contain("Failures=0");
    }

    [Fact]
    public async Task ReasoningEngine_EvaluateAsync_CancelledToken_ThrowsOperationCanceled()
    {
        // Arrange
        var objective = CreateObjective();
        var results = new List<ExecutionResult> { CreateResult(true) };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => _sut.EvaluateAsync(objective, results, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

#endregion

#region EconomicEvaluator Tests

public sealed class EconomicEvaluatorTests
{
    private readonly Mock<ISimulationEngine> _simulationEngine = new();
    private readonly Mock<IPlanningFeedbackStore> _feedbackStore = new();
    private readonly Mock<ILogger<EconomicEvaluator>> _logger = new();
    private readonly EconomicEvaluator _sut;

    public EconomicEvaluatorTests()
    {
        _sut = new EconomicEvaluator(_simulationEngine.Object, _feedbackStore.Object, _logger.Object);
    }

    private static Objective CreateObjective(string title = "Test Objective", decimal? budget = null, DateTimeOffset? due = null)
    {
        var constraints = new Dictionary<string, string>();
        if (budget.HasValue) constraints["budget"] = budget.Value.ToString();
        return new(Guid.NewGuid(), title, "desc", constraints, DateTimeOffset.UtcNow, due);
    }

    private static WorkflowDefinition CreateWorkflow(Guid? objectiveId = null) =>
        new(objectiveId ?? Guid.NewGuid(), "balanced", "Test workflow",
            Array.Empty<WorkflowStepDefinition>(), DateTimeOffset.UtcNow);

    private static SimulationResult CreateSimulation(
        string strategy,
        double successProb = 0.85,
        double latencyMs = 3600000,
        decimal cost = 100m,
        int riskCount = 0) =>
        new(Guid.NewGuid(), strategy, successProb, 1.0 - successProb, latencyMs, cost,
            Enumerable.Range(0, riskCount).Select(i => $"risk-{i}").ToList(),
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task EconomicEvaluator_EvaluateSingleStrategyAsync_ReturnsScoreWithFactors()
    {
        // Arrange
        var objective = CreateObjective();
        var workflow = CreateWorkflow();
        var simResult = CreateSimulation("balanced", successProb: 0.9, latencyMs: 1_800_000, cost: 50m);

        _simulationEngine
            .Setup(x => x.SimulateWorkflowAsync(objective, workflow, "balanced", It.IsAny<CancellationToken>()))
            .ReturnsAsync(simResult);
        _feedbackStore
            .Setup(x => x.GetRecentAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlanningFeedback>());

        // Act
        var eval = await _sut.EvaluateSingleStrategyAsync(objective, workflow, "balanced");

        // Assert
        eval.Strategy.Should().Be("balanced");
        eval.EconomicScore.Should().BeGreaterThan(0);
        eval.FactorScores.Should().ContainKeys("cost", "impact", "successProbability", "executionTime");
        eval.ScoreBreakdown.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EconomicEvaluator_EvaluateStrategiesAsync_MultipleStrategies_ReturnsBestFirst()
    {
        // Arrange
        var objective = CreateObjective();
        var workflow = CreateWorkflow();
        var strategies = new List<string> { "balanced", "aggressive" };

        _simulationEngine
            .Setup(x => x.SimulateWorkflowAsync(objective, workflow, "balanced", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSimulation("balanced", successProb: 0.9, cost: 50m));
        _simulationEngine
            .Setup(x => x.SimulateWorkflowAsync(objective, workflow, "aggressive", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSimulation("aggressive", successProb: 0.5, cost: 200m));
        _feedbackStore
            .Setup(x => x.GetRecentAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlanningFeedback>());

        // Act
        var result = await _sut.EvaluateStrategiesAsync(objective, workflow, strategies);

        // Assert
        result.Evaluations.Should().HaveCount(2);
        result.BestStrategy.Should().NotBeNull();
        result.BestStrategy.EconomicScore.Should()
            .BeGreaterThanOrEqualTo(result.Evaluations[^1].EconomicScore);
        result.SelectionRationale.Should().NotBeNullOrWhiteSpace();
        result.WeightsUsed.Should().Be(EconomicWeights.Default);
    }

    [Fact]
    public async Task EconomicEvaluator_EvaluateStrategiesAsync_HistoricalBoost_AffectsImpactScore()
    {
        // Arrange
        var objective = CreateObjective();
        var workflow = CreateWorkflow();
        var strategies = new List<string> { "reliable" };

        _simulationEngine
            .Setup(x => x.SimulateWorkflowAsync(objective, workflow, "reliable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSimulation("reliable", successProb: 0.7, cost: 100m));

        // Historical feedback: 8 out of 10 successes for "reliable"
        var historicalFeedback = Enumerable.Range(0, 10)
            .Select(i => new PlanningFeedback("reliable", "execution", i < 8, "rationale", DateTimeOffset.UtcNow))
            .ToList();
        _feedbackStore
            .Setup(x => x.GetRecentAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(historicalFeedback);

        // Act
        var result = await _sut.EvaluateStrategiesAsync(objective, workflow, strategies);

        // Assert
        var eval = result.BestStrategy;
        eval.Strategy.Should().Be("reliable");
        // Historical boost = (0.8 - 0.5) * 0.1 = 0.03, so impact = clamp(0.7 + 0.03) = 0.73
        eval.ExpectedImpact.Should().BeGreaterThan(0.7);
        eval.FactorScores["historicalBoost"].Should().BeApproximately(0.03, 0.001);
    }

    [Fact]
    public async Task EconomicEvaluator_EvaluateStrategiesAsync_CustomWeights_UsesProvidedWeights()
    {
        // Arrange
        var objective = CreateObjective();
        var workflow = CreateWorkflow();
        var strategies = new List<string> { "cost-optimized" };
        var customWeights = new EconomicWeights(
            CostWeight: 0.80,
            ImpactWeight: 0.10,
            SuccessProbabilityWeight: 0.05,
            ExecutionTimeWeight: 0.05);

        _simulationEngine
            .Setup(x => x.SimulateWorkflowAsync(objective, workflow, "cost-optimized", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSimulation("cost-optimized", successProb: 0.6, cost: 10m));
        _feedbackStore
            .Setup(x => x.GetRecentAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlanningFeedback>());

        // Act
        var result = await _sut.EvaluateStrategiesAsync(objective, workflow, strategies, customWeights);

        // Assert
        result.WeightsUsed.Should().Be(customWeights);
        result.BestStrategy.ScoreBreakdown.Should().Contain("*0.8 ");
    }

    [Fact]
    public async Task EconomicEvaluator_EvaluateStrategiesAsync_CancelledToken_ThrowsOperationCanceled()
    {
        // Arrange
        var objective = CreateObjective();
        var workflow = CreateWorkflow();
        var strategies = new List<string> { "test" };
        using var cts = new CancellationTokenSource();

        _feedbackStore
            .Setup(x => x.GetRecentAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlanningFeedback>());

        // Cancel before the loop iteration
        _simulationEngine
            .Setup(x => x.SimulateWorkflowAsync(objective, workflow, "test", It.IsAny<CancellationToken>()))
            .Returns(async (Objective _, WorkflowDefinition _, string _, CancellationToken ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return CreateSimulation("test");
            });

        // Act
        var act = () => _sut.EvaluateStrategiesAsync(objective, workflow, strategies, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

#endregion

#region ExplanationEngine Tests

public sealed class ExplanationEngineTests
{
    private readonly Mock<IEconomicEvaluator> _economicEvaluator = new();
    private readonly Mock<IAgentCapabilityRegistry> _capabilityRegistry = new();
    private readonly Mock<ILogger<ExplanationEngine>> _logger = new();
    private readonly ExplanationEngine _sut;

    public ExplanationEngineTests()
    {
        _sut = new ExplanationEngine(_economicEvaluator.Object, _capabilityRegistry.Object, _logger.Object);
    }

    private static StrategyEvaluation CreateStrategyEvaluation(string strategy, double score) =>
        new(strategy, 50m, 0.8, 0.85, 2.0, score, $"score={score:F3}",
            new Dictionary<string, double>
            {
                ["cost"] = 0.9,
                ["impact"] = 0.8,
                ["successProbability"] = 0.85,
                ["executionTime"] = 0.7
            },
            DateTimeOffset.UtcNow);

    private static EconomicEvaluationResult CreateEconomicResult(params StrategyEvaluation[] evals)
    {
        var sorted = evals.OrderByDescending(e => e.EconomicScore).ToList();
        return new(sorted, sorted[0], $"Best: {sorted[0].Strategy}", EconomicWeights.Default, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ExplanationEngine_ExplainStrategyChoiceAsync_ReturnsStrategyExplanation()
    {
        // Arrange
        var goalId = Guid.NewGuid();
        var candidates = new List<string> { "balanced", "aggressive" };

        var balancedEval = CreateStrategyEvaluation("balanced", 0.85);
        var aggressiveEval = CreateStrategyEvaluation("aggressive", 0.65);
        var econResult = CreateEconomicResult(balancedEval, aggressiveEval);

        _economicEvaluator
            .Setup(x => x.EvaluateStrategiesAsync(
                It.IsAny<Objective>(), It.IsAny<WorkflowDefinition>(),
                candidates, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(econResult);

        // Act
        var result = await _sut.ExplainStrategyChoiceAsync(goalId, "Scale API", candidates);

        // Assert
        result.GoalId.Should().Be(goalId);
        result.GoalTitle.Should().Be("Scale API");
        result.ChosenStrategy.Should().Be("balanced");
        result.EconomicScore.Should().Be(0.85);
        result.Factors.Should().NotBeEmpty();
        result.Alternatives.Should().HaveCount(1);
        result.Alternatives[0].Strategy.Should().Be("aggressive");
    }

    [Fact]
    public async Task ExplanationEngine_ExplainAgentSelectionAsync_WithResult_ReturnsAgentExplanation()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var selection = new AgentSelectionResult(agentId, "FinanceBot", 0.92, 0.95, 300, 1.5m, "Best success rate");

        _capabilityRegistry
            .Setup(x => x.SelectBestAgentAsync("financial-analysis", "budget-review", It.IsAny<CancellationToken>()))
            .ReturnsAsync(selection);

        var altId = Guid.NewGuid();
        _capabilityRegistry
            .Setup(x => x.QueryByCapabilityAsync("financial-analysis", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentCapabilityProfile>
            {
                new(agentId, "FinanceBot", "1.0", new[] { "financial-analysis" },
                    Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                    300, 500, 1.5m, 100, 95, 5, 0.95, 10.0, DateTimeOffset.UtcNow),
                new(altId, "AltBot", "1.0", new[] { "financial-analysis" },
                    Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                    600, 1000, 3.0m, 50, 40, 10, 0.80, 5.0, DateTimeOffset.UtcNow)
            });

        // Act
        var result = await _sut.ExplainAgentSelectionAsync("financial-analysis", "budget-review");

        // Assert
        result.SelectedAgentId.Should().Be(agentId);
        result.SelectedAgentName.Should().Be("FinanceBot");
        result.SelectionScore.Should().Be(0.92);
        result.Factors.Should().HaveCount(4);
        result.Alternatives.Should().HaveCount(1);
        result.Alternatives[0].AgentName.Should().Be("AltBot");
    }

    [Fact]
    public async Task ExplanationEngine_ExplainAgentSelectionAsync_NoAgent_ReturnsEmptyExplanation()
    {
        // Arrange
        _capabilityRegistry
            .Setup(x => x.SelectBestAgentAsync("nonexistent-cap", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentSelectionResult?)null);

        // Act
        var result = await _sut.ExplainAgentSelectionAsync("nonexistent-cap", null);

        // Assert
        result.SelectedAgentId.Should().Be(Guid.Empty);
        result.SelectedAgentName.Should().Be("None");
        result.SelectionScore.Should().Be(0);
        result.SelectionReason.Should().Contain("No agent found");
        result.Factors.Should().BeEmpty();
        result.Alternatives.Should().BeEmpty();
    }

    [Fact]
    public async Task ExplanationEngine_ExplainDecisionAsync_ReturnsCombinedExplanation()
    {
        // Arrange
        var goalId = Guid.NewGuid();
        var candidates = new List<string> { "balanced" };
        var agentId = Guid.NewGuid();

        var evalResult = CreateEconomicResult(CreateStrategyEvaluation("balanced", 0.80));
        _economicEvaluator
            .Setup(x => x.EvaluateStrategiesAsync(
                It.IsAny<Objective>(), It.IsAny<WorkflowDefinition>(),
                candidates, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evalResult);

        var selection = new AgentSelectionResult(agentId, "OpsBot", 0.88, 0.90, 200, 0.5m, "High throughput");
        _capabilityRegistry
            .Setup(x => x.SelectBestAgentAsync("operations", "deploy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(selection);
        _capabilityRegistry
            .Setup(x => x.QueryByCapabilityAsync("operations", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentCapabilityProfile>
            {
                new(agentId, "OpsBot", "1.0", new[] { "operations" },
                    Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                    200, 400, 0.5m, 200, 180, 20, 0.90, 20.0, DateTimeOffset.UtcNow)
            });

        // Act
        var result = await _sut.ExplainDecisionAsync(goalId, "Deploy v2", candidates, "operations", "deploy");

        // Assert
        result.DecisionType.Should().Be("StrategyAndAgent");
        result.StrategyExplanation.Should().NotBeNull();
        result.StrategyExplanation!.ChosenStrategy.Should().Be("balanced");
        result.AgentExplanation.Should().NotBeNull();
        result.AgentExplanation!.SelectedAgentName.Should().Be("OpsBot");
        result.Summary.Should().Contain("balanced").And.Contain("OpsBot");
    }

    [Fact]
    public async Task ExplanationEngine_ExplainStrategyChoiceAsync_CancelledToken_ThrowsOperationCanceled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _economicEvaluator
            .Setup(x => x.EvaluateStrategiesAsync(
                It.IsAny<Objective>(), It.IsAny<WorkflowDefinition>(),
                It.IsAny<IReadOnlyList<string>>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var act = () => _sut.ExplainStrategyChoiceAsync(Guid.NewGuid(), "Goal", new List<string> { "s1" }, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

#endregion

#region OutcomeEvaluator Tests

public sealed class OutcomeEvaluatorTests
{
    private readonly Mock<IKnowledgeGraphStore> _knowledgeStore = new();
    private readonly Mock<IPlanningFeedbackStore> _feedbackStore = new();
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<ILogger<OutcomeEvaluator>> _logger = new();
    private readonly OutcomeEvaluator _sut;

    public OutcomeEvaluatorTests()
    {
        _sut = new OutcomeEvaluator(
            _knowledgeStore.Object, _feedbackStore.Object,
            _eventBus.Object, _logger.Object);

        // Default setups for knowledge store and event bus
        _knowledgeStore
            .Setup(x => x.UpsertNodeAsync(It.IsAny<KnowledgeNode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _knowledgeStore
            .Setup(x => x.UpsertRelationshipAsync(It.IsAny<KnowledgeRelationship>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _feedbackStore
            .Setup(x => x.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _eventBus
            .Setup(x => x.PublishAsync(It.IsAny<SystemEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static StrategySimulationResult CreateSimulation(
        Guid graphId,
        string strategy,
        double overallSuccessProb = 0.9,
        double riskScore = 0.3,
        double totalDurationHours = 5.0,
        decimal totalCost = 100m,
        params SimulatedNodeResult[] nodes) =>
        new(Guid.NewGuid(), graphId, strategy,
            new ExpectedOutcome(overallSuccessProb, overallSuccessProb >= 0.5, "success", 0.85,
                nodes.Length, 2, 1),
            riskScore, riskScore >= 0.6 ? "high" : riskScore >= 0.3 ? "medium" : "low",
            nodes.ToList(),
            riskScore >= 0.5 ? new[] { "risk-a" } : Array.Empty<string>(),
            Array.Empty<string>(),
            totalDurationHours, totalCost, DateTimeOffset.UtcNow);

    private static TaskGraphExecutionResult CreateActualResult(
        Guid graphId,
        Guid goalId,
        string strategy,
        bool overallSuccess = true,
        double totalDurationHours = 5.0,
        decimal totalCost = 100m,
        params TaskNodeExecutionResult[] nodes) =>
        new(graphId, goalId, strategy, overallSuccess, nodes.ToList(),
            totalDurationHours, totalCost, DateTimeOffset.UtcNow);

    [Fact]
    public async Task OutcomeEvaluator_EvaluateAsync_MatchingNodes_ComputesAccurateMetrics()
    {
        // Arrange
        var graphId = Guid.NewGuid();
        var goalId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        var simNode = new SimulatedNodeResult(nodeId, "ProcessData", "operations-agent",
            0.9, 2.0, 50m, true, Array.Empty<string>());

        var simulation = CreateSimulation(graphId, "balanced",
            overallSuccessProb: 0.9, riskScore: 0.3,
            totalDurationHours: 5.0, totalCost: 100m, simNode);

        var actualNode = new TaskNodeExecutionResult(nodeId, "ProcessData", "operations-agent",
            true, 2.1, 52m, 1, Array.Empty<string>(), DateTimeOffset.UtcNow);

        var actualResult = CreateActualResult(graphId, goalId, "balanced",
            overallSuccess: true, totalDurationHours: 5.2, totalCost: 105m, actualNode);

        // Act
        var result = await _sut.EvaluateAsync(simulation, actualResult);

        // Assert
        result.Strategy.Should().Be("balanced");
        result.GoalId.Should().Be(goalId);
        result.GraphId.Should().Be(graphId);
        result.SuccessMetrics.SucceededNodes.Should().Be(1);
        result.SuccessMetrics.FailedNodes.Should().Be(0);
        result.SuccessMetrics.SuccessRate.Should().Be(1.0);
        result.SuccessMetrics.OverallScore.Should().BeGreaterThan(0.5);
        result.NodeEvaluations.Should().HaveCount(1);
        result.NodeEvaluations[0].ActualSuccess.Should().BeTrue();
        result.NodeEvaluations[0].PredictedSuccessProbability.Should().Be(0.9);
        result.OverallAssessment.Should().BeOneOf("excellent", "good", "fair", "poor");

        // Verify persistence
        _knowledgeStore.Verify(x => x.UpsertNodeAsync(
            It.Is<KnowledgeNode>(n => n.NodeType == "outcome_evaluation"),
            It.IsAny<CancellationToken>()), Times.Once);
        _feedbackStore.Verify(x => x.AddAsync(
            It.Is<PlanningFeedback>(f => f.Strategy == "balanced" && f.WasSuccessful == true),
            It.IsAny<CancellationToken>()), Times.Once);
        _eventBus.Verify(x => x.PublishAsync(
            It.Is<SystemEvent>(e => e.EventType == "reasoner.outcome.evaluated"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OutcomeEvaluator_EvaluateAsync_UnmatchedNodes_UsesDefaultPredictions()
    {
        // Arrange
        var graphId = Guid.NewGuid();
        var goalId = Guid.NewGuid();

        // Simulation has node A; actual has node B (different NodeId)
        var simNodeId = Guid.NewGuid();
        var actualNodeId = Guid.NewGuid();

        var simNode = new SimulatedNodeResult(simNodeId, "SimNode", "ops",
            0.9, 3.0, 80m, true, Array.Empty<string>());
        var simulation = CreateSimulation(graphId, "safe-mode",
            overallSuccessProb: 0.8, riskScore: 0.4,
            totalDurationHours: 4.0, totalCost: 80m, simNode);

        var actualNode = new TaskNodeExecutionResult(actualNodeId, "ActualNode", "ops",
            false, 1.0, 20m, 2, new[] { "timeout" }, DateTimeOffset.UtcNow);
        var actualResult = CreateActualResult(graphId, goalId, "safe-mode",
            overallSuccess: false, totalDurationHours: 1.0, totalCost: 20m, actualNode);

        // Act
        var result = await _sut.EvaluateAsync(simulation, actualResult);

        // Assert
        result.SuccessMetrics.FailedNodes.Should().Be(1);
        result.SuccessMetrics.SucceededNodes.Should().Be(0);
        result.Comparison.ActualSuccess.Should().BeFalse();

        // Unmatched node: predicted values default to 0
        var nodeEval = result.NodeEvaluations[0];
        nodeEval.PredictedSuccessProbability.Should().Be(0);
        nodeEval.PredictedDurationHours.Should().Be(0);
        nodeEval.PredictedCost.Should().Be(0);
        nodeEval.ActualSuccess.Should().BeFalse();

        // Feedback stored as unsuccessful
        _feedbackStore.Verify(x => x.AddAsync(
            It.Is<PlanningFeedback>(f => f.WasSuccessful == false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OutcomeEvaluator_GetEvaluationsForGoalAsync_EmptyRelationships_ReturnsEmptyList()
    {
        // Arrange
        var goalId = Guid.NewGuid();
        _knowledgeStore
            .Setup(x => x.QueryRelationshipsAsync($"goal:{goalId}", "has_evaluation", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeRelationship>());

        // Act
        var results = await _sut.GetEvaluationsForGoalAsync(goalId);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task OutcomeEvaluator_GetEvaluationsForStrategyAsync_EmptyRelationships_ReturnsEmptyList()
    {
        // Arrange
        _knowledgeStore
            .Setup(x => x.QueryRelationshipsAsync("strategy:conservative", "has_evaluation", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeRelationship>());

        // Act
        var results = await _sut.GetEvaluationsForStrategyAsync("conservative");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task OutcomeEvaluator_EvaluateAsync_CancelledToken_ThrowsOperationCanceled()
    {
        // Arrange
        var graphId = Guid.NewGuid();
        var goalId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var simulation = CreateSimulation(graphId, "balanced");
        var actualResult = CreateActualResult(graphId, goalId, "balanced");

        // Act
        var act = () => _sut.EvaluateAsync(simulation, actualResult, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task OutcomeEvaluator_EvaluateAsync_ScoreCalculation_WeightsAppliedCorrectly()
    {
        // Arrange: all nodes succeed with perfect predictions
        var graphId = Guid.NewGuid();
        var goalId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        var simNode = new SimulatedNodeResult(nodeId, "PerfectNode", "agent-a",
            0.95, 2.0, 50m, false, Array.Empty<string>());
        var simulation = CreateSimulation(graphId, "standard",
            overallSuccessProb: 0.95, riskScore: 0.2,
            totalDurationHours: 2.0, totalCost: 50m, simNode);

        var actualNode = new TaskNodeExecutionResult(nodeId, "PerfectNode", "agent-a",
            true, 2.0, 50m, 1, Array.Empty<string>(), DateTimeOffset.UtcNow);
        var actualResult = CreateActualResult(graphId, goalId, "standard",
            overallSuccess: true, totalDurationHours: 2.0, totalCost: 50m, actualNode);

        // Act
        var result = await _sut.EvaluateAsync(simulation, actualResult);

        // Assert
        // successRate = 1.0, accuracyScore should be high (correct prediction, perfect duration/cost)
        // durationAccuracy = 1.0 (0% deviation), costAccuracy = 1.0 (0% deviation)
        // riskPredictionCorrect = true (riskScore < 0.5 and actualSuccess)
        // overallScore = 1.0*0.30 + accuracy*0.25 + 1.0*0.20 + 1.0*0.15 + 1.0*0.10
        result.SuccessMetrics.SuccessRate.Should().Be(1.0);
        result.SuccessMetrics.DurationAccuracy.Should().Be(1.0);
        result.SuccessMetrics.CostAccuracy.Should().Be(1.0);
        result.SuccessMetrics.RiskPredictionAccuracy.Should().Be(1.0);
        result.SuccessMetrics.OverallScore.Should().BeGreaterThanOrEqualTo(0.9);
        result.OverallAssessment.Should().Be("excellent");
    }

    [Fact]
    public async Task OutcomeEvaluator_EvaluateAsync_MultipleNodes_MixedResults_ComputesCorrectCounts()
    {
        // Arrange
        var graphId = Guid.NewGuid();
        var goalId = Guid.NewGuid();
        var nodeId1 = Guid.NewGuid();
        var nodeId2 = Guid.NewGuid();
        var nodeId3 = Guid.NewGuid();

        var simNodes = new[]
        {
            new SimulatedNodeResult(nodeId1, "Step1", "agent-a", 0.9, 1.0, 30m, true, Array.Empty<string>()),
            new SimulatedNodeResult(nodeId2, "Step2", "agent-b", 0.8, 2.0, 40m, false, Array.Empty<string>()),
            new SimulatedNodeResult(nodeId3, "Step3", "agent-c", 0.95, 1.5, 30m, true, Array.Empty<string>())
        };
        var simulation = CreateSimulation(graphId, "balanced",
            overallSuccessProb: 0.85, riskScore: 0.3,
            totalDurationHours: 4.5, totalCost: 100m, simNodes);

        var actualNodes = new[]
        {
            new TaskNodeExecutionResult(nodeId1, "Step1", "agent-a", true, 1.1, 32m, 1, Array.Empty<string>(), DateTimeOffset.UtcNow),
            new TaskNodeExecutionResult(nodeId2, "Step2", "agent-b", false, 3.0, 60m, 2, new[] { "error" }, DateTimeOffset.UtcNow),
            new TaskNodeExecutionResult(nodeId3, "Step3", "agent-c", true, 1.4, 28m, 1, Array.Empty<string>(), DateTimeOffset.UtcNow)
        };
        var actualResult = CreateActualResult(graphId, goalId, "balanced",
            overallSuccess: false, totalDurationHours: 5.5, totalCost: 120m, actualNodes);

        // Act
        var result = await _sut.EvaluateAsync(simulation, actualResult);

        // Assert
        result.SuccessMetrics.TotalNodes.Should().Be(3);
        result.SuccessMetrics.SucceededNodes.Should().Be(2);
        result.SuccessMetrics.FailedNodes.Should().Be(1);
        result.SuccessMetrics.SuccessRate.Should().BeApproximately(2.0 / 3.0, 0.001);
        result.NodeEvaluations.Should().HaveCount(3);
        result.Comparison.ActualSuccess.Should().BeFalse();
        result.Recommendations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task OutcomeEvaluator_EvaluateAsync_DurationCostDeviation_GeneratesInsightsAndRecommendations()
    {
        // Arrange: significant deviation in duration and cost
        var graphId = Guid.NewGuid();
        var goalId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        var simNode = new SimulatedNodeResult(nodeId, "SlowNode", "agent-x",
            0.9, 1.0, 20m, true, Array.Empty<string>());
        var simulation = CreateSimulation(graphId, "aggressive",
            overallSuccessProb: 0.9, riskScore: 0.2,
            totalDurationHours: 2.0, totalCost: 40m, simNode);

        // Actual takes 4x longer and 3x cost
        var actualNode = new TaskNodeExecutionResult(nodeId, "SlowNode", "agent-x",
            true, 4.0, 60m, 1, Array.Empty<string>(), DateTimeOffset.UtcNow);
        var actualResult = CreateActualResult(graphId, goalId, "aggressive",
            overallSuccess: true, totalDurationHours: 8.0, totalCost: 120m, actualNode);

        // Act
        var result = await _sut.EvaluateAsync(simulation, actualResult);

        // Assert
        // Duration deviation = ((8 - 2) / 2) * 100 = 300%
        result.Comparison.DurationDeviationPercent.Should().BeGreaterThan(100);
        // Cost deviation = ((120 - 40) / 40) * 100 = 200%
        result.Comparison.CostDeviationPercent.Should().BeGreaterThan(100);

        // These large deviations should trigger insights/recommendations
        result.Insights.Should().Contain(i => i.Contains("longer") || i.Contains("more"));
        result.Recommendations.Should().Contain(r => r.Contains("duration") || r.Contains("cost") || r.Contains("Duration") || r.Contains("Cost"));
    }
}

#endregion
