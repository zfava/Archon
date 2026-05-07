using ArchonAI.Agents.Tooling.ConnectorTools;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Tooling;
using FluentAssertions;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.GoogleWorkspace;

public sealed class GoogleWorkspaceConnectorToolTests
{
    private readonly IGoogleWorkspaceConnector _connector = Substitute.For<IGoogleWorkspaceConnector>();
    private readonly GoogleWorkspaceConnectorTool _tool;

    public GoogleWorkspaceConnectorToolTests()
    {
        _connector.SystemName.Returns("google-workspace");
        _tool = new GoogleWorkspaceConnectorTool(_connector);
    }

    [Fact]
    public void Name_Returns_ConnectorGoogleWorkspace()
    {
        _tool.Name.Should().Be("connector.google-workspace");
    }

    [Fact]
    public async Task ExecuteAsync_GetEmails_DelegatesToConnector()
    {
        var messages = new List<GmailMessage>
        {
            new("msg1", "thread1", "sender@test.com", "user@test.com", "Hello", "Hello there", DateTimeOffset.UtcNow)
        };

        _connector.GetEmailsAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(messages);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-emails",
            ["query"] = "is:unread",
            ["maxResults"] = "10"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["service"].Should().Be("gmail");
        result.Outputs["count"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_SendEmail_DelegatesToConnector()
    {
        _connector.SendEmailAsync("to@test.com", "Subject", "Body", false, Arg.Any<CancellationToken>())
            .Returns(new GmailSendResult(true, "msg-new", "thread-new", null));

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
        result.Outputs["messageId"].Should().Be("msg-new");
    }

    [Fact]
    public async Task ExecuteAsync_GetDocument_DelegatesToConnector()
    {
        _connector.GetDocumentAsync("doc-123", Arg.Any<CancellationToken>())
            .Returns(new GoogleDocument("doc-123", "Test Doc", "Hello World", DateTimeOffset.UtcNow));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-document",
            ["documentId"] = "doc-123"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["service"].Should().Be("docs");
        result.Outputs["documentId"].Should().Be("doc-123");
        result.Outputs["title"].Should().Be("Test Doc");
    }

    [Fact]
    public async Task ExecuteAsync_CreateDocument_DelegatesToConnector()
    {
        _connector.CreateDocumentAsync("New Doc", "Content here", Arg.Any<CancellationToken>())
            .Returns("doc-new-001");

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "create-document",
            ["title"] = "New Doc",
            ["content"] = "Content here"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["operation"].Should().Be("create-document");
        result.Outputs["documentId"].Should().Be("doc-new-001");
    }

    [Fact]
    public async Task ExecuteAsync_ReadSpreadsheet_DelegatesToConnector()
    {
        var data = new GoogleSheetData("sheet-123", "Sheet1!A1:B2",
            new List<IReadOnlyList<string>> { new List<string> { "A", "B" } },
            1, 2, DateTimeOffset.UtcNow);

        _connector.ReadSpreadsheetAsync("sheet-123", "Sheet1!A1:B2", Arg.Any<CancellationToken>())
            .Returns(data);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "read-spreadsheet",
            ["spreadsheetId"] = "sheet-123",
            ["range"] = "Sheet1!A1:B2"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["service"].Should().Be("sheets");
        result.Outputs["rowCount"].Should().Be("1");
        result.Outputs["columnCount"].Should().Be("2");
    }

    [Fact]
    public async Task ExecuteAsync_WriteSpreadsheet_WithRowData()
    {
        _connector.WriteSpreadsheetAsync("sheet-123", "Sheet1!A1", Arg.Any<IReadOnlyList<IReadOnlyList<string>>>(), Arg.Any<CancellationToken>())
            .Returns(4);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "write-spreadsheet",
            ["spreadsheetId"] = "sheet-123",
            ["range"] = "Sheet1!A1",
            ["row.0.0"] = "Name",
            ["row.0.1"] = "Age",
            ["row.1.0"] = "Alice",
            ["row.1.1"] = "30"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["operation"].Should().Be("write-spreadsheet");
        result.Outputs["updatedCells"].Should().Be("4");
    }

    [Fact]
    public async Task ExecuteAsync_ListFiles_DelegatesToConnector()
    {
        var files = new List<DriveFileInfo>
        {
            new("file-1", "Report.pdf", "application/pdf", 1024, null, null, DateTimeOffset.UtcNow),
            new("file-2", "Data.xlsx", "application/vnd.ms-excel", 2048, "folder-1", null, DateTimeOffset.UtcNow)
        };

        _connector.ListFilesAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(files);

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "list-files",
            ["query"] = "mimeType='application/pdf'"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["service"].Should().Be("drive");
        result.Outputs["count"].Should().Be("2");
    }

    [Fact]
    public async Task ExecuteAsync_GetFile_DelegatesToConnector()
    {
        _connector.GetFileMetadataAsync("file-1", Arg.Any<CancellationToken>())
            .Returns(new DriveFileInfo("file-1", "Report.pdf", "application/pdf", 4096, "folder-1", null, DateTimeOffset.UtcNow));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "get-file",
            ["fileId"] = "file-1"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["fileId"].Should().Be("file-1");
        result.Outputs["fileName"].Should().Be("Report.pdf");
        result.Outputs["sizeBytes"].Should().Be("4096");
    }

    [Fact]
    public async Task ExecuteAsync_Status_ReturnsConnectorStatus()
    {
        _connector.GetStatus().Returns(new GoogleWorkspaceConnectorStatus(
            true, "user@test.com", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30),
            200, 5, 50, 30, 20, 100, 500, DateTimeOffset.UtcNow));

        var request = new ToolExecutionRequest(_tool.Name, new Dictionary<string, string>
        {
            ["action"] = "status"
        }, "test-user", DateTimeOffset.UtcNow);

        var result = await _tool.ExecuteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Outputs["isConnected"].Should().Be("True");
        result.Outputs["email"].Should().Be("user@test.com");
        result.Outputs["emailsSent"].Should().Be("50");
        result.Outputs["docsAccessed"].Should().Be("30");
        result.Outputs["sheetsAccessed"].Should().Be("20");
        result.Outputs["driveOps"].Should().Be("100");
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
