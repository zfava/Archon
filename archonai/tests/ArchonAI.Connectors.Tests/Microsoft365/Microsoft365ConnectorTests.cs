using System.Net;
using System.Text.Json;
using ArchonAI.Connectors.Microsoft365;
using ArchonAI.Connectors.Tests.Shared;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.Microsoft365;

public sealed class Microsoft365ConnectorTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<Microsoft365Connector> _logger = Substitute.For<ILogger<Microsoft365Connector>>();
    private readonly Microsoft365Options _options = new()
    {
        GraphBaseUrl = "https://graph.test/v1.0",
        OAuthTokenUrl = "https://login.test/{TenantId}/oauth2/v2.0/token",
        TenantId = "test-tenant",
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        Scope = "https://graph.microsoft.com/.default",
        MaxRetries = 2,
        RetryBaseDelayMs = 10,
        HttpTimeoutSeconds = 30
    };

    private Microsoft365Connector CreateConnector()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://graph.test") };
        return new Microsoft365Connector(httpClient, _eventBus, _logger, Options.Create(_options));
    }

    [Fact]
    public void SystemName_Returns_Microsoft365()
    {
        using var connector = CreateConnector();
        connector.SystemName.Should().Be("microsoft365");
    }

    [Fact]
    public async Task AuthenticateAsync_Success_ReturnsAuthResult()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { userPrincipalName = "user@test.com" }))
        });

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeTrue();
        result.TenantId.Should().Be("test-tenant");
        result.UserPrincipalName.Should().Be("user@test.com");
        result.ExpiresAtUtc.Should().NotBeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_Failure_ReturnsError()
    {
        _handler.SetResponse(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new
        {
            error = "invalid_client",
            error_description = "Client secret is invalid"
        }));

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeFalse();
        result.Error.Should().Be("Client secret is invalid");
    }

    [Fact]
    public async Task GetEmailsAsync_ReturnsMessages()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            value = new[]
            {
                new
                {
                    id = "msg1",
                    from = new { emailAddress = new { address = "sender@test.com" } },
                    subject = "Test Subject",
                    bodyPreview = "Hello there",
                    isRead = false,
                    receivedDateTime = "2026-03-15T10:00:00Z"
                }
            }
        }));

        using var connector = CreateConnector();
        var messages = await connector.GetEmailsAsync(null, 10);

        messages.Should().HaveCount(1);
        messages[0].Id.Should().Be("msg1");
        messages[0].From.Should().Be("sender@test.com");
        messages[0].Subject.Should().Be("Test Subject");
        messages[0].BodyPreview.Should().Be("Hello there");
        messages[0].IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task SendEmailAsync_ReturnsSuccess()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { userPrincipalName = "user@test.com" })),
            (HttpStatusCode.Accepted, "")
        });

        using var connector = CreateConnector();
        var result = await connector.SendEmailAsync("recipient@test.com", "Test", "Hello!");

        result.IsSuccess.Should().BeTrue();
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "m365.outlook.email.sent"),
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
    public async Task SendEmailAsync_ThrowsOnEmptySubject()
    {
        using var connector = CreateConnector();

        var act = () => connector.SendEmailAsync("to@test.com", string.Empty, "Body");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("subject");
    }

    [Fact]
    public async Task GetTeamsChannelsAsync_ReturnsChannels()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            value = new[]
            {
                new { id = "ch-1", displayName = "General", description = "Main channel", membershipType = "standard" },
                new { id = "ch-2", displayName = "Dev", description = "Dev channel", membershipType = "private" }
            }
        }));

        using var connector = CreateConnector();
        var channels = await connector.GetTeamsChannelsAsync("team-123");

        channels.Should().HaveCount(2);
        channels[0].Id.Should().Be("ch-1");
        channels[0].DisplayName.Should().Be("General");
        channels[1].MembershipType.Should().Be("private");
    }

    [Fact]
    public async Task GetTeamsChannelsAsync_ThrowsOnEmptyTeamId()
    {
        using var connector = CreateConnector();

        var act = () => connector.GetTeamsChannelsAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("teamId");
    }

    [Fact]
    public async Task SendTeamsMessageAsync_ReturnsResult()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new { id = "msg-teams-001" }));

        using var connector = CreateConnector();
        var result = await connector.SendTeamsMessageAsync("team-1", "ch-1", "Hello Teams!");

        result.IsSuccess.Should().BeTrue();
        result.MessageId.Should().Be("msg-teams-001");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "m365.teams.message.sent"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendTeamsMessageAsync_ThrowsOnEmptyContent()
    {
        using var connector = CreateConnector();

        var act = () => connector.SendTeamsMessageAsync("team-1", "ch-1", string.Empty);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("content");
    }

    [Fact]
    public async Task GetTeamsMessagesAsync_ReturnsMessages()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            value = new[]
            {
                new
                {
                    id = "tmsg-1",
                    from = new { user = new { displayName = "Alice" } },
                    body = new { content = "Hey team!" },
                    createdDateTime = "2026-03-15T12:00:00Z"
                }
            }
        }));

        using var connector = CreateConnector();
        var messages = await connector.GetTeamsMessagesAsync("team-1", "ch-1", 10);

        messages.Should().HaveCount(1);
        messages[0].Id.Should().Be("tmsg-1");
        messages[0].FromDisplayName.Should().Be("Alice");
        messages[0].BodyContent.Should().Be("Hey team!");
    }

    [Fact]
    public async Task GetSharePointItemsAsync_ReturnsItems()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            value = new[]
            {
                new
                {
                    id = "sp-item-1",
                    name = "Report.docx",
                    webUrl = "https://sharepoint.test/Report.docx",
                    contentType = new { name = "Document" },
                    size = 10240,
                    lastModifiedDateTime = "2026-03-10T08:00:00Z"
                }
            }
        }));

        using var connector = CreateConnector();
        var items = await connector.GetSharePointItemsAsync("site-1", "list-1", 50);

        items.Should().HaveCount(1);
        items[0].Id.Should().Be("sp-item-1");
        items[0].Name.Should().Be("Report.docx");
        items[0].ContentType.Should().Be("Document");
        items[0].SizeBytes.Should().Be(10240);
    }

    [Fact]
    public async Task GetSharePointItemsAsync_ThrowsOnEmptySiteId()
    {
        using var connector = CreateConnector();

        var act = () => connector.GetSharePointItemsAsync(string.Empty, null, 50);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("siteId");
    }

    [Fact]
    public async Task GetSharePointItemAsync_ReturnsItem()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            id = "sp-item-2",
            name = "Presentation.pptx",
            webUrl = "https://sharepoint.test/Presentation.pptx",
            file = new { mimeType = "application/vnd.ms-powerpoint" },
            size = 51200,
            lastModifiedDateTime = "2026-03-12T14:00:00Z"
        }));

        using var connector = CreateConnector();
        var item = await connector.GetSharePointItemAsync("site-1", "drive-1", "sp-item-2");

        item.Id.Should().Be("sp-item-2");
        item.Name.Should().Be("Presentation.pptx");
        item.SizeBytes.Should().Be(51200);
    }

    [Fact]
    public async Task ListOneDriveFilesAsync_ReturnsFiles()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            value = new[]
            {
                new
                {
                    id = "od-1",
                    name = "Budget.xlsx",
                    file = new { mimeType = "application/vnd.ms-excel" },
                    size = 2048,
                    parentReference = new { id = "folder-root" },
                    webUrl = "https://onedrive.test/Budget.xlsx",
                    lastModifiedDateTime = "2026-03-14T16:00:00Z"
                }
            }
        }));

        using var connector = CreateConnector();
        var files = await connector.ListOneDriveFilesAsync(null, 50);

        files.Should().HaveCount(1);
        files[0].Id.Should().Be("od-1");
        files[0].Name.Should().Be("Budget.xlsx");
        files[0].MimeType.Should().Be("application/vnd.ms-excel");
        files[0].SizeBytes.Should().Be(2048);
    }

    [Fact]
    public async Task GetOneDriveItemAsync_ReturnsItem()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            id = "od-2",
            name = "Photo.jpg",
            file = new { mimeType = "image/jpeg" },
            size = 4096,
            parentReference = new { id = "folder-pics" },
            webUrl = "https://onedrive.test/Photo.jpg",
            lastModifiedDateTime = "2026-03-15T09:00:00Z"
        }));

        using var connector = CreateConnector();
        var item = await connector.GetOneDriveItemAsync("od-2");

        item.Id.Should().Be("od-2");
        item.Name.Should().Be("Photo.jpg");
        item.MimeType.Should().Be("image/jpeg");
        item.SizeBytes.Should().Be(4096);
        item.ParentId.Should().Be("folder-pics");
    }

    [Fact]
    public async Task GetOneDriveItemAsync_ThrowsOnEmptyId()
    {
        using var connector = CreateConnector();

        var act = () => connector.GetOneDriveItemAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("itemId");
    }

    [Fact]
    public void GetStatus_ReturnsCurrentStatus()
    {
        using var connector = CreateConnector();
        var status = connector.GetStatus();

        status.IsConnected.Should().BeFalse();
        status.TenantId.Should().Be("test-tenant");
        status.TotalRequests.Should().Be(0);
        status.FailedRequests.Should().Be(0);
        status.EmailsSent.Should().Be(0);
        status.TeamsMessagesSent.Should().Be(0);
        status.SharePointOps.Should().Be(0);
        status.OneDriveOps.Should().Be(0);
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
            Arg.Is<SystemEvent>(e => e.EventType == "m365.result.pushed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryLogic_RetriesOnServiceUnavailable()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { userPrincipalName = "user@test.com" })),
            (HttpStatusCode.ServiceUnavailable, ""),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { value = Array.Empty<object>() }))
        });

        using var connector = CreateConnector();
        var files = await connector.ListOneDriveFilesAsync(null, 10);

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
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { userPrincipalName = "user@test.com" })),
            (HttpStatusCode.OK, apiResponseJson)
        });
    }
}
