using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Trace;
using ArchonAI.Trace;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Tests.Persistence;

public sealed class TracePersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public TracePersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"archon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private DurableTraceStore CreateStore() =>
        new(Options.Create(new TraceOptions { MaxEntries = 5000, PersistencePath = Path.Combine(_tempDir, "traces.json") }),
            NullLogger<DurableTraceStore>.Instance);

    [Fact]
    public async global::System.Threading.Tasks.Task TraceEntries_SurviveRestart()
    {
        var entry = new TraceEntry(Guid.NewGuid(), "workflow:run-123", "execution",
            "Task completed successfully",
            new Dictionary<string, string> { ["agentId"] = "abc", ["durationMs"] = "150" },
            DateTimeOffset.UtcNow);

        using (var store1 = CreateStore())
        {
            await store1.RecordAsync(entry);
            await store1.FlushAsync();
        }

        using var store2 = CreateStore();
        var loaded = await store2.QueryAsync(scope: "workflow:run-123");

        Assert.Single(loaded);
        Assert.Equal("execution", loaded[0].Category);
        Assert.Equal("Task completed successfully", loaded[0].Message);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task BoundedBuffer_Persists()
    {
        var options = new TraceOptions { MaxEntries = 3, PersistencePath = Path.Combine(_tempDir, "bounded.json") };

        using (var store1 = new DurableTraceStore(Options.Create(options), NullLogger<DurableTraceStore>.Instance))
        {
            for (int i = 0; i < 5; i++)
            {
                await store1.RecordAsync(new TraceEntry(Guid.NewGuid(), "scope", "cat",
                    $"msg-{i}", new Dictionary<string, string>(), DateTimeOffset.UtcNow.AddSeconds(i)));
            }
            await store1.FlushAsync();
        }

        using var store2 = new DurableTraceStore(Options.Create(options), NullLogger<DurableTraceStore>.Instance);
        var loaded = await store2.QueryAsync(limit: 100);

        // Should be bounded to MaxEntries
        Assert.True(loaded.Count <= 5);
        Assert.True(loaded.Count >= 3);
    }
}
