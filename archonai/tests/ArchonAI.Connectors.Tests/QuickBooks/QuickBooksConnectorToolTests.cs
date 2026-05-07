using ArchonAI.Agents.Tooling.ConnectorTools;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Tooling;
using FluentAssertions;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.QuickBooks;

public sealed class QuickBooksConnectorToolTests
{
    private readonly IQuickBooksConnector _connector = Substitute.For<IQuickBooksConnector>();
    private readonly QuickBooksConnectorTool _tool;

    public QuickBooksConnectorToolTests()
    {
        _connector.SystemName.Returns("quickbooks");
        _tool = new QuickBooksConnectorTool(_connector);
    }

    [Fact]
    public void Name_Returns_ConnectorQuickBooks()
    {
        _tool.Name.Should().Be("connector.quickbooks");
    }

    [Fact]
    public async Task ExecuteAsync_GetReports_DelegatesToConnector()
    {
        var records = new List<QuickBooksRecord>
        {
            new("0", "ProfitAndLoss", new Dictionary<string, string> { ["Amount"] = "5000" }, DateTimeOffset.UtcNow)
        };

        _connector.GetFinancialReportsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(records);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-reports",
            ["reportType"] = "ProfitAndLoss",
            ["startDate"] = "2026-01-01",
            ["endDate"] = "2026-03-01"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["reportType"].Should().Be("ProfitAndLoss");
        result.Outputs["count"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_CreateInvoice_WithMultipleLineItems()
    {
        _connector.CreateInvoiceAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<QuickBooksLineItem>>(), Arg.Any<CancellationToken>())
            .Returns("INV-001");

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "create-invoice",
            ["customerId"] = "CUST-001",
            ["line.0.description"] = "Consulting",
            ["line.0.amount"] = "150.00",
            ["line.0.quantity"] = "2",
            ["line.1.description"] = "Support",
            ["line.1.amount"] = "75.00",
            ["line.1.quantity"] = "1"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["invoiceId"].Should().Be("INV-001");
        result.Outputs["lineItemCount"].Should().Be("2");
    }

    [Fact]
    public async Task ExecuteAsync_CreateInvoice_SingleLineShorthand()
    {
        _connector.CreateInvoiceAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<QuickBooksLineItem>>(), Arg.Any<CancellationToken>())
            .Returns("INV-002");

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "create-invoice",
            ["customerId"] = "CUST-001",
            ["description"] = "Quick Service",
            ["amount"] = "200.00",
            ["quantity"] = "1"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["invoiceId"].Should().Be("INV-002");
        result.Outputs["lineItemCount"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_UpdateCustomer_DelegatesToConnector()
    {
        _connector.UpdateCustomerAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns("CUST-001");

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "update-customer",
            ["customerId"] = "CUST-001",
            ["field.DisplayName"] = "Updated Corp"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["customerId"].Should().Be("CUST-001");
        result.Outputs["operation"].Should().Be("update-customer");
    }

    [Fact]
    public async Task ExecuteAsync_GetTransactions_DelegatesToConnector()
    {
        var records = new List<QuickBooksRecord>
        {
            new("TXN-001", "Purchase", new Dictionary<string, string> { ["TotalAmt"] = "250" }, DateTimeOffset.UtcNow)
        };

        _connector.GetTransactionHistoryAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(records);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-transactions",
            ["accountId"] = "ACC-001",
            ["limit"] = "50"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["objectType"].Should().Be("Purchase");
        result.Outputs["count"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_Status_ReturnsConnectorStatus()
    {
        _connector.GetStatus().Returns(new QuickBooksConnectorStatus(
            true, "123456789", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30), 100, 5, 450, DateTimeOffset.UtcNow));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "status"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["isConnected"].Should().Be("True");
        result.Outputs["companyId"].Should().Be("123456789");
        result.Outputs["totalRequests"].Should().Be("100");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownAction_ReturnsFalse()
    {
        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "unknown-action"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeFalse();
    }
}
