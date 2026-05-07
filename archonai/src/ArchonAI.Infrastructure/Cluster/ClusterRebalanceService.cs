using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Infrastructure.Cluster;

/// <summary>
/// Background service that periodically evaluates cluster workload balance
/// and triggers rebalancing when load is unevenly distributed.
/// </summary>
public sealed class ClusterRebalanceService : BackgroundService
{
    private readonly IClusterCoordinator _coordinator;
    private readonly ClusterOptions _options;
    private readonly ILogger<ClusterRebalanceService> _logger;

    public ClusterRebalanceService(
        IClusterCoordinator coordinator,
        IOptions<ClusterOptions> options,
        ILogger<ClusterRebalanceService> logger)
    {
        _coordinator = coordinator;
        _options = options.Value;
        _logger = logger;
    }

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AutoRebalance)
        {
            _logger.LogInformation("Cluster auto-rebalance is disabled");
            return;
        }

        _logger.LogInformation(
            "Cluster rebalance service started (interval: {Interval}s, threshold: {Threshold}%)",
            _options.RebalanceIntervalSeconds, _options.RebalanceThresholdPercent);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await global::System.Threading.Tasks.Task.Delay(
                    TimeSpan.FromSeconds(_options.RebalanceIntervalSeconds), stoppingToken);

                await _coordinator.RebalanceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cluster rebalance cycle failed");
            }
        }

        _logger.LogInformation("Cluster rebalance service stopped");
    }
}
