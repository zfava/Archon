using System.Net;
using System.Text.Json;
using ArchonAI.Connectors.HubSpot;
using ArchonAI.Connectors.Tests.Shared;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.HubSpot;

public sealed class HubSpotConnectorTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<HubSpotConnector> _logger = Substitute.For<ILogger<HubSpotConnector>>();
    private readonly HubSpotOptions _options = new()
    {
        BaseUrl = "https://api.hubapi.test",
        OAuthTokenUrl = "https://api.hubapi.test/oauth/v1/token",
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        RefreshToken = "test-refresh-token",
        MaxRetries = 2,
        RetryBaseDelayMs = 10
    };

    private HubSpotConnector CreateConnector()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://api.hubapi.test") };
        return new HubSpotConnector(httpClient, _eventBus, _logger, Options.Create(_options));
    }

    [Fact]
    public void SystemName_Returns_HubSpot()
    {
        using var connector = CreateConnector();
        connector.SystemName.Should().Be("hubspot");
    }

    [Fact]
    public async Task AuthenticateAsync_Success_ReturnsAuthResult()
    {
        _handler.SetResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            access_token = "test-token",
            expires_in = 1800,
            hub_id = 12345678
        }));

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeTrue();
        result.PortalId.Should().Be("12345678");
        result.ExpiresAtUtc.Should().NotBeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_Failure_ReturnsError()
    {
        _handler.SetResponse(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new
        {
            status = "BAD_REFRESH_TOKEN",
            message = "Invalid refresh token"
        }));

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeFalse();
        result.Error.Should().Be("Invalid refresh token");
    }

    [Fact]
    public async Task GetContactsAsync_ReturnsRecords()
    {
        SetupAuthAndCrmResponse(new
        {
            results = new[]
            {
                new
                {
                    id = "101",
                    properties = new { firstname = "Jane", lastname = "Doe", email = "jane@example.com" }
                }
            }
        });

        using var connector = CreateConnector();
        var records = await connector.GetContactsAsync(null);

        records.Should().HaveCount(1);
        records[0].Id.Should().Be("101");
        records[0].ObjectType.Should().Be("Contact");
        records[0].Properties["firstname"].Should().Be("Jane");
        records[0].Properties["email"].Should().Be("jane@example.com");
    }

    [Fact]
    public async Task GetDealsAsync_ReturnsRecords()
    {
        SetupAuthAndCrmResponse(new
        {
            results = new[]
            {
                new
                {
                    id = "201",
                    properties = new { dealname = "Big Opportunity", amount = "50000", dealstage = "closedwon" }
                }
            }
        });

        using var connector = CreateConnector();
        var records = await connector.GetDealsAsync(null);

        records.Should().HaveCount(1);
        records[0].Id.Should().Be("201");
        records[0].ObjectType.Should().Be("Deal");
        records[0].Properties["dealname"].Should().Be("Big Opportunity");
    }

    [Fact]
    public async Task CreateRecordAsync_ReturnsRecordId()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 1800 })),
            (HttpStatusCode.Created, JsonSerializer.Serialize(new { id = "301" }))
        });

        using var connector = CreateConnector();
        var properties = new Dictionary<string, string> { ["firstname"] = "New", ["lastname"] = "Contact" };
        string recordId = await connector.CreateRecordAsync("contacts", properties);

        recordId.Should().Be("301");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "hubspot.record.created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatePipelineRecordAsync_AuditsWriteOperation()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 1800 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { id = "201" }))
        });

        using var connector = CreateConnector();
        var properties = new Dictionary<string, string> { ["dealstage"] = "contractsent" };
        string result = await connector.UpdatePipelineRecordAsync("deals", "201", properties);

        result.Should().Be("201");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "hubspot.record.updated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetStatus_ReturnsCurrentStatus()
    {
        using var connector = CreateConnector();
        var status = connector.GetStatus();

        status.IsConnected.Should().BeFalse();
        status.TotalRequests.Should().Be(0);
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
            Arg.Is<SystemEvent>(e => e.EventType == "hubspot.result.pushed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryLogic_RetriesOnServiceUnavailable()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 1800 })),
            (HttpStatusCode.ServiceUnavailable, ""),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { results = Array.Empty<object>() }))
        });

        using var connector = CreateConnector();
        var records = await connector.GetContactsAsync(null);

        records.Should().BeEmpty();
        _handler.RequestCount.Should().Be(3); // auth + retry + success
    }

    [Fact]
    public async Task GetContactsAsync_WithFilter_IncludesFilterInRequest()
    {
        SetupAuthAndCrmResponse(new { results = Array.Empty<object>() });

        using var connector = CreateConnector();
        var records = await connector.GetContactsAsync("email,firstname,lastname", 50);

        records.Should().BeEmpty();
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    private void SetupAuthAndCrmResponse(object crmResponse)
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 1800 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(crmResponse))
        });
    }
}
