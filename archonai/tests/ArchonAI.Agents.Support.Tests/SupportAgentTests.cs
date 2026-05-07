using ArchonAI.Agents.Support;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Agents.Support.Tests;

public sealed class SupportAgentTests
{
    private readonly ISupportEngine _supportEngine = Substitute.For<ISupportEngine>();
    private readonly IModelProvider _modelProvider = Substitute.For<IModelProvider>();
    private readonly ILogger<SupportAgent> _logger = Substitute.For<ILogger<SupportAgent>>();

    private SupportAgent CreateAgent() => new(_supportEngine, _modelProvider, _logger);

    private static CoreTask CreateTask(string capability, Dictionary<string, string>? inputs = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, "TestTask", "Test",
        capability, inputs ?? new Dictionary<string, string>(),
        DateTimeOffset.UtcNow, null, null);

    private static CoreExecutionContext CreateContext() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "test-tenant", new Dictionary<string, string>(), DateTimeOffset.UtcNow);

    [Fact]
    public void Describe_ReturnsCorrectAgent()
    {
        var agent = CreateAgent();
        var desc = agent.Describe();

        desc.Id.Should().Be(Guid.Parse("e7a1f945-ac63-4e8d-b514-3f9d70216ea8"));
        desc.Name.Should().Be("SupportAgent");
        desc.Capabilities.Should().HaveCount(3);
        desc.Capabilities.Select(c => c.Name).Should().Contain("ticket-analysis");
        desc.Capabilities.Select(c => c.Name).Should().Contain("recurring-issue-detection");
        desc.Capabilities.Select(c => c.Name).Should().Contain("auto-response-recommendation");
    }

    [Fact]
    public async Task ExecuteAsync_TicketAnalysis_CallsEngine()
    {
        var analysisResult = new TicketAnalysisResult(
            true, "test", 10, 3, 7, 24.0, 4.5,
            new List<string> { "billing" },
            new List<string>(),
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);

        _supportEngine.AnalyzeTicketsAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(analysisResult);

        var agent = CreateAgent();
        var task = CreateTask("ticket-analysis", new Dictionary<string, string> { ["scope"] = "test" });
        var result = await agent.ExecuteAsync(task, CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Summary.Should().Contain("test");
        result.Outputs["totalTickets"].Should().Be("10");
    }

    [Fact]
    public async Task ExecuteAsync_RecurringIssueDetection_CallsEngine()
    {
        var issues = new List<RecurringIssue>
        {
            new(Guid.NewGuid(), "billing", "Billing issue", "Recurring", 5, 0.6,
                "Fix billing", new Dictionary<string, string>(), DateTimeOffset.UtcNow)
        };

        _supportEngine.DetectRecurringIssuesAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(issues);

        var agent = CreateAgent();
        var task = CreateTask("recurring-issue-detection", new Dictionary<string, string>
        {
            ["scope"] = "*", ["minOccurrences"] = "5"
        });
        var result = await agent.ExecuteAsync(task, CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Summary.Should().Contain("1 recurring issues");
    }

    [Fact]
    public async Task ExecuteAsync_AutoResponseRecommendation_CallsEngine()
    {
        var recommendations = new List<AutoResponseRecommendation>
        {
            new(Guid.NewGuid(), "billing", "Auto-response for billing",
                "Thank you for contacting support...", 0.85, 10,
                new Dictionary<string, string>(), DateTimeOffset.UtcNow)
        };

        _supportEngine.RecommendAutoResponsesAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(recommendations);

        var agent = CreateAgent();
        var task = CreateTask("auto-response-recommendation", new Dictionary<string, string>
        {
            ["issueCategory"] = "billing"
        });
        var result = await agent.ExecuteAsync(task, CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Summary.Should().Contain("1 auto-response recommendations");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCapability_FallsBackToModel()
    {
        _modelProvider.GenerateAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("test-provider", "test-model", true, "model content",
                Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var task = CreateTask("unknown-capability");
        var result = await agent.ExecuteAsync(task, CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["provider"].Should().Be("test-provider");
        await _modelProvider.Received(1).GenerateAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }
}
