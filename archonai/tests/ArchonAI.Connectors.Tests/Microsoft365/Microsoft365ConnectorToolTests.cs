using ArchonAI.Agents.Tooling.ConnectorTools;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Tooling;
using FluentAssertions;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.Microsoft365;

public sealed class Microsoft365ConnectorToolTests
{
    private readonly IMicrosoft365Connector _connector = Substitute.For<IMicrosoft365Connector>();
    private readonly Microsoft365ConnectorTool _tool;

    public Microsoft365ConnectorToolTests()
    {
        _connector.SystemName.Returns("microsoft365");
        _tool = new Microsoft365ConnectorTool(_connector);
    }

    [Fact]
    public void Name_Returns_ConnectorMicrosoft365()
    {
        _tool.Name.Should().Be("connector.microsoft365");
    }

    [Fact]
    public async Task ExecuteAsync_GetEmails_DelegatesToConnector()
    {
        var messages = new List<OutlookMessage>
        {
            new("msg1", "sender@test.com", "Hello", "Hello there", false, DateTimeOffset.UtcNow)
        };

        _connector.GetEmailsAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(messages);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-emails",
            ["filter"] = "isRead eq false",
            ["top"] = "10"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["service"].Should().Be("outlook");
        result.Outputs["count"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_SendEmail_DelegatesToConnector()
    {
        _connector.SendEmailAsync("to@test.com", "Subject", "Body", false, Arg.Any<CancellationToken>())
            .Returns(new OutlookSendResult(true, null, null));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "send-email",
            ["to"] = "to@test.com",
            ["subject"] = "Subject",
            ["body"] = "Body"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["operation"].Should().Be("send-email");
    }

    [Fact]
    public async Task ExecuteAsync_GetChannels_DelegatesToConnector()
    {
        var channels = new List<TeamsChannelInfo>
        {
            new("ch-1", "General", "Main channel", "standard")
        };

        _connector.GetTeamsChannelsAsync("team-1", Arg.Any<CancellationToken>())
            .Returns(channels);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-channels",
            ["teamId"] = "team-1"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["service"].Should().Be("teams");
        result.Outputs["count"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_SendTeamsMessage_DelegatesToConnector()
    {
        _connector.SendTeamsMessageAsync("team-1", "ch-1", "Hello!", Arg.Any<CancellationToken>())
            .Returns(new TeamsMessageResult(true, "msg-t1", null));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "send-teams-message",
            ["teamId"] = "team-1",
            ["channelId"] = "ch-1",
            ["content"] = "Hello!"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["operation"].Should().Be("send-teams-message");
        result.Outputs["messageId"].Should().Be("msg-t1");
    }

    [Fact]
    public async Task ExecuteAsync_GetTeamsMessages_DelegatesToConnector()
    {
        var messages = new List<TeamsMessage>
        {
            new("tmsg-1", "Alice", "Hey!", DateTimeOffset.UtcNow)
        };

        _connector.GetTeamsMessagesAsync("team-1", "ch-1", 20, Arg.Any<CancellationToken>())
            .Returns(messages);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-teams-messages",
            ["teamId"] = "team-1",
            ["channelId"] = "ch-1"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["service"].Should().Be("teams");
        result.Outputs["count"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_GetSharePointItems_DelegatesToConnector()
    {
        var items = new List<SharePointItem>
        {
            new("sp-1", "Doc.docx", "https://sp.test/Doc.docx", "Document", 1024, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };

        _connector.GetSharePointItemsAsync("site-1", null, 50, Arg.Any<CancellationToken>())
            .Returns(items);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-sharepoint-items",
            ["siteId"] = "site-1"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["service"].Should().Be("sharepoint");
        result.Outputs["count"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_GetSharePointItem_DelegatesToConnector()
    {
        _connector.GetSharePointItemAsync("site-1", "drive-1", "item-1", Arg.Any<CancellationToken>())
            .Returns(new SharePointItem("item-1", "Report.pdf", "https://sp.test/Report.pdf", "application/pdf", 5120, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-sharepoint-item",
            ["siteId"] = "site-1",
            ["driveId"] = "drive-1",
            ["itemId"] = "item-1"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["itemId"].Should().Be("item-1");
        result.Outputs["name"].Should().Be("Report.pdf");
    }

    [Fact]
    public async Task ExecuteAsync_ListOneDriveFiles_DelegatesToConnector()
    {
        var files = new List<OneDriveItem>
        {
            new("od-1", "Budget.xlsx", "application/vnd.ms-excel", 2048, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };

        _connector.ListOneDriveFilesAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(files);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "list-onedrive-files"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["service"].Should().Be("onedrive");
        result.Outputs["count"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_GetOneDriveItem_DelegatesToConnector()
    {
        _connector.GetOneDriveItemAsync("od-2", Arg.Any<CancellationToken>())
            .Returns(new OneDriveItem("od-2", "Photo.jpg", "image/jpeg", 4096, "folder-1", "https://od.test/Photo.jpg", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-onedrive-item",
            ["itemId"] = "od-2"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["itemId"].Should().Be("od-2");
        result.Outputs["name"].Should().Be("Photo.jpg");
        result.Outputs["mimeType"].Should().Be("image/jpeg");
        result.Outputs["sizeBytes"].Should().Be("4096");
    }

    [Fact]
    public async Task ExecuteAsync_Status_ReturnsConnectorStatus()
    {
        _connector.GetStatus().Returns(new M365ConnectorStatus(
            true, "test-tenant", "user@test.com", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30),
            200, 5, 50, 30, 20, 10, 500, DateTimeOffset.UtcNow));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "status"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["isConnected"].Should().Be("True");
        result.Outputs["tenantId"].Should().Be("test-tenant");
        result.Outputs["emailsSent"].Should().Be("50");
        result.Outputs["teamsMessagesSent"].Should().Be("30");
        result.Outputs["sharePointOps"].Should().Be("20");
        result.Outputs["oneDriveOps"].Should().Be("10");
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
