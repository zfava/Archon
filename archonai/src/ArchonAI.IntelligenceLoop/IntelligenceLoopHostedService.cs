using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.IntelligenceLoop;

/// <summary>
/// Background service that runs the intelligence loop continuously.
/// Each cycle: Perception → Planner → Reasoner → Simulation → TaskGraph → Agents → Evaluation → Learning.
/// </summary>
public sealed class IntelligenceLoopHostedService : BackgroundService
{
    private readonly IIntelligenceLoop _loop;
    private readonly ILogger<IntelligenceLoopHostedService> _logger;
    private readonly IntelligenceLoopOptions _options;

    public IntelligenceLoopHostedService(
        IIntelligenceLoop loop,
        ILogger<IntelligenceLoopHostedService> logger,
        IOptions<IntelligenceLoopOptions> options)
    {
        _loop = loop;
        _logger = logger;
        _options = options.Value;
    }

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Intelligence loop hosted service starting. Cycle interval: {Interval}s",
            _options.CycleIntervalSeconds);

        // Brief startup delay to let other services initialize
        await global::System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _loop.ExecuteCycleAsync(stoppingToken);

                _logger.LogInformation(
                    "Intelligence loop cycle {CycleId} completed: {Goals} goals, {Tasks} tasks, {Duration}ms",
                    result.CycleId, result.GoalsGenerated, result.TasksExecuted,
                    result.CycleDuration.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Intelligence loop cycle failed. Retrying after interval.");
            }

            try
            {
                await global::System.Threading.Tasks.Task.Delay(
                    TimeSpan.FromSeconds(_options.CycleIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Intelligence loop hosted service stopping. Final status: {@Status}", _loop.GetStatus());
    }
}
