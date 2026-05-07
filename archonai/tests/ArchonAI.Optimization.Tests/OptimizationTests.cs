using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models.Routing;
using ArchonAI.Core.Models.Optimization;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Trace;
using ArchonAI.Optimization;

namespace ArchonAI.Optimization.Tests;

public class OptimizationTests
{
    // ══════════════════════════════════════════════════════════════
    //  Shared helpers
    // ══════════════════════════════════════════════════════════════

    private static IOptions<OptimizationOptions> DefaultOptions(Action<OptimizationOptions>? configure = null)
    {
        var opts = new OptimizationOptions();
        configure?.Invoke(opts);
        return Options.Create(opts);
    }

    private static WorkflowDefinition CreateWorkflow(
        IReadOnlyList<WorkflowStepDefinition> steps,
        string strategy = "balanced")
    {
        return new WorkflowDefinition(
            ObjectiveId: Guid.NewGuid(),
            Strategy: strategy,
            Summary: "Test workflow",
            Steps: steps,
            CreatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static WorkflowStepDefinition Step(int order, string name, string agentType) =>
        new(order, name, $"Description for {name}", agentType, new Dictionary<string, string>());

    private static Objective CreateObjective(Dictionary<string, string>? constraints = null) =>
        new(Guid.NewGuid(), "Test objective", "Desc", constraints ?? new Dictionary<string, string>(), DateTimeOffset.UtcNow, null);

    private static PerformanceReport EmptyPerformanceReport(double healthScore = 0.85) =>
        new(
            AgentEfficiency: Array.Empty<AgentEfficiencyRecord>(),
            ModelAccuracy: Array.Empty<ModelAccuracyRecord>(),
            TaskCompletion: Array.Empty<TaskCompletionRecord>(),
            Recommendations: Array.Empty<PerformanceRecommendation>(),
            OverallHealthScore: healthScore,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

    private static AgentEfficiencyRecord AgentRecord(
        string name, int completed, int failed, double avgTimeMs = 1000, double efficiencyScore = 0.7) =>
        new(
            AgentId: Guid.NewGuid(),
            AgentName: name,
            TasksCompleted: completed,
            TasksFailed: failed,
            SuccessRate: (completed + failed) > 0 ? (double)completed / (completed + failed) : 0,
            AverageExecutionTimeMs: avgTimeMs,
            AverageCost: 0.01m,
            EfficiencyScore: efficiencyScore,
            LastActiveUtc: DateTimeOffset.UtcNow);

    private static TaskCompletionRecord TaskRecord(string taskType, int completed, int failed, double avgTimeMs = 500) =>
        new(
            TaskType: taskType,
            TotalTasks: completed + failed,
            Completed: completed,
            Failed: failed,
            CompletionRate: (completed + failed) > 0 ? (double)completed / (completed + failed) : 0,
            AverageExecutionTimeMs: avgTimeMs,
            AverageCost: 0.005m);

    private static ModelPerformanceScore ModelScore(
        string provider, string model, double successRate, double accuracy,
        int sampleCount = 20, double avgCost = 0.01, double avgLatency = 500, double composite = 0.7) =>
        new(
            Provider: provider,
            Model: model,
            AverageLatencyMs: avgLatency,
            AverageCostPerRequest: avgCost,
            AccuracyRate: accuracy,
            SuccessRate: successRate,
            SampleCount: sampleCount,
            CompositeScore: composite,
            LastUpdatedUtc: DateTimeOffset.UtcNow);

    // ══════════════════════════════════════════════════════════════
    //  OptimizationEngine tests
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task OptimizationEngine_OptimizeWorkflowAsync_EfficientWorkflow_ReturnsUnchanged()
    {
        // A workflow with fewer steps than InefficientStepThreshold and no duplicates
        // should be returned as-is (AnalyzeEfficiency returns WasInefficient=false).
        var steps = new[]
        {
            Step(1, "StepA", "AgentX"),
            Step(2, "StepB", "AgentY"),
            Step(3, "StepC", "AgentZ")
        };
        var workflow = CreateWorkflow(steps);
        var objective = CreateObjective();

        var feedbackStore = new Mock<IPlanningFeedbackStore>();
        var perfAnalyzer = new Mock<IPerformanceAnalyzer>();

        var engine = new OptimizationEngine(
            DefaultOptions(),
            feedbackStore.Object,
            perfAnalyzer.Object);

        var result = await engine.OptimizeWorkflowAsync(objective, workflow);

        // 3 unique steps < default threshold 6, no duplicates => not inefficient
        result.Should().BeSameAs(workflow);
        feedbackStore.Verify(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OptimizationEngine_OptimizeWorkflowAsync_DuplicateSteps_RemovesDuplicates()
    {
        // Duplicate (Name, AgentType) pairs trigger optimization
        var steps = new[]
        {
            Step(1, "StepA", "AgentX"),
            Step(2, "StepA", "AgentX"), // duplicate
            Step(3, "StepB", "AgentY"),
            Step(4, "StepC", "AgentZ")
        };
        var workflow = CreateWorkflow(steps);
        var objective = CreateObjective();

        var feedbackStore = new Mock<IPlanningFeedbackStore>();
        feedbackStore.Setup(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var perfAnalyzer = new Mock<IPerformanceAnalyzer>();

        var engine = new OptimizationEngine(
            DefaultOptions(),
            feedbackStore.Object,
            perfAnalyzer.Object);

        var result = await engine.OptimizeWorkflowAsync(objective, workflow);

        result.Should().NotBeSameAs(workflow);
        result.Steps.Should().HaveCount(3); // 3 unique steps after dedup
        result.Summary.Should().Contain("duplicate-step-elimination");
        // Steps should be re-ordered starting at 1
        result.Steps[0].Order.Should().Be(1);
        result.Steps[1].Order.Should().Be(2);
        result.Steps[2].Order.Should().Be(3);
        feedbackStore.Verify(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OptimizationEngine_OptimizeWorkflowAsync_SafeModeStrategy_ChangesToBalanced()
    {
        // When strategy is "safe-mode" and workflow is inefficient, it gets changed to "balanced"
        var steps = new[]
        {
            Step(1, "A", "X"),
            Step(2, "A", "X"), // duplicate triggers optimization
            Step(3, "B", "Y")
        };
        var workflow = CreateWorkflow(steps, strategy: "safe-mode");
        var objective = CreateObjective();

        var feedbackStore = new Mock<IPlanningFeedbackStore>();
        feedbackStore.Setup(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var engine = new OptimizationEngine(
            DefaultOptions(),
            feedbackStore.Object,
            new Mock<IPerformanceAnalyzer>().Object);

        var result = await engine.OptimizeWorkflowAsync(objective, workflow);

        result.Strategy.Should().Be("balanced");
    }

    [Fact]
    public async Task OptimizationEngine_OptimizeWorkflowAsync_ExceedsStepThreshold_ReducesSteps()
    {
        // Workflow with steps >= InefficientStepThreshold triggers step-count-reduction
        var steps = Enumerable.Range(1, 7)
            .Select(i => Step(i, $"Step{i}", $"Agent{i}"))
            .ToArray();
        var workflow = CreateWorkflow(steps);
        var objective = CreateObjective();

        var feedbackStore = new Mock<IPlanningFeedbackStore>();
        feedbackStore.Setup(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Threshold is 6 by default, 7 unique steps >= 6 => stepThresholdExceeded
        var engine = new OptimizationEngine(
            DefaultOptions(),
            feedbackStore.Object,
            new Mock<IPerformanceAnalyzer>().Object);

        var result = await engine.OptimizeWorkflowAsync(objective, workflow);

        // All steps are unique and non-empty, so DistinctBy doesn't reduce count.
        // But the condition stepThresholdExceeded is true so optimization runs.
        result.Summary.Should().Contain("step-count-reduction");
    }

    [Fact]
    public async Task OptimizationEngine_GenerateImprovedStrategiesAsync_ReturnsConfiguredCount()
    {
        var options = DefaultOptions(o => o.MaxImprovedStrategies = 4);
        var workflow = CreateWorkflow(new[] { Step(1, "StepA", "AgentX"), Step(2, "StepB", "AgentY") });
        var objective = CreateObjective(new Dictionary<string, string> { ["objectiveType"] = "sales" });

        var engine = new OptimizationEngine(
            options,
            new Mock<IPlanningFeedbackStore>().Object,
            new Mock<IPerformanceAnalyzer>().Object);

        var strategies = await engine.GenerateImprovedStrategiesAsync(objective, workflow);

        strategies.Should().HaveCount(4);
        strategies[0].WorkflowTemplate.Should().Be("balanced"); // index == 1 yields "balanced"
        strategies[1].WorkflowTemplate.Should().Be("optimized-v2");
        strategies[2].WorkflowTemplate.Should().Be("optimized-v3");
        strategies[3].WorkflowTemplate.Should().Be("optimized-v4");
        strategies.Should().AllSatisfy(s =>
        {
            s.ObjectiveType.Should().Be("sales");
            s.SuccessMetrics.Should().ContainKey("source").WhoseValue.Should().Be("optimization-engine");
            s.RecommendedAgents.Should().HaveCount(2); // distinct agent types
        });
    }

    [Fact]
    public async Task OptimizationEngine_DeployOptimizedWorkflowAsync_AutoDeployEnabled_ReturnsTrue()
    {
        var feedbackStore = new Mock<IPlanningFeedbackStore>();
        feedbackStore.Setup(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var engine = new OptimizationEngine(
            DefaultOptions(o => o.AutoDeployOptimizedWorkflows = true),
            feedbackStore.Object,
            new Mock<IPerformanceAnalyzer>().Object);

        var workflow = CreateWorkflow(new[] { Step(1, "A", "X") });
        var result = await engine.DeployOptimizedWorkflowAsync(workflow);

        result.Should().BeTrue();
        feedbackStore.Verify(s => s.AddAsync(
            It.Is<PlanningFeedback>(f => f.Capability == "workflow-deployment" && f.WasSuccessful),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OptimizationEngine_DeployOptimizedWorkflowAsync_AutoDeployDisabled_ReturnsFalse()
    {
        var feedbackStore = new Mock<IPlanningFeedbackStore>();

        var engine = new OptimizationEngine(
            DefaultOptions(o => o.AutoDeployOptimizedWorkflows = false),
            feedbackStore.Object,
            new Mock<IPerformanceAnalyzer>().Object);

        var workflow = CreateWorkflow(new[] { Step(1, "A", "X") });
        var result = await engine.DeployOptimizedWorkflowAsync(workflow);

        result.Should().BeFalse();
        feedbackStore.Verify(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OptimizationEngine_AnalyzePerformanceAsync_DelegatesToPerformanceAnalyzer()
    {
        var expected = EmptyPerformanceReport(0.92);
        var perfAnalyzer = new Mock<IPerformanceAnalyzer>();
        perfAnalyzer.Setup(p => p.AnalyzeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var engine = new OptimizationEngine(
            DefaultOptions(),
            new Mock<IPlanningFeedbackStore>().Object,
            perfAnalyzer.Object);

        var result = await engine.AnalyzePerformanceAsync();

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task OptimizationEngine_RunContinuousImprovementCycleAsync_AutoApplyEnabled_AppliesActions()
    {
        var actions = new List<ImprovementAction>
        {
            new(Guid.NewGuid(), "agent-efficiency", "agent1", "reduce-task-load",
                new Dictionary<string, string>(), false, DateTimeOffset.UtcNow, null)
        };

        var perfAnalyzer = new Mock<IPerformanceAnalyzer>();
        perfAnalyzer.Setup(p => p.GenerateImprovementsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(actions);
        perfAnalyzer.Setup(p => p.ApplyImprovementsAsync(It.IsAny<IReadOnlyList<ImprovementAction>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var engine = new OptimizationEngine(
            DefaultOptions(o => o.AutoApplyImprovements = true),
            new Mock<IPlanningFeedbackStore>().Object,
            perfAnalyzer.Object);

        var result = await engine.RunContinuousImprovementCycleAsync();

        result.Should().HaveCount(1);
        perfAnalyzer.Verify(p => p.ApplyImprovementsAsync(actions, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OptimizationEngine_RunContinuousImprovementCycleAsync_AutoApplyDisabled_DoesNotApply()
    {
        var actions = new List<ImprovementAction>
        {
            new(Guid.NewGuid(), "agent-efficiency", "agent1", "reduce-task-load",
                new Dictionary<string, string>(), false, DateTimeOffset.UtcNow, null)
        };

        var perfAnalyzer = new Mock<IPerformanceAnalyzer>();
        perfAnalyzer.Setup(p => p.GenerateImprovementsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(actions);

        var engine = new OptimizationEngine(
            DefaultOptions(o => o.AutoApplyImprovements = false),
            new Mock<IPlanningFeedbackStore>().Object,
            perfAnalyzer.Object);

        var result = await engine.RunContinuousImprovementCycleAsync();

        result.Should().HaveCount(1);
        perfAnalyzer.Verify(p => p.ApplyImprovementsAsync(It.IsAny<IReadOnlyList<ImprovementAction>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ══════════════════════════════════════════════════════════════
    //  PerformanceAnalyzer tests
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PerformanceAnalyzer_AnalyzeAsync_NoData_ReturnsHealthScoreOne()
    {
        var modelTracker = new Mock<IModelPerformanceTracker>();
        modelTracker.Setup(m => m.GetAllScores()).Returns(Array.Empty<ModelPerformanceScore>());

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(t => t.RecordAsync(It.IsAny<TraceEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var analyzer = new PerformanceAnalyzer(
            modelTracker.Object,
            new Mock<IPlanningFeedbackStore>().Object,
            traceStore.Object,
            DefaultOptions());

        var report = await analyzer.AnalyzeAsync();

        // No agents/models/tasks => all default to 1.0 => health = 1.0
        report.OverallHealthScore.Should().Be(1.0);
        report.AgentEfficiency.Should().BeEmpty();
        report.ModelAccuracy.Should().BeEmpty();
        report.TaskCompletion.Should().BeEmpty();
        report.Recommendations.Should().BeEmpty();
    }

    [Fact]
    public async Task PerformanceAnalyzer_RecordAgentExecution_TracksMetricsCorrectly()
    {
        var modelTracker = new Mock<IModelPerformanceTracker>();
        modelTracker.Setup(m => m.GetAllScores()).Returns(Array.Empty<ModelPerformanceScore>());

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(t => t.RecordAsync(It.IsAny<TraceEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var analyzer = new PerformanceAnalyzer(
            modelTracker.Object,
            new Mock<IPlanningFeedbackStore>().Object,
            traceStore.Object,
            DefaultOptions());

        var agentId = Guid.NewGuid();
        analyzer.RecordAgentExecution(agentId, "TestAgent", true, 1000, 0.05m);
        analyzer.RecordAgentExecution(agentId, "TestAgent", true, 2000, 0.10m);
        analyzer.RecordAgentExecution(agentId, "TestAgent", false, 3000, 0.15m);

        var report = await analyzer.AnalyzeAsync();

        report.AgentEfficiency.Should().HaveCount(1);
        var agent = report.AgentEfficiency[0];
        agent.AgentName.Should().Be("TestAgent");
        agent.TasksCompleted.Should().Be(2);
        agent.TasksFailed.Should().Be(1);
        // SuccessRate = 2/3
        agent.SuccessRate.Should().BeApproximately(2.0 / 3.0, 0.001);
        // AvgTime = (1000+2000+3000)/3 = 2000
        agent.AverageExecutionTimeMs.Should().BeApproximately(2000, 0.1);
        // AvgCost = (0.05+0.10+0.15)/3 = 0.10
        agent.AverageCost.Should().Be(0.10m);
    }

    [Fact]
    public async Task PerformanceAnalyzer_RecordTaskCompletion_TracksMetricsCorrectly()
    {
        var modelTracker = new Mock<IModelPerformanceTracker>();
        modelTracker.Setup(m => m.GetAllScores()).Returns(Array.Empty<ModelPerformanceScore>());

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(t => t.RecordAsync(It.IsAny<TraceEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var analyzer = new PerformanceAnalyzer(
            modelTracker.Object,
            new Mock<IPlanningFeedbackStore>().Object,
            traceStore.Object,
            DefaultOptions());

        analyzer.RecordTaskCompletion("email-send", true, 200, 0.01m);
        analyzer.RecordTaskCompletion("email-send", true, 300, 0.02m);
        analyzer.RecordTaskCompletion("email-send", false, 400, 0.03m);

        var report = await analyzer.AnalyzeAsync();

        report.TaskCompletion.Should().HaveCount(1);
        var task = report.TaskCompletion[0];
        task.TaskType.Should().Be("email-send");
        task.Completed.Should().Be(2);
        task.Failed.Should().Be(1);
        task.TotalTasks.Should().Be(3);
        task.CompletionRate.Should().BeApproximately(2.0 / 3.0, 0.001);
        task.AverageExecutionTimeMs.Should().BeApproximately(300, 0.1);
        task.AverageCost.Should().Be(0.02m);
    }

    [Fact]
    public async Task PerformanceAnalyzer_GenerateImprovementsAsync_UnderperformingAgent_CreatesAction()
    {
        var modelTracker = new Mock<IModelPerformanceTracker>();
        modelTracker.Setup(m => m.GetAllScores()).Returns(Array.Empty<ModelPerformanceScore>());

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(t => t.RecordAsync(It.IsAny<TraceEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var analyzer = new PerformanceAnalyzer(
            modelTracker.Object,
            new Mock<IPlanningFeedbackStore>().Object,
            traceStore.Object,
            DefaultOptions(o => o.MinSamplesForAnalysis = 5));

        var agentId = Guid.NewGuid();
        // Record 2 successes and 4 failures = 6 total >= minSamples=5, successRate=0.333 < threshold 0.6
        for (int i = 0; i < 2; i++)
            analyzer.RecordAgentExecution(agentId, "BadAgent", true, 500, 0.01m);
        for (int i = 0; i < 4; i++)
            analyzer.RecordAgentExecution(agentId, "BadAgent", false, 500, 0.01m);

        var actions = await analyzer.GenerateImprovementsAsync();

        actions.Should().Contain(a => a.Category == "agent-efficiency" && a.Action == "reduce-task-load");
    }

    [Fact]
    public async Task PerformanceAnalyzer_ApplyImprovementsAsync_RecordsFeedbackAndTrace()
    {
        var feedbackStore = new Mock<IPlanningFeedbackStore>();
        feedbackStore.Setup(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(t => t.RecordAsync(It.IsAny<TraceEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var analyzer = new PerformanceAnalyzer(
            new Mock<IModelPerformanceTracker>().Object,
            feedbackStore.Object,
            traceStore.Object,
            DefaultOptions());

        var actions = new List<ImprovementAction>
        {
            new(Guid.NewGuid(), "agent-efficiency", "agent-1", "reduce-task-load",
                new Dictionary<string, string> { ["agentName"] = "Slow" }, false, DateTimeOffset.UtcNow, null),
            new(Guid.NewGuid(), "model-accuracy", "openai::gpt-4", "deprioritize-model",
                new Dictionary<string, string>(), false, DateTimeOffset.UtcNow, null)
        };

        await analyzer.ApplyImprovementsAsync(actions);

        // Only non-Applied actions are processed; both have Applied=false
        feedbackStore.Verify(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        traceStore.Verify(t => t.RecordAsync(
            It.Is<TraceEntry>(e => e.Category == "improvement-applied"),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task PerformanceAnalyzer_ApplyImprovementsAsync_SkipsAlreadyAppliedActions()
    {
        var feedbackStore = new Mock<IPlanningFeedbackStore>();
        feedbackStore.Setup(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(t => t.RecordAsync(It.IsAny<TraceEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var analyzer = new PerformanceAnalyzer(
            new Mock<IModelPerformanceTracker>().Object,
            feedbackStore.Object,
            traceStore.Object,
            DefaultOptions());

        var actions = new List<ImprovementAction>
        {
            new(Guid.NewGuid(), "agent-efficiency", "agent-1", "reduce-task-load",
                new Dictionary<string, string>(), true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) // Applied=true
        };

        await analyzer.ApplyImprovementsAsync(actions);

        // Applied=true => skipped
        feedbackStore.Verify(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PerformanceAnalyzer_ComputeOverallHealth_WeightedCorrectly()
    {
        // Health = (taskHealth * 0.40) + (agentHealth * 0.35) + (modelHealth * 0.25)
        // With one agent (efficiency=0.5), one model (composite=0.8), one task (completionRate=0.6):
        // health = (0.6 * 0.40) + (0.5 * 0.35) + (0.8 * 0.25) = 0.24 + 0.175 + 0.20 = 0.615
        var modelTracker = new Mock<IModelPerformanceTracker>();
        modelTracker.Setup(m => m.GetAllScores()).Returns(new[]
        {
            ModelScore("openai", "gpt-4", 0.9, 0.85, composite: 0.8)
        });

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(t => t.RecordAsync(It.IsAny<TraceEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var analyzer = new PerformanceAnalyzer(
            modelTracker.Object,
            new Mock<IPlanningFeedbackStore>().Object,
            traceStore.Object,
            DefaultOptions(o => o.MinSamplesForAnalysis = 1));

        var agentId = Guid.NewGuid();
        // To get efficiency ~0.5, we need successRate*0.7 + speedScore*0.3 ≈ 0.5
        // Record 1 success, 1 fail => successRate=0.5. avgTime=1000ms => speedScore=1-(1000/30000)≈0.967
        // efficiency = 0.5*0.7 + 0.967*0.3 = 0.35 + 0.29 = 0.64
        analyzer.RecordAgentExecution(agentId, "Agent1", true, 1000, 0.01m);
        analyzer.RecordAgentExecution(agentId, "Agent1", false, 1000, 0.01m);

        // Task: 3 success, 2 fail => completionRate = 0.6
        for (int i = 0; i < 3; i++)
            analyzer.RecordTaskCompletion("taskA", true, 500, 0.01m);
        for (int i = 0; i < 2; i++)
            analyzer.RecordTaskCompletion("taskA", false, 500, 0.01m);

        var report = await analyzer.AnalyzeAsync();

        // agentEfficiency ≈ 0.64, modelComposite = 0.8, taskCompletion = 0.6
        // health = 0.6*0.40 + 0.64*0.35 + 0.8*0.25 = 0.24 + 0.224 + 0.20 = 0.664
        report.OverallHealthScore.Should().BeGreaterThanOrEqualTo(0.0);
        report.OverallHealthScore.Should().BeLessThanOrEqualTo(1.0);
        // With these values, health should be roughly in the 0.6-0.7 range
        report.OverallHealthScore.Should().BeApproximately(0.664, 0.01);
    }

    // ══════════════════════════════════════════════════════════════
    //  ContinuousImprovementEngine tests
    // ══════════════════════════════════════════════════════════════

    private ContinuousImprovementEngine CreateCIEngine(
        Mock<IPerformanceAnalyzer>? perfAnalyzer = null,
        Mock<IModelPerformanceTracker>? modelTracker = null,
        Mock<IPlanningFeedbackStore>? feedbackStore = null,
        Mock<IEventBus>? eventBus = null,
        Mock<ITraceStore>? traceStore = null,
        Action<OptimizationOptions>? configure = null)
    {
        perfAnalyzer ??= new Mock<IPerformanceAnalyzer>();
        modelTracker ??= new Mock<IModelPerformanceTracker>();
        feedbackStore ??= new Mock<IPlanningFeedbackStore>();
        eventBus ??= new Mock<IEventBus>();
        traceStore ??= new Mock<ITraceStore>();

        modelTracker.Setup(m => m.GetAllScores()).Returns(Array.Empty<ModelPerformanceScore>());
        feedbackStore.Setup(s => s.AddAsync(It.IsAny<PlanningFeedback>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        eventBus.Setup(e => e.PublishAsync(It.IsAny<SystemEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        traceStore.Setup(t => t.RecordAsync(It.IsAny<TraceEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new ContinuousImprovementEngine(
            perfAnalyzer.Object,
            modelTracker.Object,
            feedbackStore.Object,
            eventBus.Object,
            traceStore.Object,
            DefaultOptions(configure),
            new Mock<ILogger<ContinuousImprovementEngine>>().Object);
    }

    [Fact]
    public async Task ContinuousImprovementEngine_RunCycleAsync_IncrementsCycleNumber()
    {
        var perfAnalyzer = new Mock<IPerformanceAnalyzer>();
        perfAnalyzer.Setup(p => p.AnalyzeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyPerformanceReport(0.9));

        var engine = CreateCIEngine(perfAnalyzer: perfAnalyzer);

        var report1 = await engine.RunCycleAsync();
        var report2 = await engine.RunCycleAsync();

        report1.CycleNumber.Should().Be(1);
        report2.CycleNumber.Should().Be(2);
    }

    [Fact]
    public async Task ContinuousImprovementEngine_RunCycleAsync_TracksHealthTrend()
    {
        var perfAnalyzer = new Mock<IPerformanceAnalyzer>();
        perfAnalyzer.Setup(p => p.AnalyzeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyPerformanceReport(0.8));

        var engine = CreateCIEngine(perfAnalyzer: perfAnalyzer);

        // First cycle: previousHealthScore starts at 1.0
        var report1 = await engine.RunCycleAsync();
        report1.PreviousHealthScore.Should().Be(1.0);
        report1.OverallHealthScore.Should().Be(0.8);
        // HealthTrend = 0.8 - 1.0 = -0.2
        report1.HealthTrend.Should().BeApproximately(-0.2, 0.0001);

        // Second cycle: previousHealthScore is now 0.8
        var report2 = await engine.RunCycleAsync();
        report2.PreviousHealthScore.Should().Be(0.8);
        // HealthTrend = 0.8 - 0.8 = 0.0
        report2.HealthTrend.Should().BeApproximately(0.0, 0.0001);
    }

    [Fact]
    public async Task ContinuousImprovementEngine_GetCycleHistory_ReturnsPastReports()
    {
        var perfAnalyzer = new Mock<IPerformanceAnalyzer>();
        perfAnalyzer.Setup(p => p.AnalyzeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyPerformanceReport(0.9));

        var engine = CreateCIEngine(perfAnalyzer: perfAnalyzer);

        engine.GetCycleHistory().Should().BeEmpty();

        await engine.RunCycleAsync();
        await engine.RunCycleAsync();

        var history = engine.GetCycleHistory();
        history.Should().HaveCount(2);
        history[0].CycleNumber.Should().Be(1);
        history[1].CycleNumber.Should().Be(2);
    }

    [Fact]
    public async Task ContinuousImprovementEngine_DetectInefficienciesAsync_AgentBelowThreshold_DetectsBottleneck()
    {
        var lowSuccessAgent = AgentRecord("SlowAgent", completed: 3, failed: 12, avgTimeMs: 2000, efficiencyScore: 0.2);
        var report = new PerformanceReport(
            AgentEfficiency: new[] { lowSuccessAgent },
            ModelAccuracy: Array.Empty<ModelAccuracyRecord>(),
            TaskCompletion: Array.Empty<TaskCompletionRecord>(),
            Recommendations: Array.Empty<PerformanceRecommendation>(),
            OverallHealthScore: 0.3,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        var perfAnalyzer = new Mock<IPerformanceAnalyzer>();
        perfAnalyzer.Setup(p => p.AnalyzeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var modelTracker = new Mock<IModelPerformanceTracker>();
        modelTracker.Setup(m => m.GetAllScores()).Returns(Array.Empty<ModelPerformanceScore>());

        var engine = CreateCIEngine(
            perfAnalyzer: perfAnalyzer,
            modelTracker: modelTracker,
            configure: o => o.MinSamplesForAnalysis = 10);

        var inefficiencies = await engine.DetectInefficienciesAsync();

        // 3+12=15 >= minSamples=10, successRate=3/15=0.2 < threshold 0.6 => AgentBottleneck
        inefficiencies.Should().Contain(i => i.Type == InefficiencyType.AgentBottleneck);
        var bottleneck = inefficiencies.First(i => i.Type == InefficiencyType.AgentBottleneck);
        bottleneck.Target.Should().Be("SlowAgent");
        // successRate 0.2 < 0.3 => Critical severity
        bottleneck.Severity.Should().Be(InefficiencySeverity.Critical);
    }

    [Fact]
    public async Task ContinuousImprovementEngine_RecommendImprovementsAsync_AgentBottleneck_GeneratesRecommendation()
    {
        var engine = CreateCIEngine();

        var inefficiencies = new List<DetectedInefficiency>
        {
            new(
                InefficiencyId: Guid.NewGuid(),
                Type: InefficiencyType.AgentBottleneck,
                Severity: InefficiencySeverity.Critical,
                Target: "BadAgent",
                Description: "Agent has 20% success rate",
                CurrentValue: 0.2, // < 0.3 => severe
                ThresholdValue: 0.6,
                DeviationPercent: 66.67,
                Evidence: new Dictionary<string, string>
                {
                    ["agentId"] = Guid.NewGuid().ToString(),
                    ["completed"] = "2",
                    ["failed"] = "8",
                    ["efficiencyScore"] = "0.15",
                    ["avgExecutionTimeMs"] = "5000"
                },
                DetectedAtUtc: DateTimeOffset.UtcNow)
        };

        var recommendations = await engine.RecommendImprovementsAsync(inefficiencies);

        recommendations.Should().HaveCountGreaterThanOrEqualTo(1);
        var rec = recommendations.First(r => r.Category == "agent-efficiency");
        // CurrentValue 0.2 < 0.3 => severe => "replace-agent" action, High priority
        rec.ProposedAction.Should().Be("replace-agent");
        rec.Priority.Should().Be(ImprovementPriority.High);
        rec.Target.Should().Be("BadAgent");
        rec.EstimatedImpact.Should().BeGreaterThan(0);
        rec.EstimatedImpact.Should().BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public async Task ContinuousImprovementEngine_RecommendImprovementsAsync_CascadingFailure_GeneratesUrgentRecommendation()
    {
        var engine = CreateCIEngine();

        var inefficiencies = new List<DetectedInefficiency>
        {
            new(
                InefficiencyId: Guid.NewGuid(),
                Type: InefficiencyType.CascadingFailure,
                Severity: InefficiencySeverity.Critical,
                Target: "system",
                Description: "Multiple failures",
                CurrentValue: 0.3,
                ThresholdValue: 0.6,
                DeviationPercent: 50,
                Evidence: new Dictionary<string, string>
                {
                    ["failingAgents"] = "4",
                    ["failingTasks"] = "3",
                    ["overallHealthScore"] = "0.3"
                },
                DetectedAtUtc: DateTimeOffset.UtcNow)
        };

        var recommendations = await engine.RecommendImprovementsAsync(inefficiencies);

        recommendations.Should().HaveCount(1);
        var rec = recommendations[0];
        rec.Priority.Should().Be(ImprovementPriority.Urgent);
        rec.Category.Should().Be("system-stability");
        rec.ProposedAction.Should().Be("system-diagnostic");
        rec.EstimatedImpact.Should().Be(0.9);
    }

    [Fact]
    public async Task ContinuousImprovementEngine_GetTrendsAsync_NoData_ReturnsEmpty()
    {
        var engine = CreateCIEngine();

        var trends = await engine.GetTrendsAsync();

        // No cycles run => no trend data => empty
        trends.Should().BeEmpty();
    }

    [Fact]
    public async Task ContinuousImprovementEngine_RunCycleAsync_PublishesEventAndTrace()
    {
        var perfAnalyzer = new Mock<IPerformanceAnalyzer>();
        perfAnalyzer.Setup(p => p.AnalyzeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyPerformanceReport(0.85));

        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(e => e.PublishAsync(It.IsAny<SystemEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(t => t.RecordAsync(It.IsAny<TraceEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var engine = CreateCIEngine(
            perfAnalyzer: perfAnalyzer,
            eventBus: eventBus,
            traceStore: traceStore);

        await engine.RunCycleAsync();

        eventBus.Verify(e => e.PublishAsync(
            It.Is<SystemEvent>(ev => ev.EventType == "optimization.continuous-improvement.cycle-completed"),
            It.IsAny<CancellationToken>()), Times.Once);

        traceStore.Verify(t => t.RecordAsync(
            It.Is<TraceEntry>(te => te.Category == "continuous-improvement-cycle"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ══════════════════════════════════════════════════════════════
    //  OptimizationOptions tests
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void OptimizationOptions_Defaults_HaveExpectedValues()
    {
        var options = new OptimizationOptions();

        options.InefficientStepThreshold.Should().Be(6);
        options.MaxImprovedStrategies.Should().Be(3);
        options.AutoDeployOptimizedWorkflows.Should().BeTrue();
        options.MinSamplesForAnalysis.Should().Be(10);
        options.AgentSuccessRateThreshold.Should().Be(0.6);
        options.ModelSuccessRateThreshold.Should().Be(0.7);
        options.TaskCompletionRateThreshold.Should().Be(0.65);
        options.AutoApplyImprovements.Should().BeTrue();
    }
}
