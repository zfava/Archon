using System.Net;
using System.Text.Json;
using ArchonAI.Connectors.GoogleWorkspace;
using ArchonAI.Connectors.Tests.Shared;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.GoogleWorkspace;

public sealed class GoogleWorkspaceConnectorTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<GoogleWorkspaceConnector> _logger = Substitute.For<ILogger<GoogleWorkspaceConnector>>();
    private readonly GoogleWorkspaceOptions _options = new()
    {
        OAuthTokenUrl = "https://oauth2.test/token",
        GmailBaseUrl = "https://gmail.test/gmail/v1",
        DocsBaseUrl = "https://docs.test/v1",
        SheetsBaseUrl = "https://sheets.test/v4",
        DriveBaseUrl = "https://drive.test/drive/v3",
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        RefreshToken = "test-refresh-token",
        MaxRetries = 2,
        RetryBaseDelayMs = 10
    };

    private GoogleWorkspaceConnector CreateConnector()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://google.test") };
        return new GoogleWorkspaceConnector(httpClient, _eventBus, _logger, Options.Create(_options));
    }

    [Fact]
    public void SystemName_Returns_GoogleWorkspace()
    {
        using var connector = CreateConnector();
        connector.SystemName.Should().Be("google-workspace");
    }

    [Fact]
    public async Task AuthenticateAsync_Success_ReturnsAuthResult()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { email = "user@test.com" }))
        });

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeTrue();
        result.Email.Should().Be("user@test.com");
        result.ExpiresAtUtc.Should().NotBeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_Failure_ReturnsError()
    {
        _handler.SetResponse(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new
        {
            error = "invalid_grant",
            error_description = "Token has been revoked"
        }));

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeFalse();
        result.Error.Should().Be("Token has been revoked");
    }

    [Fact]
    public async Task GetEmailsAsync_ReturnsMessages()
    {
        _handler.SetResponseSequence(new[]
        {
            // Auth
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { email = "user@test.com" })),
            // List messages
            (HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                messages = new[] { new { id = "msg1", threadId = "thread1" } }
            })),
            // Get message detail
            (HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                id = "msg1",
                threadId = "thread1",
                snippet = "Hello there",
                internalDate = "1710000000000",
                payload = new
                {
                    headers = new[]
                    {
                        new { name = "From", value = "sender@test.com" },
                        new { name = "To", value = "user@test.com" },
                        new { name = "Subject", value = "Test Subject" }
                    }
                }
            }))
        });

        using var connector = CreateConnector();
        var messages = await connector.GetEmailsAsync("is:unread", 10);

        messages.Should().HaveCount(1);
        messages[0].Id.Should().Be("msg1");
        messages[0].From.Should().Be("sender@test.com");
        messages[0].Subject.Should().Be("Test Subject");
        messages[0].Snippet.Should().Be("Hello there");
    }

    [Fact]
    public async Task SendEmailAsync_ReturnsResult()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            id = "msg-new-001",
            threadId = "thread-new-001"
        }));

        using var connector = CreateConnector();
        var result = await connector.SendEmailAsync("recipient@test.com", "Test", "Hello!");

        result.IsSuccess.Should().BeTrue();
        result.MessageId.Should().Be("msg-new-001");
        result.ThreadId.Should().Be("thread-new-001");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "google.gmail.sent"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendEmailAsync_ThrowsOnEmptyRecipient()
    {
        using var connector = CreateConnector();

        var act = () => connector.SendEmailAsync(string.Empty, "Subject", "Body");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("to");
    }

    [Fact]
    public async Task GetDocumentAsync_ReturnsDocument()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            documentId = "doc-123",
            title = "Test Document",
            body = new
            {
                content = new[]
                {
                    new
                    {
                        paragraph = new
                        {
                            elements = new[]
                            {
                                new { textRun = new { content = "Hello World" } }
                            }
                        }
                    }
                }
            }
        }));

        using var connector = CreateConnector();
        var doc = await connector.GetDocumentAsync("doc-123");

        doc.DocumentId.Should().Be("doc-123");
        doc.Title.Should().Be("Test Document");
        doc.BodyText.Should().Be("Hello World");
    }

    [Fact]
    public async Task GetDocumentAsync_ThrowsOnEmptyId()
    {
        using var connector = CreateConnector();

        var act = () => connector.GetDocumentAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("documentId");
    }

    [Fact]
    public async Task CreateDocumentAsync_ReturnsDocumentId()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { email = "user@test.com" })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { documentId = "doc-new-001" })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { replies = Array.Empty<object>() }))
        });

        using var connector = CreateConnector();
        string docId = await connector.CreateDocumentAsync("New Doc", "Some content");

        docId.Should().Be("doc-new-001");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "google.docs.created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadSpreadsheetAsync_ReturnsData()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            range = "Sheet1!A1:B3",
            values = new[]
            {
                new[] { "Name", "Age" },
                new[] { "Alice", "30" },
                new[] { "Bob", "25" }
            }
        }));

        using var connector = CreateConnector();
        var data = await connector.ReadSpreadsheetAsync("sheet-123", "Sheet1!A1:B3");

        data.SpreadsheetId.Should().Be("sheet-123");
        data.Range.Should().Be("Sheet1!A1:B3");
        data.RowCount.Should().Be(3);
        data.ColumnCount.Should().Be(2);
        data.Values[0][0].Should().Be("Name");
        data.Values[1][0].Should().Be("Alice");
    }

    [Fact]
    public async Task ReadSpreadsheetAsync_ThrowsOnEmptyId()
    {
        using var connector = CreateConnector();

        var act = () => connector.ReadSpreadsheetAsync(string.Empty, "A1:B2");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("spreadsheetId");
    }

    [Fact]
    public async Task WriteSpreadsheetAsync_ReturnsUpdatedCells()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            updatedCells = 6
        }));

        using var connector = CreateConnector();
        var values = new List<IReadOnlyList<string>>
        {
            new List<string> { "Name", "Age" },
            new List<string> { "Charlie", "35" }
        };
        int updated = await connector.WriteSpreadsheetAsync("sheet-123", "Sheet1!A1:B2", values);

        updated.Should().Be(6);
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "google.sheets.updated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteSpreadsheetAsync_ThrowsOnEmptyValues()
    {
        using var connector = CreateConnector();

        var act = () => connector.WriteSpreadsheetAsync("sheet-123", "A1", new List<IReadOnlyList<string>>());

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("values");
    }

    [Fact]
    public async Task ListFilesAsync_ReturnsFiles()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            files = new[]
            {
                new { id = "file-1", name = "Report.pdf", mimeType = "application/pdf", size = "1024", modifiedTime = "2026-03-01T10:00:00Z" },
                new { id = "file-2", name = "Data.xlsx", mimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", size = "2048", modifiedTime = "2026-03-10T14:30:00Z" }
            }
        }));

        using var connector = CreateConnector();
        var files = await connector.ListFilesAsync(null, 50);

        files.Should().HaveCount(2);
        files[0].Id.Should().Be("file-1");
        files[0].Name.Should().Be("Report.pdf");
        files[0].MimeType.Should().Be("application/pdf");
        files[0].SizeBytes.Should().Be(1024);
    }

    [Fact]
    public async Task GetFileMetadataAsync_ReturnsFileInfo()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            id = "file-1",
            name = "Report.pdf",
            mimeType = "application/pdf",
            size = "4096",
            parents = new[] { "folder-1" },
            modifiedTime = "2026-03-15T08:00:00Z"
        }));

        using var connector = CreateConnector();
        var file = await connector.GetFileMetadataAsync("file-1");

        file.Id.Should().Be("file-1");
        file.Name.Should().Be("Report.pdf");
        file.SizeBytes.Should().Be(4096);
        file.ParentId.Should().Be("folder-1");
    }

    [Fact]
    public async Task GetFileMetadataAsync_ThrowsOnEmptyId()
    {
        using var connector = CreateConnector();

        var act = () => connector.GetFileMetadataAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("fileId");
    }

    [Fact]
    public void GetStatus_ReturnsCurrentStatus()
    {
        using var connector = CreateConnector();
        var status = connector.GetStatus();

        status.IsConnected.Should().BeFalse();
        status.TotalRequests.Should().Be(0);
        status.EmailsSent.Should().Be(0);
        status.DocsAccessed.Should().Be(0);
        status.SheetsAccessed.Should().Be(0);
        status.DriveOps.Should().Be(0);
        status.StatusAsOfUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PushResultAsync_EmitsAuditEvent()
    {
        using var connector = CreateConnector();
        var executionResult = new ExecutionResult(
            Guid.NewGuid(), true, "Test", new Dictionary<string, string>(),
            Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);

        await connector.PushResultAsync(executionResult);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "google.workspace.result.pushed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryLogic_RetriesOnServiceUnavailable()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { email = "user@test.com" })),
            (HttpStatusCode.ServiceUnavailable, ""),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { files = Array.Empty<object>() }))
        });

        using var connector = CreateConnector();
        var files = await connector.ListFilesAsync(null, 10);

        files.Should().BeEmpty();
        _handler.RequestCount.Should().Be(4); // auth + userinfo + retry + success
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    private void SetupAuthAndResponse(string apiResponseJson)
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { email = "user@test.com" })),
            (HttpStatusCode.OK, apiResponseJson)
        });
    }
}
