using ArchonAI.Agents.Tooling.ConnectorTools;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Tooling;
using FluentAssertions;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.HubSpot;

public sealed class HubSpotConnectorToolTests
{
    private readonly IHubSpotConnector _connector = Substitute.For<IHubSpotConnector>();
    private readonly HubSpotConnectorTool _tool;

    public HubSpotConnectorToolTests()
    {
        _connector.SystemName.Returns("hubspot");
        _tool = new HubSpotConnectorTool(_connector);
    }

    [Fact]
    public void Name_Returns_ConnectorHubSpot()
    {
        _tool.Name.Should().Be("connector.hubspot");
    }

    [Fact]
    public async Task ExecuteAsync_GetContacts_DelegatesToConnector()
    {
        var records = new List<HubSpotRecord>
        {
            new("101", "Contact", new Dictionary<string, string> { ["firstname"] = "Jane" }, DateTimeOffset.UtcNow)
        };

        _connector.GetContactsAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(records);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-contacts",
            ["filter"] = "email,firstname"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["count"].Should().Be("1");
        result.Outputs["objectType"].Should().Be("Contact");
    }

    [Fact]
    public async Task ExecuteAsync_GetDeals_DelegatesToConnector()
    {
        _connector.GetDealsAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<HubSpotRecord>().ToList());

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-deals"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["count"].Should().Be("0");
    }

    [Fact]
    public async Task ExecuteAsync_UpdatePipeline_ReturnsRecordId()
    {
        _connector.UpdatePipelineRecordAsync("deals", "201", Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns("201");

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "update-pipeline",
            ["objectType"] = "deals",
            ["recordId"] = "201",
            ["field.dealstage"] = "contractsent"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["operation"].Should().Be("update-pipeline");
        result.Outputs["recordId"].Should().Be("201");
    }

    [Fact]
    public async Task ExecuteAsync_CreateRecord_ReturnsRecordId()
    {
        _connector.CreateRecordAsync("contacts", Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns("301");

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "create-record",
            ["objectType"] = "contacts",
            ["field.firstname"] = "New",
            ["field.lastname"] = "Contact"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["recordId"].Should().Be("301");
        result.Outputs["operation"].Should().Be("create");
    }

    [Fact]
    public async Task ExecuteAsync_Status_ReturnsConnectorStatus()
    {
        _connector.GetStatus().Returns(new HubSpotConnectorStatus(
            true, "12345678", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30), 50, 2, 240000, DateTimeOffset.UtcNow));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "status"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["isConnected"].Should().Be("True");
        result.Outputs["portalId"].Should().Be("12345678");
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
