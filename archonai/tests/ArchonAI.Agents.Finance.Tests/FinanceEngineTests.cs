using ArchonAI.Agents.Finance;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.DataFabric;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Agents.Finance.Tests;

public sealed class FinanceEngineTests
{
    private readonly IDataFabricEngine _dataFabric = Substitute.For<IDataFabricEngine>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<FinanceEngine> _logger = Substitute.For<ILogger<FinanceEngine>>();
    private readonly FinanceOptions _options = new();

    private FinanceEngine CreateEngine() =>
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

    [Fact]
    public async Task AnalyzePerformanceAsync_ReturnsAnalysisWithGrowth()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["revenue"] = "100000", ["expenses"] = "70000", ["prevRevenue"] = "90000", ["prevExpenses"] = "65000" }
        });

        var engine = CreateEngine();
        var result = await engine.AnalyzePerformanceAsync("*", new Dictionary<string, string>());

        result.IsSuccess.Should().BeTrue();
        result.RevenueGrowthRate.Should().BeApproximately(0.111, 0.01);
        result.ProfitMargin.Should().BeApproximately(0.3, 0.01);
        result.KeyFindings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AnalyzePerformanceAsync_DetectsRisks()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["revenue"] = "100000", ["expenses"] = "95000", ["prevRevenue"] = "90000", ["prevExpenses"] = "70000" }
        });

        var engine = CreateEngine();
        var result = await engine.AnalyzePerformanceAsync("*", new Dictionary<string, string>());

        result.Risks.Should().Contain(r => r.Contains("Expenses growing faster"));
        result.ProfitMargin.Should().BeLessThan(0.1);
    }

    [Fact]
    public async Task AnalyzePerformanceAsync_EmitsAuditEvent()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        await engine.AnalyzePerformanceAsync("*", new Dictionary<string, string>());

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "finance.performance.analyzed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DetectAnomaliesAsync_FindsOutliers()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["amount"] = "100", ["account"] = "ops" },
            new Dictionary<string, string> { ["amount"] = "105", ["account"] = "ops" },
            new Dictionary<string, string> { ["amount"] = "98", ["account"] = "ops" },
            new Dictionary<string, string> { ["amount"] = "500", ["account"] = "marketing" }
        });

        var engine = CreateEngine();
        var anomalies = await engine.DetectAnomaliesAsync("*");

        anomalies.Should().NotBeEmpty();
        anomalies.Should().Contain(a => a.AnomalyType == "unusually-high");
    }

    [Fact]
    public async Task DetectAnomaliesAsync_ReturnsEmptyWhenNoData()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        var anomalies = await engine.DetectAnomaliesAsync("*");

        anomalies.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateSummaryAsync_ReturnsSummary()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["amount"] = "50000", ["type"] = "revenue", ["category"] = "Services" },
            new Dictionary<string, string> { ["amount"] = "30000", ["type"] = "revenue", ["category"] = "Products" },
            new Dictionary<string, string> { ["amount"] = "20000", ["type"] = "expense", ["category"] = "Salaries" },
            new Dictionary<string, string> { ["amount"] = "10000", ["type"] = "expense", ["category"] = "Operations" }
        });

        var engine = CreateEngine();
        var summary = await engine.GenerateSummaryAsync("*", "Q1-2026");

        summary.TotalRevenue.Should().Be(80000);
        summary.TotalExpenses.Should().Be(30000);
        summary.NetIncome.Should().Be(50000);
        summary.ProfitMargin.Should().BeApproximately(0.625, 0.01);
        summary.TopRevenueItems.Should().HaveCount(2);
        summary.TopExpenseItems.Should().HaveCount(2);
    }

    [Fact]
    public async Task GenerateSummaryAsync_EmitsAuditEvent()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        await engine.GenerateSummaryAsync("*", "current");

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "finance.summary.generated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssistBudgetingAsync_ReturnsWorkflowResult()
    {
        SetupFabricResult(new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["amount"] = "5000", ["category"] = "Software" },
            new Dictionary<string, string> { ["amount"] = "3000", ["category"] = "Hardware" }
        });

        var engine = CreateEngine();
        var result = await engine.AssistBudgetingAsync("eng-dept",
            new Dictionary<string, string> { ["currentBudget"] = "10000" });

        result.IsSuccess.Should().BeTrue();
        result.DepartmentId.Should().Be("eng-dept");
        result.Recommendations.Should().NotBeEmpty();
        result.AllocatedBudget.Should().Be(10000);
    }

    [Fact]
    public async Task AssistBudgetingAsync_ThrowsOnEmptyDepartmentId()
    {
        var engine = CreateEngine();

        var act = () => engine.AssistBudgetingAsync(string.Empty, new Dictionary<string, string>());

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("departmentId");
    }

    [Fact]
    public async Task AssistBudgetingAsync_EmitsAuditEvent()
    {
        SetupFabricResult(Array.Empty<IReadOnlyDictionary<string, string>>());

        var engine = CreateEngine();
        await engine.AssistBudgetingAsync("dept-1", new Dictionary<string, string>());

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "finance.budget.assisted"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetStatus_ReturnsCurrentStatus()
    {
        var engine = CreateEngine();
        var status = engine.GetStatus();

        status.IsActive.Should().BeTrue();
        status.AnalysesPerformed.Should().Be(0);
        status.AnomaliesDetected.Should().Be(0);
        status.SummariesGenerated.Should().Be(0);
        status.BudgetWorkflows.Should().Be(0);
        status.DataFabricQueries.Should().Be(0);
        status.StatusAsOfUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
