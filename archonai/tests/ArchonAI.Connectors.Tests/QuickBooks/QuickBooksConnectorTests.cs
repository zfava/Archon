using System.Net;
using System.Text.Json;
using ArchonAI.Connectors.QuickBooks;
using ArchonAI.Connectors.Tests.Shared;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.QuickBooks;

public sealed class QuickBooksConnectorTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<QuickBooksConnector> _logger = Substitute.For<ILogger<QuickBooksConnector>>();
    private readonly QuickBooksOptions _options = new()
    {
        BaseUrl = "https://quickbooks.api.test",
        OAuthTokenUrl = "https://oauth.test/token",
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        RefreshToken = "test-refresh-token",
        CompanyId = "123456789",
        ApiVersion = "v3",
        MaxRetries = 2,
        RetryBaseDelayMs = 10
    };

    private QuickBooksConnector CreateConnector()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://quickbooks.api.test") };
        return new QuickBooksConnector(httpClient, _eventBus, _logger, Options.Create(_options));
    }

    [Fact]
    public void SystemName_Returns_QuickBooks()
    {
        using var connector = CreateConnector();
        connector.SystemName.Should().Be("quickbooks");
    }

    [Fact]
    public async Task AuthenticateAsync_Success_ReturnsAuthResult()
    {
        _handler.SetResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            access_token = "test-access-token",
            expires_in = 3600
        }));

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeTrue();
        result.CompanyId.Should().Be("123456789");
        result.Error.Should().BeNull();
        result.ExpiresAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_Failure_ReturnsError()
    {
        _handler.SetResponse(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new
        {
            error = "invalid_grant",
            error_description = "token expired"
        }));

        using var connector = CreateConnector();
        var result = await connector.AuthenticateAsync();

        result.IsAuthenticated.Should().BeFalse();
        result.Error.Should().Be("token expired");
    }

    [Fact]
    public async Task GetFinancialReportsAsync_ReturnsRecords()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            Columns = new
            {
                Column = new[] { new { ColTitle = "Date" }, new { ColTitle = "Amount" } }
            },
            Rows = new
            {
                Row = new[]
                {
                    new { ColData = new[] { new { value = "2026-01-01" }, new { value = "5000.00" } } },
                    new { ColData = new[] { new { value = "2026-02-01" }, new { value = "7500.00" } } }
                }
            }
        }));

        using var connector = CreateConnector();
        var records = await connector.GetFinancialReportsAsync("ProfitAndLoss", "2026-01-01", "2026-03-01");

        records.Should().HaveCount(2);
        records[0].ObjectType.Should().Be("ProfitAndLoss");
        records[0].Fields["Date"].Should().Be("2026-01-01");
        records[0].Fields["Amount"].Should().Be("5000.00");
    }

    [Fact]
    public async Task CreateInvoiceAsync_ReturnsInvoiceId()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { Invoice = new { Id = "INV-001" } }))
        });

        using var connector = CreateConnector();
        var lineItems = new List<QuickBooksLineItem>
        {
            new("Consulting", 150.00m, 2, null)
        };
        string invoiceId = await connector.CreateInvoiceAsync("CUST-001", lineItems);

        invoiceId.Should().Be("INV-001");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "quickbooks.invoice.created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateInvoiceAsync_ThrowsOnEmptyCustomerId()
    {
        using var connector = CreateConnector();
        var lineItems = new List<QuickBooksLineItem> { new("Service", 100m, 1, null) };

        var act = () => connector.CreateInvoiceAsync(string.Empty, lineItems);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("customerId");
    }

    [Fact]
    public async Task CreateInvoiceAsync_ThrowsOnNegativeAmount()
    {
        using var connector = CreateConnector();
        var lineItems = new List<QuickBooksLineItem> { new("Service", -50m, 1, null) };

        var act = () => connector.CreateInvoiceAsync("CUST-001", lineItems);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("lineItems");
    }

    [Fact]
    public async Task CreateInvoiceAsync_ThrowsOnZeroQuantity()
    {
        using var connector = CreateConnector();
        var lineItems = new List<QuickBooksLineItem> { new("Service", 100m, 0, null) };

        var act = () => connector.CreateInvoiceAsync("CUST-001", lineItems);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("lineItems");
    }

    [Fact]
    public async Task UpdateCustomerAsync_EmitsAuditEvent()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { Customer = new { Id = "CUST-001", SyncToken = "3" } })),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { Customer = new { Id = "CUST-001", SyncToken = "4" } }))
        });

        using var connector = CreateConnector();
        var fields = new Dictionary<string, string> { ["DisplayName"] = "Updated Name" };
        string result = await connector.UpdateCustomerAsync("CUST-001", fields);

        result.Should().Be("CUST-001");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "quickbooks.customer.updated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_ReturnsRecords()
    {
        SetupAuthAndResponse(JsonSerializer.Serialize(new
        {
            QueryResponse = new
            {
                Purchase = new[]
                {
                    new { Id = "TXN-001", TxnDate = "2026-01-15", TotalAmt = "250.00" },
                    new { Id = "TXN-002", TxnDate = "2026-02-20", TotalAmt = "175.00" }
                }
            }
        }));

        using var connector = CreateConnector();
        var records = await connector.GetTransactionHistoryAsync(null, "2026-01-01", "2026-03-01", 50);

        records.Should().HaveCount(2);
        records[0].Id.Should().Be("TXN-001");
        records[0].ObjectType.Should().Be("Purchase");
        records[0].Fields["TotalAmt"].Should().Be("250.00");
    }

    [Fact]
    public void GetStatus_ReturnsCurrentStatus()
    {
        using var connector = CreateConnector();
        var status = connector.GetStatus();

        status.IsConnected.Should().BeFalse();
        status.CompanyId.Should().Be("123456789");
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
            Arg.Is<SystemEvent>(e => e.EventType == "quickbooks.result.pushed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryLogic_RetriesOnServiceUnavailable()
    {
        _handler.SetResponseSequence(new[]
        {
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.ServiceUnavailable, ""),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { QueryResponse = new { Purchase = Array.Empty<object>() } }))
        });

        using var connector = CreateConnector();
        var records = await connector.GetTransactionHistoryAsync(null, null, null, 10);

        records.Should().BeEmpty();
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
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 })),
            (HttpStatusCode.OK, apiResponseJson)
        });
    }
}
