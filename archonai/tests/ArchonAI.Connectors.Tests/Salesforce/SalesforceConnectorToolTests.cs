using ArchonAI.Agents.Tooling.ConnectorTools;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Tooling;
using FluentAssertions;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.Salesforce;

public sealed class SalesforceConnectorToolTests
{
    private readonly ISalesforceConnector _connector = Substitute.For<ISalesforceConnector>();
    private readonly SalesforceConnectorTool _tool;

    public SalesforceConnectorToolTests()
    {
        _connector.SystemName.Returns("salesforce");
        _tool = new SalesforceConnectorTool(_connector);
    }

    [Fact]
    public void Name_Returns_ConnectorSalesforce()
    {
        _tool.Name.Should().Be("connector.salesforce");
    }

    [Fact]
    public async Task ExecuteAsync_QueryAccounts_DelegatesToConnector()
    {
        var records = new List<SalesforceRecord>
        {
            new("001A", "Account", new Dictionary<string, string> { ["Name"] = "Acme" }, DateTimeOffset.UtcNow)
        };

        _connector.QueryAccountsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(records);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "query-accounts",
            ["filter"] = "Name LIKE '%Acme%'"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["count"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_QueryContacts_DelegatesToConnector()
    {
        _connector.QueryContactsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SalesforceRecord>().ToList());

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "query-contacts"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["count"].Should().Be("0");
    }

    [Fact]
    public async Task ExecuteAsync_QueryOpportunities_DelegatesToConnector()
    {
        _connector.QueryOpportunitiesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SalesforceRecord>().ToList());

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "query-opportunities"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_CreateRecord_ReturnsRecordId()
    {
        _connector.CreateRecordAsync("Account", Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns("001NEW");

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "create-record",
            ["objectType"] = "Account",
            ["field.Name"] = "New Account"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["recordId"].Should().Be("001NEW");
        result.Outputs["operation"].Should().Be("create");
    }

    [Fact]
    public async Task ExecuteAsync_UpdateRecord_ReturnsRecordId()
    {
        _connector.UpdateRecordAsync("Account", "001A", Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns("001A");

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "update-record",
            ["objectType"] = "Account",
            ["recordId"] = "001A",
            ["field.Name"] = "Updated Name"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["operation"].Should().Be("update");
    }

    [Fact]
    public async Task ExecuteAsync_Status_ReturnsConnectorStatus()
    {
        _connector.GetStatus().Returns(new SalesforceConnectorStatus(
            true, "https://instance.salesforce.com", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(2), 100, 5, 14900, DateTimeOffset.UtcNow));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "status"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["isConnected"].Should().Be("True");
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
