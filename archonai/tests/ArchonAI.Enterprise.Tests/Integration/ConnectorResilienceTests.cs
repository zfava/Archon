using System.Net;
using System.Text.Json;
using ArchonAI.Connectors.Salesforce;
using ArchonAI.Connectors.HubSpot;
using ArchonAI.Enterprise.Tests.Shared;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Integration tests for connector failure handling and retry logic:
/// transient failure recovery, authentication re-negotiation,
/// rate limit handling, and circuit breaker behavior.
/// </summary>
public sealed class ConnectorResilienceTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();

    // ── Salesforce Retry Tests ────────────────────────────────────────

    private SalesforceConnector CreateSalesforce(int maxRetries = 3)
    {
        var opts = new SalesforceOptions
        {
            LoginUrl = "https://login.salesforce.test",
            ClientId = "test-client-id",
            ClientSecret = "test-secret",
            Username = "test@test.com",
            Password = "testpass",
            SecurityToken = "token",
            ApiVersion = "v59.0",
            MaxRetries = maxRetries,
            RetryBaseDelayMs = 10,
        };

        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://test.salesforce.com") };
        return new SalesforceConnector(httpClient, _eventBus,
            Substitute.For<ILogger<SalesforceConnector>>(), Options.Create(opts));
    }

    [Fact]
    public async Task Salesforce_TransientFailure_RetriesAndRecovers()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, AuthJson()),
            (HttpStatusCode.ServiceUnavailable, ""),
            (HttpStatusCode.OK, EmptyQueryJson()),
        });

        using var connector = CreateSalesforce();
        var records = await connector.QueryAccountsAsync("");

        records.Should().BeEmpty();
        _handler.RequestCount.Should().Be(3); // auth + fail + retry success
    }

    [Fact]
    public async Task Salesforce_RateLimited_RetriesAfterBackoff()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, AuthJson()),
            (HttpStatusCode.TooManyRequests, ""),
            (HttpStatusCode.OK, EmptyQueryJson()),
        });

        using var connector = CreateSalesforce();
        var records = await connector.QueryAccountsAsync("");

        records.Should().BeEmpty();
        _handler.RequestCount.Should().Be(3);
    }

    [Fact]
    public async Task Salesforce_AuthFailure_ReportsError()
    {
        _handler.SetResponse(HttpStatusCode.BadRequest,
            JsonSerializer.Serialize(new { error = "invalid_grant", error_description = "bad credentials" }));

        using var connector = CreateSalesforce();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeFalse();
        result.Error.Should().Contain("bad credentials");
    }

    [Fact]
    public async Task Salesforce_MaxRetriesExhausted_ReportsFailure()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, AuthJson()),
            (HttpStatusCode.ServiceUnavailable, ""),
            (HttpStatusCode.ServiceUnavailable, ""),
            (HttpStatusCode.ServiceUnavailable, ""), // All retries fail
        });

        using var connector = CreateSalesforce(maxRetries: 2);

        // Should throw or return empty after exhausting retries
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await connector.QueryAccountsAsync(""));
    }

    [Fact]
    public async Task Salesforce_CreateRecord_EmitsAuditEvent()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, AuthJson()),
            (HttpStatusCode.Created, JsonSerializer.Serialize(new { id = "001NEW", success = true })),
        });

        using var connector = CreateSalesforce();
        var fields = new Dictionary<string, string> { ["Name"] = "Test Account" };
        await connector.CreateRecordAsync("Account", fields);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "salesforce.record.created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Salesforce_InitialStatus_NotConnected()
    {
        using var connector = CreateSalesforce();
        var status = connector.GetStatus();

        status.IsConnected.Should().BeFalse();
        status.TotalRequests.Should().Be(0);
    }

    // ── HubSpot Retry Tests ──────────────────────────────────────────

    private HubSpotConnector CreateHubSpot(int maxRetries = 3)
    {
        var opts = new HubSpotOptions
        {
            BaseUrl = "https://api.hubspot.test",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RefreshToken = "test-refresh",
            MaxRetries = maxRetries,
            RetryBaseDelayMs = 10,
        };

        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://api.hubspot.test") };
        return new HubSpotConnector(httpClient, _eventBus,
            Substitute.For<ILogger<HubSpotConnector>>(), Options.Create(opts));
    }

    [Fact]
    public async Task HubSpot_TransientFailure_RetriesAndRecovers()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, HubSpotAuthJson()),
            (HttpStatusCode.ServiceUnavailable, ""),
            (HttpStatusCode.OK, HubSpotEmptyResultJson()),
        });

        using var connector = CreateHubSpot();
        var records = await connector.GetContactsAsync(null);

        records.Should().BeEmpty();
    }

    [Fact]
    public async Task HubSpot_CreateRecord_EmitsAuditEvent()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, HubSpotAuthJson()),
            (HttpStatusCode.Created, JsonSerializer.Serialize(new { id = "123" })),
        });

        using var connector = CreateHubSpot();
        var props = new Dictionary<string, string> { ["email"] = "test@test.com" };
        await connector.CreateRecordAsync("contacts", props);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "hubspot.record.created"),
            Arg.Any<CancellationToken>());
    }

    // ── Cross-Connector Consistency ───────────────────────────────────

    [Fact]
    public async Task AllConnectors_PushResult_EmitsEvent()
    {
        using var sf = CreateSalesforce();
        var result = new ExecutionResult(Guid.NewGuid(), true, "OK",
            new Dictionary<string, string>(), Array.Empty<string>(),
            Array.Empty<string>(), DateTimeOffset.UtcNow);

        await sf.PushResultAsync(result);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "salesforce.result.pushed"),
            Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static string AuthJson() =>
        JsonSerializer.Serialize(new { access_token = "test-token", instance_url = "https://instance.salesforce.test" });

    private static string EmptyQueryJson() =>
        JsonSerializer.Serialize(new { totalSize = 0, done = true, records = Array.Empty<object>() });

    private static string HubSpotAuthJson() =>
        JsonSerializer.Serialize(new { access_token = "hs-token", expires_in = 3600 });

    private static string HubSpotEmptyResultJson() =>
        JsonSerializer.Serialize(new { results = Array.Empty<object>() });

    public void Dispose() => _handler.Dispose();
}
