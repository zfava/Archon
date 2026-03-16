using ArchonAI.Agents.Operations;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Operations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Agents.Operations.Tests;

public sealed class OperationsAgentTests
{
    private readonly IOperationsEngine _operationsEngine = Substitute.For<IOperationsEngine>();
    private readonly IModelProvider _modelProvider = Substitute.For<IModelProvider>();
    private readonly ILogger<OperationsAgent> _logger = Substitute.For<ILogger<OperationsAgent>>();

    private OperationsAgent CreateAgent() => new(_operationsEngine, _modelProvider, _logger);

    private static CoreTask CreateTask(string capability, Dictionary<string, string>? inputs = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, "TestTask", "Test",
        capability, inputs ?? new Dictionary<string, string>(),
        DateTimeOffset.UtcNow, null, null);

    private static CoreExecutionContext CreateContext(Guid? objectiveId = null) => new(
        Guid.NewGuid(), objectiveId ?? Guid.NewGuid(), Guid.NewGuid(),
        "test-tenant", new Dictionary<string, string>(), DateTimeOffset.UtcNow);

    [Fact]
    public void Describe_ReturnsOperationsAgent()
    {
        var agent = CreateAgent();
        var desc = agent.Describe();

        desc.Name.Should().Be("OperationsAgent");
        desc.Version.Should().Be("1.0.0");
        desc.Capabilities.Should().HaveCount(4);
        desc.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WorkflowAnalysis_DelegatesToEngine()
    {
        var objectiveId = Guid.NewGuid();
        _operationsEngine.AnalyzeWorkflowAsync(objectiveId, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowAnalysisResult(
                objectiveId, true, 10, 8, 2, 0.8,
                new[] { "API timeout" }, new[] { "Add retry" },
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var task = CreateTask("workflow-analysis");
        var ctx = CreateContext(objectiveId);

        var result = await agent.ExecuteAsync(task, ctx);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["efficiencyScore"].Should().Be("0.800");
        result.Outputs["totalSteps"].Should().Be("10");
    }

    [Fact]
    public async Task ExecuteAsync_InefficiencyDetection_DelegatesToEngine()
    {
        _operationsEngine.IdentifyInefficienciesAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<OperationsInsight>
            {
                new(Guid.NewGuid(), "high-failure-rate", "High failure", "Lots of failures", 0.9, "crm",
                    new Dictionary<string, string>(), DateTimeOffset.UtcNow)
            });

        var agent = CreateAgent();
        var task = CreateTask("inefficiency-detection", new Dictionary<string, string> { ["scope"] = "crm" });

        var result = await agent.ExecuteAsync(task, CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["insightCount"].Should().Be("1");
        result.Outputs["scope"].Should().Be("crm");
    }

    [Fact]
    public async Task ExecuteAsync_ImprovementRecommendation_DelegatesToEngine()
    {
        var objectiveId = Guid.NewGuid();
        _operationsEngine.RecommendImprovementsAsync(objectiveId, Arg.Any<CancellationToken>())
            .Returns(new List<OperationsRecommendation>
            {
                new(Guid.NewGuid(), objectiveId, "failure-mitigation", "Fix timeouts",
                    "Add retry logic", 0.8, "safe-mode", new[] { "workflow-orchestration" }, DateTimeOffset.UtcNow)
            });

        var agent = CreateAgent();
        var task = CreateTask("improvement-recommendation");
        var ctx = CreateContext(objectiveId);

        var result = await agent.ExecuteAsync(task, ctx);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["recommendationCount"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_WorkflowCoordination_DelegatesToEngine()
    {
        _operationsEngine.CoordinateWorkflowAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowCoordinationResult(
                Guid.NewGuid(), true, "balanced", 2, 2, 0,
                new Dictionary<string, string>(), Array.Empty<string>(),
                TimeSpan.FromMilliseconds(150), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var task = CreateTask("workflow-coordination", new Dictionary<string, string>
        {
            ["workflowTemplate"] = "balanced",
            ["agentCapabilities"] = "workflow-orchestration,tooling-execution"
        });

        var result = await agent.ExecuteAsync(task, CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Summary.Should().Contain("balanced");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCapability_UsesReasoningLoop()
    {
        _modelProvider.GenerateAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse(
                "test-provider", "test-model", true,
                "Operations analysis complete",
                Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var task = CreateTask("custom-capability", new Dictionary<string, string>
        {
            ["prompt"] = "Analyze custom workflow"
        });

        var result = await agent.ExecuteAsync(task, CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["provider"].Should().Be("test-provider");
        result.Outputs["content"].Should().Be("Operations analysis complete");
    }
}
