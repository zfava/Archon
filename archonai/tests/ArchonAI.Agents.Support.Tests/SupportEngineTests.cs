using ArchonAI.Agents.Support;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.DataFabric;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Agents.Support.Tests;

public sealed class SupportEngineTests
{
    private readonly IDataFabricEngine _dataFabric = Substitute.For<IDataFabricEngine>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<SupportEngine> _logger = Substitute.For<ILogger<SupportEngine>>();
    private readonly SupportOptions _options = new();

    private SupportEngine CreateEngine() =>
        new(_dataFabric, _eventBus, _logger, Options.Create(_options));

    private void SetupFabricResult(IReadOnlyDictionary<string, string>[] rows)
    {
        _dataFabric.QueryEnterpriseDataAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataFabricQueryResult(true, "ok", rows,
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));
    }

    private static IReadOnlyDictionary<string, string> TicketRow(
        string status, string category, string resolutionHours = "24", string satisfaction = "4.0", string title = "Issue")
    {
        return new Dictionary<string, string>
        {
            ["status"] = status,
            ["category"] = category,
            ["resolution_hours"] = resolutionHours,
            ["satisfaction"] = satisfaction,
            ["title"] = title
        };
    }

    [Fact]
    public async Task AnalyzeTicketsAsync_ReturnsCorrectCounts()
    {
        var rows = new[]
        {
            TicketRow("open", "billing"),
            TicketRow("open", "billing"),
            TicketRow("resolved", "technical", "12", "5.0"),
            TicketRow("resolved", "technical", "36", "3.0")
        };
        SetupFabricResult(rows);

        var engine = CreateEngine();
        var result = await engine.AnalyzeTicketsAsync("test-scope", new Dictionary<string, string>());

        result.IsSuccess.Should().BeTrue();
        result.Scope.Should().Be("test-scope");
        result.TotalTickets.Should().Be(4);
        result.OpenTickets.Should().Be(2);
        result.ResolvedTickets.Should().Be(2);
        result.TopCategories.Should().Contain("billing");
        result.TopCategories.Should().Contain("technical");
    }

    [Fact]
    public async Task AnalyzeTicketsAsync_CalculatesAverageResolutionHours()
    {
        var rows = new[]
        {
            TicketRow("resolved", "billing", "10", "4.0"),
            TicketRow("resolved", "billing", "30", "4.0")
        };
        SetupFabricResult(rows);

        var engine = CreateEngine();
        var result = await engine.AnalyzeTicketsAsync("scope", new Dictionary<string, string>());

        result.AverageResolutionHours.Should().Be(20.0);
    }

    [Fact]
    public async Task AnalyzeTicketsAsync_CalculatesSatisfactionScore()
    {
        var rows = new[]
        {
            TicketRow("resolved", "billing", "10", "2.0"),
            TicketRow("resolved", "billing", "10", "4.0")
        };
        SetupFabricResult(rows);

        var engine = CreateEngine();
        var result = await engine.AnalyzeTicketsAsync("scope", new Dictionary<string, string>());

        result.SatisfactionScore.Should().Be(3.0);
    }

    [Fact]
    public async Task AnalyzeTicketsAsync_EmitsAuditEvent()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        await engine.AnalyzeTicketsAsync("scope", new Dictionary<string, string>());

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "support.tickets.analyzed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DetectRecurringIssuesAsync_ReturnsIssuesAboveThreshold()
    {
        var rows = new[]
        {
            TicketRow("open", "billing"),
            TicketRow("open", "billing"),
            TicketRow("open", "billing"),
            TicketRow("resolved", "billing"),
            TicketRow("open", "technical")
        };
        SetupFabricResult(rows);

        var engine = CreateEngine();
        var result = await engine.DetectRecurringIssuesAsync("*", 3);

        result.Should().HaveCount(1);
        result[0].Category.Should().Be("billing");
        result[0].Occurrences.Should().Be(4);
    }

    [Fact]
    public async Task DetectRecurringIssuesAsync_ReturnsEmptyWhenNoneAboveThreshold()
    {
        var rows = new[]
        {
            TicketRow("open", "billing"),
            TicketRow("open", "technical")
        };
        SetupFabricResult(rows);

        var engine = CreateEngine();
        var result = await engine.DetectRecurringIssuesAsync("*", 3);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RecommendAutoResponsesAsync_GeneratesRecommendations()
    {
        var rows = Enumerable.Range(0, 5)
            .Select(_ => TicketRow("open", "billing"))
            .Concat(Enumerable.Range(0, 5).Select(_ => TicketRow("open", "technical")))
            .ToArray();
        SetupFabricResult(rows);

        var engine = CreateEngine();
        var result = await engine.RecommendAutoResponsesAsync("billing");

        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RecommendAutoResponsesAsync_EmitsAuditEvent()
    {
        var rows = Enumerable.Range(0, 5)
            .Select(_ => TicketRow("open", "billing"))
            .ToArray();
        SetupFabricResult(rows);

        var engine = CreateEngine();
        await engine.RecommendAutoResponsesAsync("billing");

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "support.autoresponses.generated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetStatus_ReturnsActiveStatus()
    {
        var engine = CreateEngine();
        var status = engine.GetStatus();

        status.IsActive.Should().BeTrue();
        status.TicketsAnalyzed.Should().Be(0);
        status.RecurringIssuesDetected.Should().Be(0);
        status.AutoResponsesGenerated.Should().Be(0);
        status.DataFabricQueries.Should().Be(0);
    }

    [Fact]
    public async Task GetStatus_ReflectsOperationCounts()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        await engine.AnalyzeTicketsAsync("scope", new Dictionary<string, string>());
        await engine.DetectRecurringIssuesAsync("*", 1);

        var status = engine.GetStatus();
        status.TicketsAnalyzed.Should().Be(1);
        status.DataFabricQueries.Should().Be(2);
    }
}
