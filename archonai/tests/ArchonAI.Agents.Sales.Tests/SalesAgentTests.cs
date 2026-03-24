using ArchonAI.Agents.Sales;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Sales;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Agents.Sales.Tests;

public sealed class SalesAgentTests
{
    private readonly ISalesEngine _salesEngine = Substitute.For<ISalesEngine>();
    private readonly IModelProvider _modelProvider = Substitute.For<IModelProvider>();
    private readonly ILogger<SalesAgent> _logger = Substitute.For<ILogger<SalesAgent>>();

    private SalesAgent CreateAgent() => new(_salesEngine, _modelProvider, _logger);

    private static CoreTask CreateTask(string capability, Dictionary<string, string>? inputs = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, "TestTask", "Test",
        capability, inputs ?? new Dictionary<string, string>(),
        DateTimeOffset.UtcNow, null, null);

    private static CoreExecutionContext CreateContext() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "test-tenant", new Dictionary<string, string>(), DateTimeOffset.UtcNow);

    [Fact]
    public void Describe_ReturnsSalesAgent()
    {
        var agent = CreateAgent();
        var desc = agent.Describe();

        desc.Name.Should().Be("SalesAgent");
        desc.Version.Should().Be("1.0.0");
        desc.Capabilities.Should().HaveCount(4);
        desc.IsEnabled.Should().BeTrue();
        desc.Id.Should().Be(Guid.Parse("b5e9d723-8a41-4c6b-a3f2-1d7b5e094c86"));
    }

    [Fact]
    public async Task ExecuteAsync_PipelineAnalysis_DelegatesToEngine()
    {
        _salesEngine.AnalyzePipelineAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineAnalysisResult(
                true, "*", 5, 100000, 60000, 0.5,
                new[] { "proposal: 3", "negotiation: 2" }, Array.Empty<string>(),
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("pipeline-analysis"), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["totalOpportunities"].Should().Be("5");
        result.Outputs["totalValue"].Should().Be("100000.00");
    }

    [Fact]
    public async Task ExecuteAsync_OpportunityPrioritization_DelegatesToEngine()
    {
        _salesEngine.PrioritizeOpportunitiesAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PrioritizedOpportunity>
            {
                new("opp-1", "Big Deal", 90000, 0.95, "negotiation", "High value",
                    DateTimeOffset.UtcNow.AddDays(30), new Dictionary<string, string>())
            });

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("opportunity-prioritization"), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["opportunityCount"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_OutreachRecommendation_DelegatesToEngine()
    {
        _salesEngine.RecommendOutreachAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutreachRecommendation>
            {
                new(Guid.NewGuid(), "opp-1", "follow-up", "Follow up", "Send follow-up",
                    0.7, "within 2 days", new Dictionary<string, string>(), DateTimeOffset.UtcNow)
            });

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(
            CreateTask("outreach-recommendation", new Dictionary<string, string> { ["opportunityId"] = "opp-1" }),
            CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["recommendationCount"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_SalesMetrics_DelegatesToEngine()
    {
        _salesEngine.GetMetricsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SalesMetricsSnapshot(
                "*", "Q1", 90000, 170000, 10, 5, 0.667, 9000, 30,
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("sales-metrics"), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["totalRevenue"].Should().Be("90000.00");
        result.Outputs["winRate"].Should().Be("0.6670");
        result.Outputs["dealsWon"].Should().Be("10");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCapability_UsesModel()
    {
        _modelProvider.GenerateAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse(
                "test", "model", true, "Sales insight",
                Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("custom-sales"), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["content"].Should().Be("Sales insight");
    }
}
