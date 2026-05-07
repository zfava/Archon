using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.RuntimeHealth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Runtime.HostedServices;

public sealed class RuntimeHealthMonitorService : BackgroundService
{
    private readonly IRuntimeHealthManager _healthManager;
    private readonly RuntimeHealthOptions _options;
    private readonly ILogger<RuntimeHealthMonitorService> _logger;

    public RuntimeHealthMonitorService(
        IRuntimeHealthManager healthManager,
        IOptions<RuntimeHealthOptions> options,
        ILogger<RuntimeHealthMonitorService> logger)
    {
        _healthManager = healthManager;
        _options = options.Value;
        _logger = logger;
    }

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Runtime health monitor started (interval: {Interval}s)", _options.MonitorIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await global::System.Threading.Tasks.Task.Delay(
                    TimeSpan.FromSeconds(_options.MonitorIntervalSeconds), stoppingToken);

                await _healthManager.RunHealthCheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Runtime health check cycle failed");
            }
        }

        _logger.LogInformation("Runtime health monitor stopped");
    }
}
