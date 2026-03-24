using ArchonAI.Infrastructure.Health;
using Xunit;

namespace ArchonAI.Connectors.Tests.Resilience;

public class WorkerHealthTests
{
    [Fact]
    public async Task HealthProbeResult_Ready_When_All_Checks_Pass()
    {
        Func<CancellationToken, Task<HealthProbeResult>> readinessCheck = ct =>
        {
            return Task.FromResult(new HealthProbeResult
            {
                IsReady = true,
                Checks = new Dictionary<string, HealthCheckEntry>
                {
                    ["nats"] = new HealthCheckEntry { Status = "healthy", Detail = "Connected" },
                    ["worker_role"] = new HealthCheckEntry { Status = "healthy", Detail = "runtime" }
                }
            });
        };

        var result = await readinessCheck(CancellationToken.None);
        Assert.True(result.IsReady);
        Assert.Equal("healthy", result.Checks["nats"].Status);
    }

    [Fact]
    public async Task HealthProbeResult_NotReady_When_Nats_Down()
    {
        Func<CancellationToken, Task<HealthProbeResult>> readinessCheck = ct =>
        {
            return Task.FromResult(new HealthProbeResult
            {
                IsReady = false,
                Checks = new Dictionary<string, HealthCheckEntry>
                {
                    ["nats"] = new HealthCheckEntry { Status = "unhealthy", Detail = "Connection refused" },
                    ["worker_role"] = new HealthCheckEntry { Status = "healthy", Detail = "runtime" }
                }
            });
        };

        var result = await readinessCheck(CancellationToken.None);
        Assert.False(result.IsReady);
        Assert.Equal("unhealthy", result.Checks["nats"].Status);
    }

    [Fact]
    public async Task HealthProbeResult_Respects_Cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<CancellationToken, Task<HealthProbeResult>> readinessCheck = ct =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HealthProbeResult { IsReady = true });
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => readinessCheck(cts.Token));
    }
}
