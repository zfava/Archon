using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchonAI.Connectors.Slack;
using ArchonAI.Connectors.Tests.Shared;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.Slack;

public sealed class SlackConnectorTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<SlackConnector> _logger = Substitute.For<ILogger<SlackConnector>>();
    private readonly SlackOptions _options = new()
    {
        BaseUrl = "https://slack.api.test",
        BotToken = "xoxb-test-token",
        SigningSecret = "test-signing-secret",
        MaxRetries = 2,
        RetryBaseDelayMs = 10,
        DefaultAlertChannel = "#alerts"
    };

    private SlackConnector CreateConnector()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://slack.api.test") };
        return new SlackConnector(httpClient, _eventBus, _logger, Options.Create(_options));
    }

    [Fact]
    public void SystemName_Returns_Slack()
    {
        using var connector = CreateConnector();
        connector.SystemName.Should().Be("slack");
    }

    [Fact]
    public async Task AuthenticateAsync_Success_ReturnsAuthResult()
    {
        _handler.SetResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            ok = true,
            team_id = "T12345",
            team = "TestTeam",
            user_id = "U12345"
        }));

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeTrue();
        result.TeamId.Should().Be("T12345");
        result.TeamName.Should().Be("TestTeam");
        result.BotUserId.Should().Be("U12345");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_Failure_ReturnsError()
    {
        _handler.SetResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            ok = false,
            error = "invalid_auth"
        }));

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeFalse();
        result.Error.Should().Be("invalid_auth");
    }

    [Fact]
    public async Task SendMessageAsync_Success_ReturnsTimestamp()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            ok = true,
            channel = "C12345",
            ts = "1234567890.123456"
        }));

        using var connector = CreateConnector();
        var result = await connector.SendMessageAsync("C12345", "Hello world!");

        result.IsSuccess.Should().BeTrue();
        result.Channel.Should().Be("C12345");
        result.Timestamp.Should().Be("1234567890.123456");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "slack.message.sent"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsOnEmptyChannel()
    {
        using var connector = CreateConnector();

        var act = () => connector.SendMessageAsync(string.Empty, "Hello");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("channel");
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsOnEmptyText()
    {
        using var connector = CreateConnector();

        var act = () => connector.SendMessageAsync("C12345", string.Empty);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("text");
    }

    [Fact]
    public async Task GetChannelsAsync_ReturnsChannels()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            ok = true,
            channels = new[]
            {
                new { id = "C001", name = "general", is_private = false, num_members = 42,
                    topic = new { value = "General discussion" },
                    purpose = new { value = "Company-wide chat" } },
                new { id = "C002", name = "dev-team", is_private = true, num_members = 8,
                    topic = new { value = "Dev stuff" },
                    purpose = new { value = "Engineering" } }
            }
        }));

        using var connector = CreateConnector();
        var channels = await connector.GetChannelsAsync(100);

        channels.Should().HaveCount(2);
        channels[0].Id.Should().Be("C001");
        channels[0].Name.Should().Be("general");
        channels[0].IsPrivate.Should().BeFalse();
        channels[0].MemberCount.Should().Be(42);
        channels[1].IsPrivate.Should().BeTrue();
    }

    [Fact]
    public async Task ReadChannelHistoryAsync_ReturnsMessages()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            ok = true,
            messages = new[]
            {
                new { ts = "1710000000.000001", user = "U001", text = "Hello!", thread_ts = (string?)null },
                new { ts = "1710000001.000002", user = "U002", text = "World!", thread_ts = (string?)"1710000000.000001" }
            }
        }));

        using var connector = CreateConnector();
        var messages = await connector.ReadChannelHistoryAsync("C12345", 50);

        messages.Should().HaveCount(2);
        messages[0].Ts.Should().Be("1710000000.000001");
        messages[0].User.Should().Be("U001");
        messages[0].Text.Should().Be("Hello!");
        messages[1].ThreadTs.Should().Be("1710000000.000001");
    }

    [Fact]
    public async Task ReadChannelHistoryAsync_ThrowsOnEmptyChannel()
    {
        using var connector = CreateConnector();

        var act = () => connector.ReadChannelHistoryAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("channel");
    }

    [Fact]
    public async Task PostAlertAsync_FormatsAndSendsAlert()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            ok = true,
            channel = "#alerts",
            ts = "1234567890.999999"
        }));

        using var connector = CreateConnector();
        var result = await connector.PostAlertAsync("#alerts", "critical", "Server Down", "Production server is unresponsive");

        result.IsSuccess.Should().BeTrue();
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "slack.alert.posted"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostAlertAsync_UsesDefaultChannel()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            ok = true,
            channel = "#alerts",
            ts = "1234567890.111111"
        }));

        using var connector = CreateConnector();
        var result = await connector.PostAlertAsync(string.Empty, "info", "Update", "System updated");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_ValidSignature_ReturnsTrue()
    {
        string body = JsonSerializer.Serialize(new
        {
            type = "event_callback",
            @event = new { type = "message", channel = "C001", user = "U001", text = "Hello" }
        });

        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string signature = ComputeSlackSignature(body, timestamp);

        using var connector = CreateConnector();
        bool result = await connector.ProcessWebhookEventAsync(body, signature, timestamp);

        result.Should().BeTrue();
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "slack.event.message"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_InvalidSignature_ReturnsFalse()
    {
        string body = JsonSerializer.Serialize(new { type = "event_callback" });
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        using var connector = CreateConnector();
        bool result = await connector.ProcessWebhookEventAsync(body, "v0=invalidsignature", timestamp);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessWebhookEventAsync_UrlVerification_ReturnsTrue()
    {
        string body = JsonSerializer.Serialize(new
        {
            type = "url_verification",
            challenge = "test-challenge-token"
        });

        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string signature = ComputeSlackSignature(body, timestamp);

        using var connector = CreateConnector();
        bool result = await connector.ProcessWebhookEventAsync(body, signature, timestamp);

        result.Should().BeTrue();
    }

    [Fact]
    public void GetStatus_ReturnsCurrentStatus()
    {
        using var connector = CreateConnector();
        var status = connector.GetStatus();

        status.IsConnected.Should().BeFalse();
        status.TotalMessagesSent.Should().Be(0);
        status.TotalMessagesRead.Should().Be(0);
        status.WebhookEventsProcessed.Should().Be(0);
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
            Arg.Is<SystemEvent>(e => e.EventType == "slack.result.pushed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryLogic_RetriesOnServiceUnavailable()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { ok = true, team_id = "T1", team = "Test", user_id = "U1" })),
            (HttpStatusCode.ServiceUnavailable, ""),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { ok = true, channels = Array.Empty<object>() }))
        });

        using var connector = CreateConnector();
        var channels = await connector.GetChannelsAsync(10);

        channels.Should().BeEmpty();
        _handler.RequestCount.Should().Be(3); // auth + retry + success
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    private void SetupAuthAndResponse(string apiResponseJson)
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { ok = true, team_id = "T1", team = "Test", user_id = "U1" })),
            (HttpStatusCode.OK, apiResponseJson)
        });
    }

    private string ComputeSlackSignature(string body, string timestamp)
    {
        string baseString = $"v0:{timestamp}:{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningSecret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(baseString));
        return "v0=" + Convert.ToHexStringLower(hash);
    }
}
