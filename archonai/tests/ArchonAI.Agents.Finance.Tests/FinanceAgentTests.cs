using ArchonAI.Agents.Finance;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Finance;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Agents.Finance.Tests;

public sealed class FinanceAgentTests
{
    private readonly IFinanceEngine _financeEngine = Substitute.For<IFinanceEngine>();
    private readonly IModelProvider _modelProvider = Substitute.For<IModelProvider>();
    private readonly ILogger<FinanceAgent> _logger = Substitute.For<ILogger<FinanceAgent>>();

    private FinanceAgent CreateAgent() => new(_financeEngine, _modelProvider, _logger);

    private static CoreTask CreateTask(string capability, Dictionary<string, string>? inputs = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, "TestTask", "Test",
        capability, inputs ?? new Dictionary<string, string>(),
        DateTimeOffset.UtcNow, null, null);

    private static CoreExecutionContext CreateContext() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "test-tenant", new Dictionary<string, string>(), DateTimeOffset.UtcNow);

    [Fact]
    public void Describe_ReturnsFinanceAgent()
    {
        var agent = CreateAgent();
        var desc = agent.Describe();

        desc.Name.Should().Be("FinanceAgent");
        desc.Version.Should().Be("1.0.0");
        desc.Capabilities.Should().HaveCount(4);
        desc.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_FinancialAnalysis_DelegatesToEngine()
    {
        _financeEngine.AnalyzePerformanceAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new FinancialAnalysisResult(
                true, "*", 0.1, 0.05, 0.3,
                new[] { "Growth detected" }, Array.Empty<string>(),
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("financial-analysis"), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["profitMargin"].Should().Be("0.3000");
    }

    [Fact]
    public async Task ExecuteAsync_AnomalyDetection_DelegatesToEngine()
    {
        _financeEngine.DetectAnomaliesAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<FinancialAnomaly>
            {
                new(Guid.NewGuid(), "unusually-high", "Spike", "Unusual", 0.9, 0.5, "marketing",
                    new Dictionary<string, string>(), DateTimeOffset.UtcNow)
            });

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("anomaly-detection", new Dictionary<string, string> { ["scope"] = "crm" }), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["anomalyCount"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_FinancialSummary_DelegatesToEngine()
    {
        _financeEngine.GenerateSummaryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FinancialSummary(
                "*", "Q1", 100000, 60000, 40000, 0.4,
                Array.Empty<FinancialLineItem>(), Array.Empty<FinancialLineItem>(),
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("financial-summary"), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["totalRevenue"].Should().Be("100000.00");
        result.Outputs["netIncome"].Should().Be("40000.00");
    }

    [Fact]
    public async Task ExecuteAsync_BudgetAssistance_DelegatesToEngine()
    {
        _financeEngine.AssistBudgetingAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new BudgetWorkflowResult(
                Guid.NewGuid(), true, "eng", 10000, 10500, 0.05,
                Array.Empty<BudgetRecommendation>(),
                new Dictionary<string, string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(
            CreateTask("budget-assistance", new Dictionary<string, string> { ["departmentId"] = "eng" }),
            CreateContext());

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCapability_UsesModel()
    {
        _modelProvider.GenerateAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse(
                "test", "model", true, "Financial insight",
                Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow));

        var agent = CreateAgent();
        var result = await agent.ExecuteAsync(CreateTask("custom-finance"), CreateContext());

        result.IsSuccess.Should().BeTrue();
        result.Outputs["content"].Should().Be("Financial insight");
    }
}
