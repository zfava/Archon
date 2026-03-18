using ArchonAI.Connectors.Framework;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Connector;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.Framework;

public sealed class IntegrationControlServiceTests
{
    private IntegrationControlService CreateService(params IConnector[] connectors)
    {
        return new IntegrationControlService(
            connectors,
            NullLogger<IntegrationControlService>.Instance);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Dashboard_WithNoConnectors_ReturnsEmpty()
    {
        var service = CreateService();
        var dashboard = await service.GetDashboardAsync();

        dashboard.TotalConnectors.Should().Be(0);
        dashboard.HealthyConnectors.Should().Be(0);
        dashboard.RecentSyncs.Should().BeEmpty();
        dashboard.FailedSyncs.Should().BeEmpty();
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Dashboard_WithConnectors_ReportsHealth()
    {
        var c1 = Substitute.For<IConnector>();
        c1.SystemName.Returns("connector-a");
        var c2 = Substitute.For<IConnector>();
        c2.SystemName.Returns("connector-b");

        var service = CreateService(c1, c2);
        var dashboard = await service.GetDashboardAsync();

        dashboard.TotalConnectors.Should().Be(2);
        dashboard.ConnectorHealth.Should().HaveCount(2);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetConnectorHealth_UnknownConnector_ReturnsNotConfigured()
    {
        var service = CreateService();
        var report = await service.GetConnectorHealthAsync("nonexistent");

        report.ConnectorName.Should().Be("nonexistent");
        report.Status.Should().Be(ConnectorHealthStatus.NotConfigured);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RecordSyncEvent_AppearsInRecentSyncs()
    {
        var service = CreateService();

        var syncEvent = new SyncEvent(
            Guid.NewGuid(), "salesforce", "QueryAccounts", SyncDirection.Inbound,
            SyncStatus.Completed, 50, 0, null, ConnectorErrorCategory.None,
            null, TimeSpan.FromMilliseconds(350),
            DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow);

        service.RecordSyncEvent(syncEvent);

        var recent = await service.GetRecentSyncsAsync();
        recent.Should().ContainSingle();
        recent[0].ConnectorName.Should().Be("salesforce");
        recent[0].RecordsProcessed.Should().Be(50);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RecordSyncEvent_FailedSync_AppearsInFailedSyncs()
    {
        var service = CreateService();

        var failedSync = new SyncEvent(
            Guid.NewGuid(), "hubspot", "GetContacts", SyncDirection.Inbound,
            SyncStatus.Failed, 0, 0, "Rate limit exceeded",
            ConnectorErrorCategory.RateLimit, null, TimeSpan.FromSeconds(1),
            DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow);

        service.RecordSyncEvent(failedSync);

        var failed = await service.GetFailedSyncsAsync();
        failed.Should().ContainSingle();
        failed[0].ErrorCategory.Should().Be(ConnectorErrorCategory.RateLimit);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RecordSyncEvent_FilterByConnector()
    {
        var service = CreateService();

        service.RecordSyncEvent(new SyncEvent(Guid.NewGuid(), "salesforce", "Query",
            SyncDirection.Inbound, SyncStatus.Completed, 10, 0, null,
            ConnectorErrorCategory.None, null, TimeSpan.FromMilliseconds(100),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        service.RecordSyncEvent(new SyncEvent(Guid.NewGuid(), "hubspot", "GetDeals",
            SyncDirection.Inbound, SyncStatus.Completed, 5, 0, null,
            ConnectorErrorCategory.None, null, TimeSpan.FromMilliseconds(50),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var sfOnly = await service.GetRecentSyncsAsync("salesforce");
        sfOnly.Should().ContainSingle();
        sfOnly[0].ConnectorName.Should().Be("salesforce");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RecordWebhookEvent_AppearsInList()
    {
        var service = CreateService();

        service.RecordWebhookEvent(new WebhookEvent(
            Guid.NewGuid(), "slack", "message.posted", true, true,
            null, DateTimeOffset.UtcNow));

        var events = await service.GetWebhookEventsAsync();
        events.Should().ContainSingle();
        events[0].ConnectorName.Should().Be("slack");
        events[0].SignatureValid.Should().BeTrue();
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RecordWebhookEvent_InvalidSignature_StillRecorded()
    {
        var service = CreateService();

        service.RecordWebhookEvent(new WebhookEvent(
            Guid.NewGuid(), "slack", "suspicious_event", false, false,
            "Invalid HMAC signature", DateTimeOffset.UtcNow));

        var events = await service.GetWebhookEventsAsync();
        events.Should().ContainSingle();
        events[0].SignatureValid.Should().BeFalse();
        events[0].ErrorMessage.Should().Contain("Invalid HMAC");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetCredentialStatuses_ReturnsAllConnectors()
    {
        var c1 = Substitute.For<IConnector>();
        c1.SystemName.Returns("conn-1");
        var c2 = Substitute.For<IConnector>();
        c2.SystemName.Returns("conn-2");

        var service = CreateService(c1, c2);
        var statuses = await service.GetCredentialStatusesAsync();

        statuses.Should().HaveCount(2);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task SyncEventHistory_BoundsToMaxSize()
    {
        var service = CreateService();

        for (int i = 0; i < 1050; i++)
        {
            service.RecordSyncEvent(new SyncEvent(Guid.NewGuid(), "test", "op",
                SyncDirection.Inbound, SyncStatus.Completed, 1, 0, null,
                ConnectorErrorCategory.None, null, TimeSpan.Zero,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }

        var all = await service.GetRecentSyncsAsync(limit: 2000);
        all.Count.Should().BeLessOrEqualTo(1000);
    }
}
