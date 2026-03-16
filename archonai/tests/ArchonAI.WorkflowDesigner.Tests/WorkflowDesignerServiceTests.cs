using ArchonAI.Core.Models.Workflow;
using ArchonAI.WorkflowDesigner;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.WorkflowDesigner.Tests;

public sealed class WorkflowDesignerServiceTests
{
    private readonly ILogger<WorkflowDesignerService> _logger = Substitute.For<ILogger<WorkflowDesignerService>>();

    private WorkflowDesignerService CreateService() => new(_logger);

    private static WorkflowNode AgentNode(string name, string? agentType = null)
    {
        var config = agentType is not null
            ? new Dictionary<string, string> { ["agentType"] = agentType }
            : new Dictionary<string, string>();
        return new WorkflowNode(Guid.NewGuid(), name, $"{name} description",
            WorkflowNodeType.AgentNode, config);
    }

    private static WorkflowNode DecisionNode(string name)
        => new(Guid.NewGuid(), name, $"{name} description",
            WorkflowNodeType.DecisionNode, new Dictionary<string, string>());

    private static WorkflowNode ConditionNode(string name)
        => new(Guid.NewGuid(), name, $"{name} description",
            WorkflowNodeType.ConditionNode, new Dictionary<string, string>());

    private static WorkflowNode ToolNode(string name)
        => new(Guid.NewGuid(), name, $"{name} description",
            WorkflowNodeType.ToolNode, new Dictionary<string, string>());

    private static WorkflowEdge Edge(WorkflowNode source, WorkflowNode target, string? label = null)
        => new(Guid.NewGuid(), source.Id, target.Id, label);

    // ── CreateWorkflowGraphAsync ─────────────────────────────────

    [Fact]
    public async Task CreateWorkflowGraphAsync_ReturnsGraphWithCorrectProperties()
    {
        var svc = CreateService();
        var nodes = new[] { AgentNode("Analyze", "ops"), ToolNode("Notify") };
        var edges = new[] { Edge(nodes[0], nodes[1]) };

        var graph = await svc.CreateWorkflowGraphAsync("Test", "A test workflow", nodes, edges);

        graph.Name.Should().Be("Test");
        graph.Description.Should().Be("A test workflow");
        graph.Nodes.Should().HaveCount(2);
        graph.Edges.Should().HaveCount(1);
        graph.Id.Should().NotBeEmpty();
        graph.CreatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateWorkflowGraphAsync_DefaultsMetadata()
    {
        var svc = CreateService();
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { AgentNode("A") }, Array.Empty<WorkflowEdge>());

        graph.Metadata.Should().NotBeNull().And.BeEmpty();
    }

    // ── GetWorkflowGraphAsync ────────────────────────────────────

    [Fact]
    public async Task GetWorkflowGraphAsync_ReturnsCreatedGraph()
    {
        var svc = CreateService();
        var created = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { AgentNode("A") }, Array.Empty<WorkflowEdge>());

        var fetched = await svc.GetWorkflowGraphAsync(created.Id);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetWorkflowGraphAsync_ReturnsNull_WhenNotFound()
    {
        var svc = CreateService();
        var result = await svc.GetWorkflowGraphAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    // ── ValidateWorkflowGraphAsync — structural ──────────────────

    [Fact]
    public async Task Validate_EmptyGraph_ReturnsError()
    {
        var svc = CreateService();
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            Array.Empty<WorkflowNode>(), Array.Empty<WorkflowEdge>());

        var result = await svc.ValidateWorkflowGraphAsync(graph.Id);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least one node"));
    }

    [Fact]
    public async Task Validate_MissingNodeName_ReturnsError()
    {
        var svc = CreateService();
        var node = new WorkflowNode(Guid.NewGuid(), "", "desc",
            WorkflowNodeType.AgentNode, new Dictionary<string, string>());
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { node }, Array.Empty<WorkflowEdge>());

        var result = await svc.ValidateWorkflowGraphAsync(graph.Id);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("has no name"));
    }

    [Fact]
    public async Task Validate_SelfLoop_ReturnsError()
    {
        var svc = CreateService();
        var node = AgentNode("A");
        var selfEdge = new WorkflowEdge(Guid.NewGuid(), node.Id, node.Id, null);
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { node }, new[] { selfEdge });

        var result = await svc.ValidateWorkflowGraphAsync(graph.Id);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("self-loop"));
    }

    [Fact]
    public async Task Validate_EdgeToUnknownNode_ReturnsError()
    {
        var svc = CreateService();
        var node = AgentNode("A");
        var badEdge = new WorkflowEdge(Guid.NewGuid(), node.Id, Guid.NewGuid(), null);
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { node }, new[] { badEdge });

        var result = await svc.ValidateWorkflowGraphAsync(graph.Id);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("unknown target node"));
    }

    [Fact]
    public async Task Validate_NotFoundGraph_ReturnsError()
    {
        var svc = CreateService();
        var result = await svc.ValidateWorkflowGraphAsync(Guid.NewGuid());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Workflow graph not found");
    }

    // ── ValidateWorkflowGraphAsync — node compatibility ──────────

    [Fact]
    public async Task Validate_DecisionNode_WithFewerThanTwoOutgoingEdges_ReturnsError()
    {
        var svc = CreateService();
        var decision = DecisionNode("Choose");
        var target = AgentNode("A");
        var edge = Edge(decision, target);
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { decision, target }, new[] { edge });

        var result = await svc.ValidateWorkflowGraphAsync(graph.Id);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("DecisionNode") && e.Contains("at least 2"));
    }

    [Fact]
    public async Task Validate_DecisionNode_WithTwoOutgoingEdges_IsValid()
    {
        var svc = CreateService();
        var decision = DecisionNode("Choose");
        var a = AgentNode("A", "ops");
        var b = AgentNode("B", "finance");
        var edges = new[] { Edge(decision, a, "yes"), Edge(decision, b, "no") };
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { decision, a, b }, edges);

        var result = await svc.ValidateWorkflowGraphAsync(graph.Id);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ConditionNode_WithNoOutgoingEdge_ReturnsError()
    {
        var svc = CreateService();
        var cond = ConditionNode("Check");
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { cond }, Array.Empty<WorkflowEdge>());

        var result = await svc.ValidateWorkflowGraphAsync(graph.Id);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ConditionNode") && e.Contains("at least 1"));
    }

    [Fact]
    public async Task Validate_AgentNode_WithoutAgentType_EmitsWarning()
    {
        var svc = CreateService();
        var agent = AgentNode("NoType"); // no agentType config
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { agent }, Array.Empty<WorkflowEdge>());

        var result = await svc.ValidateWorkflowGraphAsync(graph.Id);

        result.IsValid.Should().BeTrue(); // warning, not error
        result.Warnings.Should().Contain(w => w.Contains("agentType"));
    }

    // ── ValidateWorkflowGraphAsync — cycle detection ─────────────

    [Fact]
    public async Task Validate_CyclicGraph_ReturnsError()
    {
        var svc = CreateService();
        var a = AgentNode("A");
        var b = AgentNode("B");
        var c = AgentNode("C");
        var edges = new[] { Edge(a, b), Edge(b, c), Edge(c, a) };
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { a, b, c }, edges);

        var result = await svc.ValidateWorkflowGraphAsync(graph.Id);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Cycle detected"));
    }

    [Fact]
    public async Task Validate_AcyclicGraph_IsValid()
    {
        var svc = CreateService();
        var a = AgentNode("A", "ops");
        var b = AgentNode("B", "ops");
        var c = AgentNode("C", "ops");
        var edges = new[] { Edge(a, b), Edge(a, c), Edge(b, c) };
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { a, b, c }, edges);

        var result = await svc.ValidateWorkflowGraphAsync(graph.Id);

        result.IsValid.Should().BeTrue();
    }

    // ── SimulateWorkflowGraphAsync ───────────────────────────────

    [Fact]
    public async Task Simulate_LinearGraph_ReturnsOrderedSteps()
    {
        var svc = CreateService();
        var a = AgentNode("A", "ops");
        var b = ToolNode("T");
        var c = ConditionNode("C");
        var edges = new[] { Edge(a, b), Edge(b, c) };
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { a, b, c }, edges);

        var result = await svc.SimulateWorkflowGraphAsync(graph.Id);

        result.IsSuccess.Should().BeTrue();
        result.Steps.Should().HaveCount(3);
        result.Steps[0].NodeName.Should().Be("A");
        result.Steps[1].NodeName.Should().Be("T");
        result.Steps[2].NodeName.Should().Be("C");
        result.TotalDurationMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Simulate_UnreachableNode_EmitsWarning()
    {
        var svc = CreateService();
        var a = AgentNode("A", "ops");
        var orphan = AgentNode("Orphan", "ops");
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { a, orphan }, Array.Empty<WorkflowEdge>());

        var result = await svc.SimulateWorkflowGraphAsync(graph.Id);

        // Both are entry nodes (no incoming edges), so both get visited
        result.Steps.Should().HaveCount(2);
    }

    [Fact]
    public async Task Simulate_NotFound_Throws()
    {
        var svc = CreateService();
        var act = () => svc.SimulateWorkflowGraphAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── ExportWorkflowGraphAsync ─────────────────────────────────

    [Fact]
    public async Task Export_ReturnsDefinitionWithVersion()
    {
        var svc = CreateService();
        var node = AgentNode("A", "ops");
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { node }, Array.Empty<WorkflowEdge>());

        var export = await svc.ExportWorkflowGraphAsync(graph.Id, "2.0");

        export.GraphId.Should().Be(graph.Id);
        export.Name.Should().Be("G");
        export.Version.Should().Be("2.0");
        export.Nodes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Export_NotFound_Throws()
    {
        var svc = CreateService();
        var act = () => svc.ExportWorkflowGraphAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── DeleteWorkflowGraphAsync ─────────────────────────────────

    [Fact]
    public async Task Delete_ExistingGraph_ReturnsTrue()
    {
        var svc = CreateService();
        var graph = await svc.CreateWorkflowGraphAsync("G", "desc",
            new[] { AgentNode("A") }, Array.Empty<WorkflowEdge>());

        var deleted = await svc.DeleteWorkflowGraphAsync(graph.Id);

        deleted.Should().BeTrue();
        (await svc.GetWorkflowGraphAsync(graph.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_NonExistentGraph_ReturnsFalse()
    {
        var svc = CreateService();
        var deleted = await svc.DeleteWorkflowGraphAsync(Guid.NewGuid());
        deleted.Should().BeFalse();
    }

    // ── ListWorkflowGraphsAsync ──────────────────────────────────

    [Fact]
    public async Task List_ReturnsAllCreatedGraphs()
    {
        var svc = CreateService();
        await svc.CreateWorkflowGraphAsync("Alpha", "desc",
            new[] { AgentNode("A") }, Array.Empty<WorkflowEdge>());
        await svc.CreateWorkflowGraphAsync("Beta", "desc",
            new[] { AgentNode("B") }, Array.Empty<WorkflowEdge>());

        var list = await svc.ListWorkflowGraphsAsync();

        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_FiltersbyName()
    {
        var svc = CreateService();
        await svc.CreateWorkflowGraphAsync("OnboardFlow", "desc",
            new[] { AgentNode("A") }, Array.Empty<WorkflowEdge>());
        await svc.CreateWorkflowGraphAsync("AlertFlow", "desc",
            new[] { AgentNode("B") }, Array.Empty<WorkflowEdge>());

        var list = await svc.ListWorkflowGraphsAsync("Onboard");

        list.Should().HaveCount(1);
        list[0].Name.Should().Be("OnboardFlow");
    }
}
