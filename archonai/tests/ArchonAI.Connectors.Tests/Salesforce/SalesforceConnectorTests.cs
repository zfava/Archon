using System.Net;
using System.Text.Json;
using ArchonAI.Connectors.Salesforce;
using ArchonAI.Connectors.Tests.Shared;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.Salesforce;

public sealed class SalesforceConnectorTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<SalesforceConnector> _logger = Substitute.For<ILogger<SalesforceConnector>>();
    private readonly SalesforceOptions _options = new()
    {
        LoginUrl = "https://login.salesforce.test",
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        Username = "test@example.com",
        Password = "testpassword",
        SecurityToken = "testsecuritytoken",
        ApiVersion = "v59.0",
        MaxRetries = 2,
        RetryBaseDelayMs = 10
    };

    private SalesforceConnector CreateConnector()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://test.salesforce.com") };
        return new SalesforceConnector(httpClient, _eventBus, _logger, Options.Create(_options));
    }

    [Fact]
    public void SystemName_Returns_Salesforce()
    {
        using var connector = CreateConnector();
        connector.SystemName.Should().Be("salesforce");
    }

    [Fact]
    public async Task AuthenticateAsync_Success_ReturnsAuthResult()
    {
        _handler.SetResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            access_token = "test-token",
            instance_url = "https://instance.salesforce.test"
        }));

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeTrue();
        result.InstanceUrl.Should().Be("https://instance.salesforce.test");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_Failure_ReturnsError()
    {
        _handler.SetResponse(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new
        {
            error = "invalid_grant",
            error_description = "authentication failure"
        }));

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeFalse();
        result.Error.Should().Be("authentication failure");
    }

    [Fact]
    public async Task QueryAccountsAsync_ReturnsRecords()
    {
        SetupAuthAndQuery("Account", new[]
        {
            new { Id = "001A", Name = "Acme Corp", Industry = "Technology", Type = "Customer" }
        });

        using var connector = CreateConnector();
        var records = await connector.QueryAccountsAsync(string.Empty);

        records.Should().HaveCount(1);
        records[0].Id.Should().Be("001A");
        records[0].ObjectType.Should().Be("Account");
        records[0].Fields["Name"].Should().Be("Acme Corp");
    }

    [Fact]
    public async Task QueryContactsAsync_ReturnsRecords()
    {
        SetupAuthAndQuery("Contact", new[]
        {
            new { Id = "003A", FirstName = "John", LastName = "Doe", Email = "john@example.com", AccountId = "001A" }
        });

        using var connector = CreateConnector();
        var records = await connector.QueryContactsAsync(string.Empty);

        records.Should().HaveCount(1);
        records[0].Id.Should().Be("003A");
        records[0].Fields["Email"].Should().Be("john@example.com");
    }

    [Fact]
    public async Task QueryOpportunitiesAsync_ReturnsRecords()
    {
        SetupAuthAndQuery("Opportunity", new[]
        {
            new { Id = "006A", Name = "Big Deal", StageName = "Closed Won", Amount = "100000", CloseDate = "2026-03-01", AccountId = "001A" }
        });

        using var connector = CreateConnector();
        var records = await connector.QueryOpportunitiesAsync(string.Empty);

        records.Should().HaveCount(1);
        records[0].Fields["StageName"].Should().Be("Closed Won");
    }

    [Fact]
    public async Task CreateRecordAsync_ReturnsRecordId()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", instance_url = "https://instance.salesforce.test" })),
            (HttpStatusCode.Created, JsonSerializer.Serialize(new { id = "001NEW", success = true }))
        });

        using var connector = CreateConnector();
        var fields = new Dictionary<string, string> { ["Name"] = "New Account" };
        string recordId = await connector.CreateRecordAsync("Account", fields);

        recordId.Should().Be("001NEW");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "salesforce.record.created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateRecordAsync_AuditsWriteOperation()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", instance_url = "https://instance.salesforce.test" })),
            (HttpStatusCode.NoContent, "")
        });

        using var connector = CreateConnector();
        var fields = new Dictionary<string, string> { ["Name"] = "Updated Account" };
        string result = await connector.UpdateRecordAsync("Account", "001A", fields);

        result.Should().Be("001A");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "salesforce.record.updated"),
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
            Arg.Is<SystemEvent>(e => e.EventType == "salesforce.result.pushed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryLogic_RetriesOnServiceUnavailable()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", instance_url = "https://instance.salesforce.test" })),
            (HttpStatusCode.ServiceUnavailable, ""),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { totalSize = 0, done = true, records = Array.Empty<object>() }))
        });

        using var connector = CreateConnector();
        var records = await connector.QueryAccountsAsync(string.Empty);

        records.Should().BeEmpty();
        _handler.RequestCount.Should().Be(3); // auth + retry + success
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    private void SetupAuthAndQuery(string objectType, object[] recordData)
    {
        var queryResponse = new
        {
            totalSize = recordData.Length,
            done = true,
            records = recordData
        };

        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", instance_url = "https://instance.salesforce.test" })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(queryResponse))
        });
    }
}
