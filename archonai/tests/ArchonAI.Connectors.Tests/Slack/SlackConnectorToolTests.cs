using ArchonAI.Agents.Tooling.ConnectorTools;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Tooling;
using FluentAssertions;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.Slack;

public sealed class SlackConnectorToolTests
{
    private readonly ISlackConnector _connector = Substitute.For<ISlackConnector>();
    private readonly SlackConnectorTool _tool;

    public SlackConnectorToolTests()
    {
        _connector.SystemName.Returns("slack");
        _tool = new SlackConnectorTool(_connector);
    }

    [Fact]
    public void Name_Returns_ConnectorSlack()
    {
        _tool.Name.Should().Be("connector.slack");
    }

    [Fact]
    public async Task ExecuteAsync_SendMessage_DelegatesToConnector()
    {
        _connector.SendMessageAsync("C001", "Hello!", null, Arg.Any<CancellationToken>())
            .Returns(new SlackMessageResult(true, "C001", "1234567890.000001", null));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "send-message",
            ["channel"] = "C001",
            ["text"] = "Hello!"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["channel"].Should().Be("C001");
        result.Outputs["timestamp"].Should().Be("1234567890.000001");
        result.Outputs["operation"].Should().Be("send-message");
    }

    [Fact]
    public async Task ExecuteAsync_SendMessage_WithThread()
    {
        _connector.SendMessageAsync("C001", "Reply", "1234567890.000001", Arg.Any<CancellationToken>())
            .Returns(new SlackMessageResult(true, "C001", "1234567890.000002", null));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "send-message",
            ["channel"] = "C001",
            ["text"] = "Reply",
            ["threadTs"] = "1234567890.000001"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ReadChannel_DelegatesToConnector()
    {
        var messages = new List<SlackMessage>
        {
            new("1710000000.000001", "U001", "Hello!", null, DateTimeOffset.UtcNow)
        };

        _connector.ReadChannelHistoryAsync("C001", 50, null, null, Arg.Any<CancellationToken>())
            .Returns(messages);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "read-channel",
            ["channel"] = "C001"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["channel"].Should().Be("C001");
        result.Outputs["count"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_GetChannels_DelegatesToConnector()
    {
        var channels = new List<SlackChannelInfo>
        {
            new("C001", "general", false, 42, "General", "Company chat"),
            new("C002", "dev", true, 8, null, null)
        };

        _connector.GetChannelsAsync(100, Arg.Any<CancellationToken>())
            .Returns(channels);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-channels"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["count"].Should().Be("2");
    }

    [Fact]
    public async Task ExecuteAsync_PostAlert_DelegatesToConnector()
    {
        _connector.PostAlertAsync("#alerts", "critical", "Server Down", "Production unresponsive", Arg.Any<CancellationToken>())
            .Returns(new SlackMessageResult(true, "#alerts", "1234567890.999999", null));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "post-alert",
            ["channel"] = "#alerts",
            ["alertLevel"] = "critical",
            ["title"] = "Server Down",
            ["details"] = "Production unresponsive"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["alertLevel"].Should().Be("critical");
        result.Outputs["operation"].Should().Be("post-alert");
    }

    [Fact]
    public async Task ExecuteAsync_Status_ReturnsConnectorStatus()
    {
        _connector.GetStatus().Returns(new SlackConnectorStatus(
            true, "T12345", "TestTeam", DateTimeOffset.UtcNow,
            150, 500, 3, 42, 1000, DateTimeOffset.UtcNow));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "status"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["isConnected"].Should().Be("True");
        result.Outputs["teamId"].Should().Be("T12345");
        result.Outputs["teamName"].Should().Be("TestTeam");
        result.Outputs["totalMessagesSent"].Should().Be("150");
        result.Outputs["webhookEventsProcessed"].Should().Be("42");
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
