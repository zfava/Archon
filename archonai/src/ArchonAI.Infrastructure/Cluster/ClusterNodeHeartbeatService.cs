using System.Diagnostics;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Cluster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Infrastructure.Cluster;

/// <summary>
/// Background service that registers this worker node with the cluster
/// and sends periodic heartbeats with current load metrics.
/// </summary>
public sealed class ClusterNodeHeartbeatService : BackgroundService
{
    private readonly IClusterCoordinator _coordinator;
    private readonly ClusterNodeRegistrationOptions _nodeOptions;
    private readonly ClusterOptions _clusterOptions;
    private readonly ILogger<ClusterNodeHeartbeatService> _logger;

    private Guid _nodeId;

    public ClusterNodeHeartbeatService(
        IClusterCoordinator coordinator,
        IOptions<ClusterNodeRegistrationOptions> nodeOptions,
        IOptions<ClusterOptions> clusterOptions,
        ILogger<ClusterNodeHeartbeatService> logger)
    {
        _coordinator = coordinator;
        _nodeOptions = nodeOptions.Value;
        _clusterOptions = clusterOptions.Value;
        _logger = logger;
    }

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var node = await _coordinator.RegisterNodeAsync(
                hostName: _nodeOptions.HostName,
                role: _nodeOptions.Role,
                capacity: new NodeCapacity(
                    MaxConcurrentTasks: _nodeOptions.MaxConcurrentTasks,
                    MaxAgents: _nodeOptions.MaxAgents,
                    AvailableCpuCores: _nodeOptions.CpuCores,
                    AvailableMemoryBytes: _nodeOptions.MemoryBytes,
                    GpuSlots: _nodeOptions.GpuSlots),
                capabilities: _nodeOptions.Capabilities,
                labels: _nodeOptions.Labels,
                cancellationToken: stoppingToken);

            _nodeId = node.NodeId;
            _logger.LogInformation(
                "Cluster node registered: {NodeId} as '{HostName}' role={Role}",
                _nodeId, _nodeOptions.HostName, _nodeOptions.Role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register cluster node '{HostName}'", _nodeOptions.HostName);
            return;
        }

        int intervalSeconds = Math.Max(5, _clusterOptions.HeartbeatTimeoutSeconds / 3);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await global::System.Threading.Tasks.Task.Delay(
                    TimeSpan.FromSeconds(intervalSeconds), stoppingToken);

                var load = CollectCurrentLoad();
                await _coordinator.HeartbeatAsync(_nodeId, load, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed for node {NodeId}", _nodeId);
            }
        }

        _logger.LogInformation("Cluster node heartbeat stopped for {NodeId}", _nodeId);
    }

    private NodeLoad CollectCurrentLoad()
    {
        var process = Process.GetCurrentProcess();
        double cpuPercent = Math.Min(100, process.TotalProcessorTime.TotalMilliseconds /
            (Environment.ProcessorCount * (DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds) * 100);

        long memoryBytes = process.WorkingSet64;
        double memoryPercent = _nodeOptions.MemoryBytes > 0
            ? (double)memoryBytes / _nodeOptions.MemoryBytes * 100
            : 0;

        return new NodeLoad(
            ActiveTasks: (int)Math.Min(ThreadPool.PendingWorkItemCount, int.MaxValue),
            QueuedTasks: 0,
            ActiveAgents: 0,
            CpuUtilizationPercent: Math.Round(Math.Max(0, Math.Min(100, cpuPercent)), 1),
            MemoryUtilizationPercent: Math.Round(Math.Max(0, Math.Min(100, memoryPercent)), 1),
            GpuSlotsUsed: 0,
            MeasuredAtUtc: DateTimeOffset.UtcNow);
    }
}
