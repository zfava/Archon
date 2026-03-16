using ArchonAI.Agents.Marketing;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Marketing;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Agents.Marketing.Tests;

public sealed class MarketingAgentTests
{
    private readonly IMarketingEngine _marketingEngine = Substitute.For<IMarketingEngine>();
    private readonly IModelProvider _modelProvider = Substitute.For<IModelProvider>();
    private readonly ILogger<MarketingAgent> _logger = Substitute.For<ILogger<MarketingAgent>>();

    private MarketingAgent CreateAgent() => new(_marketingEngine, _modelProvider, _logger);

    private static CoreTask CreateTask(string capability, Dictionary<string, string>? inputs = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, "TestTask", "Test",
        capability, inputs ?? new Dictionary<string, string>(),
        DateTimeOffset.UtcNow, null, null);

    private static CoreExecutionContext CreateContext() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "test-tenant", new Dictionary<string, string>(), DateTimeOffset.UtcNow);

    [Fact]
    public void Describe_ReturnsMarketingAgent()
    {
        var agent = CreateAgent();
        var desc = agent.Describe();

        desc.Name.Should().Be("MarketingAgent");
        desc.Version.Should().Be("1.0.0");
        desc.Capabilities.Should().HaveCount(3);
        desc.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_CampaignAnalysis_DelegatesToEngine()
    {
        _marketingEngine.AnalyzeCampaignAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new CampaignAnalysisResult(
                true, "camp-1", 5000, 15000, 2.0, 0.1, 100000, 5000,
                new[] { "Strong ROI" }, Array.Empty<string>(),
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("campaign-analysis"), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["roi"].Should().Be("2.0000");
    }

    [Fact]
    public async Task ExecuteAsync_StrategyRecommendation_DelegatesToEngine()
    {
        _marketingEngine.RecommendStrategiesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MarketingStrategyRecommendation>
            {
                new(Guid.NewGuid(), "scale-up", "Scale email", "Strong performance", 3.0, 0.9,
                    new[] { "email" }, new Dictionary<string, string>(), DateTimeOffset.UtcNow)
            });

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("strategy-recommendation", new Dictionary<string, string> { ["scope"] = "all" }), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["strategyCount"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_EngagementMetrics_DelegatesToEngine()
    {
        _marketingEngine.GetEngagementMetricsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EngagementMetricsSnapshot(
                "*", "Q1", 80000, 4000, 350, 0.05, 0.0875, 22.86,
                new Dictionary<string, string>(), new Dictionary<string, string>(),
                DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("engagement-metrics"), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["totalImpressions"].Should().Be("80000");
        result.Outputs["totalClicks"].Should().Be("4000");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCapability_UsesModel()
    {
        _modelProvider.GenerateAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse(
                "test", "model", true, "Marketing insight",
                Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("custom-marketing"), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["content"].Should().Be("Marketing insight");
    }
}
