using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Optimization;
using ArchonAI.Core.Models.OrganizationState;
using ArchonAI.Core.Models.Perception;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Reasoning;
using ArchonAI.Core.Models.Simulation;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.StrategicPlanner.Tests;

// ══════════════════════════════════════════════════════════════
//  GoalGenerator Tests
// ══════════════════════════════════════════════════════════════

public class GoalGeneratorTests
{
    private readonly Mock<IOrganizationStateEngine> _stateEngine = new();
    private readonly Mock<IBusinessPerceptionEngine> _perceptionEngine = new();
    private readonly Mock<IPerformanceAnalyzer> _performanceAnalyzer = new();
    private readonly Mock<IPlanner> _planner = new();
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<ILogger<GoalGenerator>> _logger = new();

    private GoalGenerator CreateSut() => new(
        _stateEngine.Object,
        _perceptionEngine.Object,
        _performanceAnalyzer.Object,
        _planner.Object,
        _eventBus.Object,
        _logger.Object);

    private void SetupEmptyState()
    {
        _stateEngine.Setup(s => s.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalState(
                Guid.NewGuid(), 1,
                Array.Empty<DepartmentState>(),
                Array.Empty<ResourceState>(),
                Array.Empty<CustomerState>(),
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow));

        _stateEngine.Setup(s => s.GetCustomersByHealthAsync(CustomerHealthStatus.AtRisk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CustomerState>());

        _perceptionEngine.Setup(p => p.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerceptionDashboard(
                0, 0,
                new Dictionary<SourceSystem, long>(),
                new Dictionary<ObservationCategory, long>(),
                Array.Empty<OperationalObservation>(),
                DateTimeOffset.UtcNow));

        _performanceAnalyzer.Setup(p => p.AnalyzeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerformanceReport(
                Array.Empty<AgentEfficiencyRecord>(),
                Array.Empty<ModelAccuracyRecord>(),
                Array.Empty<TaskCompletionRecord>(),
                Array.Empty<PerformanceRecommendation>(),
                1.0,
                DateTimeOffset.UtcNow));
    }

    // ── GenerateGoalsAsync ──────────────────────────────────

    [Fact]
    public async Task GoalGenerator_GenerateGoalsAsync_NoAnomalies_ReturnsEmptyGoals()
    {
        SetupEmptyState();
        var sut = CreateSut();

        var result = await sut.GenerateGoalsAsync();

        result.GeneratedGoals.Should().BeEmpty();
        result.AnomaliesDetected.Should().Be(0);
        result.TrendsEvaluated.Should().Be(0);
        result.SignalsAnalyzed.Should().Be(0);
    }

    [Fact]
    public async Task GoalGenerator_GenerateGoalsAsync_LowHealthDepartment_CreatesHighPriorityGoal()
    {
        SetupEmptyState();
        var dept = new DepartmentState(
            "eng", DepartmentType.Engineering, "Engineering",
            5, 10, 100, 0.5,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        _stateEngine.Setup(s => s.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalState(
                Guid.NewGuid(), 1,
                new[] { dept },
                Array.Empty<ResourceState>(),
                Array.Empty<CustomerState>(),
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow));

        var sut = CreateSut();
        var result = await sut.GenerateGoalsAsync();

        result.GeneratedGoals.Should().ContainSingle(g => g.Title.Contains("Engineering") && g.Priority == GoalPriority.High);
        result.AnomaliesDetected.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GoalGenerator_GenerateGoalsAsync_CriticallyLowHealth_CreatesCriticalPriorityGoal()
    {
        SetupEmptyState();
        var dept = new DepartmentState(
            "fin", DepartmentType.Finance, "Finance",
            2, 5, 50, 0.2,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        _stateEngine.Setup(s => s.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalState(
                Guid.NewGuid(), 1,
                new[] { dept },
                Array.Empty<ResourceState>(),
                Array.Empty<CustomerState>(),
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow));

        var sut = CreateSut();
        var result = await sut.GenerateGoalsAsync();

        result.GeneratedGoals.Should().Contain(g => g.Priority == GoalPriority.Critical);
    }

    [Fact]
    public async Task GoalGenerator_GenerateGoalsAsync_HighPendingTasks_CreatesBacklogGoal()
    {
        SetupEmptyState();
        var dept = new DepartmentState(
            "ops", DepartmentType.Operations, "Operations",
            3, 120, 200, 0.9,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        _stateEngine.Setup(s => s.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalState(
                Guid.NewGuid(), 1,
                new[] { dept },
                Array.Empty<ResourceState>(),
                Array.Empty<CustomerState>(),
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow));

        var sut = CreateSut();
        var result = await sut.GenerateGoalsAsync();

        result.GeneratedGoals.Should().Contain(g => g.Title.Contains("backlog") && g.Priority == GoalPriority.High);
    }

    [Fact]
    public async Task GoalGenerator_GenerateGoalsAsync_AtRiskCustomers_CreatesRetentionGoal()
    {
        SetupEmptyState();
        var customers = new List<CustomerState>
        {
            new("c1", "Acme", CustomerHealthStatus.AtRisk, 50000, 3, 1, DateTimeOffset.UtcNow, new Dictionary<string, string>(), DateTimeOffset.UtcNow),
            new("c2", "Globex", CustomerHealthStatus.AtRisk, 75000, 2, 0, DateTimeOffset.UtcNow, new Dictionary<string, string>(), DateTimeOffset.UtcNow)
        };

        _stateEngine.Setup(s => s.GetCustomersByHealthAsync(CustomerHealthStatus.AtRisk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customers);

        var sut = CreateSut();
        var result = await sut.GenerateGoalsAsync();

        result.GeneratedGoals.Should().Contain(g => g.Title == "Retain at-risk customers");
    }

    [Fact]
    public async Task GoalGenerator_GenerateGoalsAsync_HighSeverityRevenueSignal_CreatesRevenueGoal()
    {
        SetupEmptyState();
        var observation = new OperationalObservation(
            Guid.NewGuid(), Guid.NewGuid(), SourceSystem.CRM,
            ObservationCategory.Revenue, ObservationSeverity.High,
            "deal-123", "Large deal detected",
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        _perceptionEngine.Setup(p => p.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerceptionDashboard(
                1, 1,
                new Dictionary<SourceSystem, long>(),
                new Dictionary<ObservationCategory, long>(),
                new[] { observation },
                DateTimeOffset.UtcNow));

        var sut = CreateSut();
        var result = await sut.GenerateGoalsAsync();

        result.GeneratedGoals.Should().Contain(g => g.Title == "Increase revenue from flagged opportunity");
        result.SignalsAnalyzed.Should().Be(1);
    }

    [Fact]
    public async Task GoalGenerator_GenerateGoalsAsync_PerformanceTrendWithHighImpact_CreatesGoal()
    {
        SetupEmptyState();
        var rec = new PerformanceRecommendation(
            "throughput", "api-gateway", "Scale horizontally",
            "Latency increasing under load", 0.8, DateTimeOffset.UtcNow);

        _performanceAnalyzer.Setup(p => p.AnalyzeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerformanceReport(
                Array.Empty<AgentEfficiencyRecord>(),
                Array.Empty<ModelAccuracyRecord>(),
                Array.Empty<TaskCompletionRecord>(),
                new[] { rec },
                0.7,
                DateTimeOffset.UtcNow));

        var sut = CreateSut();
        var result = await sut.GenerateGoalsAsync();

        result.GeneratedGoals.Should().Contain(g => g.Title.StartsWith("Performance:") && g.Priority == GoalPriority.High);
        result.TrendsEvaluated.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GoalGenerator_GenerateGoalsAsync_Cancellation_ThrowsOperationCanceled()
    {
        SetupEmptyState();
        var cts = new CancellationTokenSource();
        _stateEngine.Setup(s => s.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
                return null!;
            });

        var sut = CreateSut();

        var act = () => sut.GenerateGoalsAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── GetGoalAsync ────────────────────────────────────────

    [Fact]
    public async Task GoalGenerator_GetGoalAsync_UnknownId_ReturnsNull()
    {
        SetupEmptyState();
        var sut = CreateSut();

        var result = await sut.GetGoalAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GoalGenerator_GetGoalAsync_AfterGeneration_ReturnsGoal()
    {
        SetupEmptyState();
        var dept = new DepartmentState(
            "eng", DepartmentType.Engineering, "Engineering",
            5, 10, 100, 0.4,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        _stateEngine.Setup(s => s.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalState(
                Guid.NewGuid(), 1,
                new[] { dept },
                Array.Empty<ResourceState>(),
                Array.Empty<CustomerState>(),
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow));

        var sut = CreateSut();
        var genResult = await sut.GenerateGoalsAsync();
        var firstGoal = genResult.GeneratedGoals.First();

        var retrieved = await sut.GetGoalAsync(firstGoal.GoalId);

        retrieved.Should().NotBeNull();
        retrieved!.GoalId.Should().Be(firstGoal.GoalId);
    }

    [Fact]
    public async Task GoalGenerator_GetGoalAsync_Cancellation_ThrowsOperationCanceled()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.GetGoalAsync(Guid.NewGuid(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── GetGoalsByStatusAsync ───────────────────────────────

    [Fact]
    public async Task GoalGenerator_GetGoalsByStatusAsync_NoGoals_ReturnsEmptyList()
    {
        var sut = CreateSut();

        var result = await sut.GetGoalsByStatusAsync(GoalStatus.Proposed);

        result.Should().BeEmpty();
    }

    // ── ApproveGoalAsync ────────────────────────────────────

    [Fact]
    public async Task GoalGenerator_ApproveGoalAsync_ExistingGoal_ChangesStatusToApproved()
    {
        SetupEmptyState();
        var dept = new DepartmentState(
            "eng", DepartmentType.Engineering, "Engineering",
            5, 10, 100, 0.4,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        _stateEngine.Setup(s => s.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalState(
                Guid.NewGuid(), 1, new[] { dept },
                Array.Empty<ResourceState>(),
                Array.Empty<CustomerState>(),
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow));

        _planner.Setup(p => p.CreatePlanAsync(It.IsAny<Objective>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CoreTask>());

        var sut = CreateSut();
        var genResult = await sut.GenerateGoalsAsync();
        var goal = genResult.GeneratedGoals.First();

        await sut.ApproveGoalAsync(goal.GoalId);

        // After approval and successful plan creation, status goes to InProgress
        var retrieved = await sut.GetGoalAsync(goal.GoalId);
        retrieved!.Status.Should().Be(GoalStatus.InProgress);
    }

    [Fact]
    public async Task GoalGenerator_ApproveGoalAsync_UnknownGoal_DoesNotThrow()
    {
        var sut = CreateSut();

        var act = () => sut.ApproveGoalAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }

    // ── CancelGoalAsync ─────────────────────────────────────

    [Fact]
    public async Task GoalGenerator_CancelGoalAsync_ExistingGoal_SetsStatusToCancelledWithReason()
    {
        SetupEmptyState();
        var dept = new DepartmentState(
            "eng", DepartmentType.Engineering, "Engineering",
            5, 10, 100, 0.4,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        _stateEngine.Setup(s => s.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalState(
                Guid.NewGuid(), 1, new[] { dept },
                Array.Empty<ResourceState>(),
                Array.Empty<CustomerState>(),
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow));

        var sut = CreateSut();
        var genResult = await sut.GenerateGoalsAsync();
        var goal = genResult.GeneratedGoals.First();

        await sut.CancelGoalAsync(goal.GoalId, "No longer relevant");

        var retrieved = await sut.GetGoalAsync(goal.GoalId);
        retrieved!.Status.Should().Be(GoalStatus.Cancelled);
        retrieved.Context.Should().ContainKey("cancellationReason")
            .WhoseValue.Should().Be("No longer relevant");
    }

    // ── GetDashboardAsync ───────────────────────────────────

    [Fact]
    public async Task GoalGenerator_GetDashboardAsync_NoGoals_ReturnsDashboardWithZeroCounts()
    {
        var sut = CreateSut();

        var dashboard = await sut.GetDashboardAsync();

        dashboard.TotalGoals.Should().Be(0);
        dashboard.ProposedGoals.Should().Be(0);
        dashboard.InProgressGoals.Should().Be(0);
        dashboard.CompletedGoals.Should().Be(0);
        dashboard.TotalGenerationRuns.Should().Be(0);
    }

    [Fact]
    public async Task GoalGenerator_GetDashboardAsync_AfterGeneration_ReflectsGoalsAndRunCount()
    {
        SetupEmptyState();
        var dept = new DepartmentState(
            "eng", DepartmentType.Engineering, "Engineering",
            5, 10, 100, 0.4,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        _stateEngine.Setup(s => s.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalState(
                Guid.NewGuid(), 1, new[] { dept },
                Array.Empty<ResourceState>(),
                Array.Empty<CustomerState>(),
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow));

        var sut = CreateSut();
        await sut.GenerateGoalsAsync();

        var dashboard = await sut.GetDashboardAsync();

        dashboard.TotalGoals.Should().BeGreaterThan(0);
        dashboard.TotalGenerationRuns.Should().Be(1);
        dashboard.ProposedGoals.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GoalGenerator_GetDashboardAsync_Cancellation_ThrowsOperationCanceled()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.GetDashboardAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

// ══════════════════════════════════════════════════════════════
//  StrategicPlanningEngine Tests
// ══════════════════════════════════════════════════════════════

public class StrategicPlanningEngineTests
{
    private readonly Mock<IScenarioEngine> _scenarioEngine = new();
    private readonly Mock<IEconomicEvaluator> _economicEvaluator = new();
    private readonly Mock<IStrategySimulator> _strategySimulator = new();
    private readonly Mock<ITaskGraphBuilder> _taskGraphBuilder = new();

    private StrategicPlanningEngine CreateSut() => new(
        _scenarioEngine.Object,
        _economicEvaluator.Object,
        _strategySimulator.Object,
        _taskGraphBuilder.Object);

    private static Objective CreateObjective(
        Dictionary<string, string>? constraints = null,
        DateTimeOffset? dueAt = null) => new(
        Guid.NewGuid(),
        "Test Objective",
        "A test objective description",
        constraints ?? new Dictionary<string, string>(),
        DateTimeOffset.UtcNow,
        dueAt);

    private static OperationalGoal CreateGoal(
        GoalSource source = GoalSource.StateAnomaly,
        string department = "operations") => new(
        Guid.NewGuid(),
        "Test Goal",
        "Test goal description",
        GoalPriority.High,
        source,
        GoalStatus.Approved,
        "Improve performance",
        department,
        DateTimeOffset.UtcNow.AddHours(48),
        new Dictionary<string, string>(),
        DateTimeOffset.UtcNow);

    // ── BuildWorkflowAsync ──────────────────────────────────

    [Fact]
    public async Task StrategicPlanningEngine_BuildWorkflowAsync_NoConstraints_UsesBalancedStrategy()
    {
        var sut = CreateSut();
        var objective = CreateObjective();

        var result = await sut.BuildWorkflowAsync(objective);

        result.Strategy.Should().Be("balanced");
        result.ObjectiveId.Should().Be(objective.Id);
        result.Steps.Should().HaveCount(4);
    }

    [Fact]
    public async Task StrategicPlanningEngine_BuildWorkflowAsync_HighRisk_UsesSafeModeStrategy()
    {
        var sut = CreateSut();
        var objective = CreateObjective(new Dictionary<string, string> { ["risk"] = "high" });

        var result = await sut.BuildWorkflowAsync(objective);

        result.Strategy.Should().Be("safe-mode");
        // In safe-mode the execution agent should be workflow-orchestration
        result.Steps.Should().Contain(s => s.Name == "Execution" && s.AgentType == "workflow-orchestration");
    }

    [Fact]
    public async Task StrategicPlanningEngine_BuildWorkflowAsync_TightDeadline_UsesThroughputOptimized()
    {
        var sut = CreateSut();
        var objective = CreateObjective(dueAt: DateTimeOffset.UtcNow.AddHours(12));

        var result = await sut.BuildWorkflowAsync(objective);

        result.Strategy.Should().Be("throughput-optimized");
    }

    [Fact]
    public async Task StrategicPlanningEngine_BuildWorkflowAsync_StrictCost_UsesCostOptimized()
    {
        var sut = CreateSut();
        var objective = CreateObjective(new Dictionary<string, string> { ["cost"] = "strict" });

        var result = await sut.BuildWorkflowAsync(objective);

        result.Strategy.Should().Be("cost-optimized");
    }

    [Fact]
    public async Task StrategicPlanningEngine_BuildWorkflowAsync_FinancialDomain_UsesFinancialOpsAgent()
    {
        var sut = CreateSut();
        var objective = CreateObjective(new Dictionary<string, string> { ["domain"] = "financial" });

        var result = await sut.BuildWorkflowAsync(objective);

        result.Steps.Should().Contain(s => s.Name == "Execution" && s.AgentType == "financial-operations");
    }

    [Fact]
    public async Task StrategicPlanningEngine_BuildWorkflowAsync_Cancellation_ThrowsOperationCanceled()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.BuildWorkflowAsync(CreateObjective(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StrategicPlanningEngine_BuildWorkflowAsync_StepsAreOrderedCorrectly()
    {
        var sut = CreateSut();
        var result = await sut.BuildWorkflowAsync(CreateObjective());

        var orders = result.Steps.Select(s => s.Order).ToList();
        orders.Should().BeInAscendingOrder();
        orders.Should().Equal(10, 20, 30, 40);
    }

    // ── BuildAndValidateWorkflowAsync ───────────────────────

    [Fact]
    public async Task StrategicPlanningEngine_BuildAndValidateWorkflowAsync_WithCandidates_DelegatesToEconomicAndScenario()
    {
        var sut = CreateSut();
        var objective = CreateObjective();
        var candidates = new List<string> { "balanced", "safe-mode" };

        var bestEval = new StrategyEvaluation(
            "balanced", 10m, 0.8, 0.9, 2.0, 0.85,
            "Best overall", new Dictionary<string, double>(), DateTimeOffset.UtcNow);

        var econResult = new EconomicEvaluationResult(
            new[] { bestEval },
            bestEval,
            "Balanced is optimal",
            EconomicWeights.Default,
            DateTimeOffset.UtcNow);

        _economicEvaluator.Setup(e => e.EvaluateStrategiesAsync(
                It.IsAny<Objective>(), It.IsAny<WorkflowDefinition>(),
                It.IsAny<IReadOnlyList<string>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(econResult);

        var validatedPlan = new SimulationValidatedPlan(
            new WorkflowDefinition(objective.Id, "balanced", "Summary", Array.Empty<WorkflowStepDefinition>(), DateTimeOffset.UtcNow),
            "balanced",
            new ScenarioResult(
                Guid.NewGuid(), "test", "balanced", true, 0.9, 0.1,
                Array.Empty<ScenarioStepResult>(),
                new RiskAssessment(0.1, "Low", Array.Empty<RiskFactor>(), "Ok"),
                5m, 100, DateTimeOffset.UtcNow),
            true, "None", DateTimeOffset.UtcNow);

        _scenarioEngine.Setup(s => s.ValidatePlanAsync(
                It.IsAny<Objective>(), It.IsAny<WorkflowDefinition>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validatedPlan);

        var result = await sut.BuildAndValidateWorkflowAsync(objective, candidates);

        result.Should().NotBeNull();
        result.ValidatedStrategy.Should().Be("balanced");
        _economicEvaluator.Verify(e => e.EvaluateStrategiesAsync(
            It.IsAny<Objective>(), It.IsAny<WorkflowDefinition>(),
            It.IsAny<IReadOnlyList<string>>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _scenarioEngine.Verify(s => s.ValidatePlanAsync(
            It.IsAny<Objective>(), It.IsAny<WorkflowDefinition>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StrategicPlanningEngine_BuildAndValidateWorkflowAsync_NoCandidates_UsesDefaultStrategies()
    {
        var sut = CreateSut();
        var objective = CreateObjective();

        var bestEval = new StrategyEvaluation(
            "balanced", 10m, 0.8, 0.9, 2.0, 0.85,
            "Best", new Dictionary<string, double>(), DateTimeOffset.UtcNow);

        _economicEvaluator.Setup(e => e.EvaluateStrategiesAsync(
                It.IsAny<Objective>(), It.IsAny<WorkflowDefinition>(),
                It.IsAny<IReadOnlyList<string>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EconomicEvaluationResult(
                new[] { bestEval }, bestEval, "ok", EconomicWeights.Default, DateTimeOffset.UtcNow));

        _scenarioEngine.Setup(s => s.ValidatePlanAsync(
                It.IsAny<Objective>(), It.IsAny<WorkflowDefinition>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SimulationValidatedPlan(
                new WorkflowDefinition(objective.Id, "balanced", "Summary", Array.Empty<WorkflowStepDefinition>(), DateTimeOffset.UtcNow),
                "balanced",
                new ScenarioResult(Guid.NewGuid(), "test", "balanced", true, 0.9, 0.1,
                    Array.Empty<ScenarioStepResult>(),
                    new RiskAssessment(0.1, "Low", Array.Empty<RiskFactor>(), "Ok"),
                    5m, 100, DateTimeOffset.UtcNow),
                true, "None", DateTimeOffset.UtcNow));

        var result = await sut.BuildAndValidateWorkflowAsync(objective, null);

        // When no candidates provided, should use 5 default strategies
        _economicEvaluator.Verify(e => e.EvaluateStrategiesAsync(
            It.IsAny<Objective>(), It.IsAny<WorkflowDefinition>(),
            It.Is<IReadOnlyList<string>>(l => l.Count == 5), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StrategicPlanningEngine_BuildAndValidateWorkflowAsync_Cancellation_ThrowsOperationCanceled()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.BuildAndValidateWorkflowAsync(CreateObjective(), null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── BuildSimulationGuidedPlanAsync ──────────────────────

    [Fact]
    public async Task StrategicPlanningEngine_BuildSimulationGuidedPlanAsync_ReturnsSimulationGuidedPlan()
    {
        var sut = CreateSut();
        var goal = CreateGoal();

        var simResult = new StrategySimulationResult(
            Guid.NewGuid(), Guid.NewGuid(), "balanced",
            new ExpectedOutcome(0.92, true, "Success", 0.88, 5, 3, 2),
            0.15, "Low",
            Array.Empty<SimulatedNodeResult>(),
            Array.Empty<string>(), Array.Empty<string>(),
            4.5, 12.50m, DateTimeOffset.UtcNow);

        var comparison = new TaskGraphStrategyComparison(
            goal.GoalId,
            new[] { simResult },
            simResult,
            "Balanced provides best cost/performance tradeoff",
            DateTimeOffset.UtcNow);

        _strategySimulator.Setup(s => s.SimulateGoalStrategiesAsync(
                It.IsAny<OperationalGoal>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(comparison);

        var taskGraph = new TaskGraph(
            Guid.NewGuid(), goal.GoalId, goal.Title,
            Array.Empty<TaskGraphNode>(), Array.Empty<TaskGraphEdge>(),
            "balanced", DateTimeOffset.UtcNow);

        _taskGraphBuilder.Setup(t => t.BuildGraphAsync(
                It.IsAny<OperationalGoal>(), "balanced", It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        var result = await sut.BuildSimulationGuidedPlanAsync(goal);

        result.Should().NotBeNull();
        result.SelectedStrategy.Should().Be("balanced");
        result.ExpectedSuccessProbability.Should().Be(0.92);
        result.RiskScore.Should().Be(0.15);
        result.Goal.Should().Be(goal);
        result.SelectedTaskGraph.Should().Be(taskGraph);
    }

    [Fact]
    public async Task StrategicPlanningEngine_BuildSimulationGuidedPlanAsync_Cancellation_ThrowsOperationCanceled()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.BuildSimulationGuidedPlanAsync(CreateGoal(), null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

// ══════════════════════════════════════════════════════════════
//  TaskGraphBuilder Tests
// ══════════════════════════════════════════════════════════════

public class TaskGraphBuilderTests
{
    private readonly Mock<IRuntime> _runtime = new();
    private readonly Mock<IWorkflowExecutionEngine> _workflowEngine = new();
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<ILogger<TaskGraphBuilder>> _logger = new();

    private TaskGraphBuilder CreateSut() => new(
        _runtime.Object,
        _workflowEngine.Object,
        _eventBus.Object,
        _logger.Object);

    private static OperationalGoal CreateGoal(
        GoalSource source = GoalSource.StateAnomaly,
        string department = "operations",
        GoalPriority priority = GoalPriority.High,
        Dictionary<string, string>? context = null) => new(
        Guid.NewGuid(),
        "Test Goal",
        "Test goal description",
        priority,
        source,
        GoalStatus.Approved,
        "Improve performance",
        department,
        DateTimeOffset.UtcNow.AddHours(48),
        context ?? new Dictionary<string, string>(),
        DateTimeOffset.UtcNow);

    // ── BuildGraphAsync ─────────────────────────────────────

    [Fact]
    public async Task TaskGraphBuilder_BuildGraphAsync_StateAnomaly_ContainsAnalysisDiagnosisRemediationVerificationEvaluation()
    {
        var sut = CreateSut();
        var goal = CreateGoal(GoalSource.StateAnomaly);

        var graph = await sut.BuildGraphAsync(goal, "balanced");

        graph.GoalId.Should().Be(goal.GoalId);
        graph.Strategy.Should().Be("balanced");
        graph.Nodes.Should().Contain(n => n.Name == "Goal Analysis");
        graph.Nodes.Should().Contain(n => n.Name == "Anomaly Diagnosis");
        graph.Nodes.Should().Contain(n => n.Name == "Execute Remediation");
        graph.Nodes.Should().Contain(n => n.Name == "Verify Resolution");
        graph.Nodes.Should().Contain(n => n.Name == "Outcome Evaluation");
        graph.Edges.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TaskGraphBuilder_BuildGraphAsync_BusinessSignal_ContainsDataGatheringAndStrategyFormation()
    {
        var sut = CreateSut();
        var goal = CreateGoal(GoalSource.BusinessSignal, "sales");

        var graph = await sut.BuildGraphAsync(goal, "balanced");

        graph.Nodes.Should().Contain(n => n.Name == "Signal Data Gathering");
        graph.Nodes.Should().Contain(n => n.Name == "Strategy Formation");
        graph.Nodes.Should().Contain(n => n.Name == "Operational Execution");
        graph.Nodes.Should().Contain(n => n.Name == "Stakeholder Communication");
        graph.Nodes.Should().Contain(n => n.Name == "Impact Measurement");
    }

    [Fact]
    public async Task TaskGraphBuilder_BuildGraphAsync_PerformanceTrend_ContainsTrendAnalysisAndOptimization()
    {
        var sut = CreateSut();
        var goal = CreateGoal(GoalSource.PerformanceTrend);

        var graph = await sut.BuildGraphAsync(goal, "balanced");

        graph.Nodes.Should().Contain(n => n.Name == "Deep Trend Analysis");
        graph.Nodes.Should().Contain(n => n.Name == "Optimization Planning");
        graph.Nodes.Should().Contain(n => n.Name == "Apply Optimization");
        graph.Nodes.Should().Contain(n => n.Name == "Performance Validation");
    }

    [Fact]
    public async Task TaskGraphBuilder_BuildGraphAsync_DefaultSource_ContainsOperationalPlanningAndExecution()
    {
        var sut = CreateSut();
        var goal = CreateGoal(GoalSource.Manual);

        var graph = await sut.BuildGraphAsync(goal, "balanced");

        graph.Nodes.Should().Contain(n => n.Name == "Operational Planning");
        graph.Nodes.Should().Contain(n => n.Name == "Execution");
    }

    [Fact]
    public async Task TaskGraphBuilder_BuildGraphAsync_SafeMode_UsesWorkflowOrchestrationAgent()
    {
        var sut = CreateSut();
        var goal = CreateGoal(GoalSource.StateAnomaly, "finance");

        var graph = await sut.BuildGraphAsync(goal, "safe-mode");

        // In safe-mode, SelectAgentForDepartment returns "workflow-orchestration"
        graph.Nodes.Where(n => n.Name == "Execute Remediation")
            .Should().OnlyContain(n => n.AgentType == "workflow-orchestration");
    }

    [Fact]
    public async Task TaskGraphBuilder_BuildGraphAsync_FinanceDepartment_UsesFinancialOperationsAgent()
    {
        var sut = CreateSut();
        var goal = CreateGoal(GoalSource.StateAnomaly, "finance");

        var graph = await sut.BuildGraphAsync(goal, "balanced");

        graph.Nodes.Where(n => n.Name == "Execute Remediation")
            .Should().OnlyContain(n => n.AgentType == "financial-operations");
    }

    [Fact]
    public async Task TaskGraphBuilder_BuildGraphAsync_StoresGraphForRetrieval()
    {
        var sut = CreateSut();
        var goal = CreateGoal();

        var graph = await sut.BuildGraphAsync(goal, "balanced");

        var retrieved = await sut.GetGraphAsync(graph.GraphId);
        retrieved.Should().NotBeNull();
        retrieved!.GraphId.Should().Be(graph.GraphId);
    }

    [Fact]
    public async Task TaskGraphBuilder_BuildGraphAsync_Cancellation_ThrowsOperationCanceled()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.BuildGraphAsync(CreateGoal(), "balanced", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TaskGraphBuilder_BuildGraphAsync_EvaluationNodeConnectedToLeaves()
    {
        var sut = CreateSut();
        var goal = CreateGoal(GoalSource.StateAnomaly);

        var graph = await sut.BuildGraphAsync(goal, "balanced");

        var evalNode = graph.Nodes.Single(n => n.Name == "Outcome Evaluation");
        var incomingEdges = graph.Edges.Where(e => e.TargetNodeId == evalNode.NodeId).ToList();
        incomingEdges.Should().NotBeEmpty("evaluation node should receive edges from leaf nodes");
    }

    // ── DispatchGraphAsync ──────────────────────────────────

    [Fact]
    public async Task TaskGraphBuilder_DispatchGraphAsync_ValidGraph_ReturnsDispatchResult()
    {
        var sut = CreateSut();
        var goal = CreateGoal(GoalSource.Manual);

        var graph = await sut.BuildGraphAsync(goal, "balanced");

        _workflowEngine.Setup(w => w.InitializeWorkflowAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);
        _workflowEngine.Setup(w => w.CreateTaskQueueAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<CoreTask>>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);
        _runtime.Setup(r => r.ScheduleTaskAsync(It.IsAny<CoreTask>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);
        _eventBus.Setup(e => e.PublishAsync(It.IsAny<SystemEvent>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        var result = await sut.DispatchGraphAsync(graph);

        result.GraphId.Should().Be(graph.GraphId);
        result.TotalTasks.Should().Be(graph.Nodes.Count);
        result.ExecutionLayers.Should().BeGreaterThan(0);
        result.ScheduledTaskIds.Should().HaveCount(graph.Nodes.Count);

        _workflowEngine.Verify(w => w.InitializeWorkflowAsync(graph.GraphId, It.IsAny<CancellationToken>()), Times.Once);
        _runtime.Verify(r => r.ScheduleTaskAsync(It.IsAny<CoreTask>(), It.IsAny<CancellationToken>()),
            Times.Exactly(graph.Nodes.Count));
        _eventBus.Verify(e => e.PublishAsync(
            It.Is<SystemEvent>(ev => ev.EventType == "strategic.taskgraph.dispatched"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetGraphAsync ───────────────────────────────────────

    [Fact]
    public async Task TaskGraphBuilder_GetGraphAsync_UnknownId_ReturnsNull()
    {
        var sut = CreateSut();

        var result = await sut.GetGraphAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task TaskGraphBuilder_GetGraphAsync_Cancellation_ThrowsOperationCanceled()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.GetGraphAsync(Guid.NewGuid(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── GetGraphsByGoalAsync ────────────────────────────────

    [Fact]
    public async Task TaskGraphBuilder_GetGraphsByGoalAsync_NoGraphs_ReturnsEmptyList()
    {
        var sut = CreateSut();

        var result = await sut.GetGraphsByGoalAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task TaskGraphBuilder_GetGraphsByGoalAsync_MultipleGraphs_ReturnsMatchingGraphs()
    {
        var sut = CreateSut();
        var goal = CreateGoal();

        var graph1 = await sut.BuildGraphAsync(goal, "balanced");
        var graph2 = await sut.BuildGraphAsync(goal, "safe-mode");
        await sut.BuildGraphAsync(CreateGoal(), "balanced"); // different goal

        var result = await sut.GetGraphsByGoalAsync(goal.GoalId);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(g => g.GoalId == goal.GoalId);
    }

    [Fact]
    public async Task TaskGraphBuilder_GetGraphsByGoalAsync_Cancellation_ThrowsOperationCanceled()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.GetGraphsByGoalAsync(Guid.NewGuid(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
