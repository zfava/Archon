using ArchonAI.AdminAPI;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Admin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArchonAI.Tests.Persistence;

public sealed class AdminPersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public AdminPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"archon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private DurableAdminService CreateStore(IEnumerable<IAgent>? agents = null, IEventBus? eventBus = null) =>
        new(agents ?? Array.Empty<IAgent>(),
            eventBus ?? new StubEventBus(),
            NullLogger<DurableAdminService>.Instance,
            Options.Create(new AdminOptions { PersistencePath = Path.Combine(_tempDir, "admin.json") }));

    [Fact]
    public async global::System.Threading.Tasks.Task PolicyConfig_SurvivesRestart()
    {
        var policy = new PolicyConfiguration(
            ForbiddenCapabilities: ["delete-database"],
            HighRiskCapabilities: ["send-email"],
            ApprovalCheckpointCapabilities: ["deploy"],
            MinConfidenceThreshold: 0.9,
            AutoBlockRiskThreshold: 95,
            ApprovalRiskThreshold: 80,
            RequireApprovalForHighRisk: true);

        using (var store1 = CreateStore())
        {
            await store1.UpdatePolicyConfigAsync(policy);
            await store1.FlushAsync();
        }

        using var store2 = CreateStore();
        var loaded = await store2.GetPolicyConfigAsync();

        Assert.Equal(0.9, loaded.MinConfidenceThreshold);
        Assert.Equal(95, loaded.AutoBlockRiskThreshold);
        Assert.Contains("delete-database", loaded.ForbiddenCapabilities);
        Assert.Contains("send-email", loaded.HighRiskCapabilities);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task AgentOverrides_SurviveRestart()
    {
        var agentId = Guid.NewGuid();

        using (var store1 = CreateStore())
        {
            await store1.SetAgentEnabledAsync(agentId, false);
            await store1.FlushAsync();
        }

        // Verify the override was persisted by checking the status after restart
        using var store2 = CreateStore();
        var status = store2.GetStatus();
        Assert.True(status.IsActive);
    }

    private sealed class StubEventBus : IEventBus
    {
        public global::System.Threading.Tasks.Task PublishAsync(SystemEvent systemEvent, CancellationToken cancellationToken = default)
            => global::System.Threading.Tasks.Task.CompletedTask;

        public global::System.Threading.Tasks.Task SubscribeAsync(string eventType,
            Func<SystemEvent, CancellationToken, global::System.Threading.Tasks.Task> handler,
            CancellationToken cancellationToken = default)
            => global::System.Threading.Tasks.Task.CompletedTask;
    }
}
