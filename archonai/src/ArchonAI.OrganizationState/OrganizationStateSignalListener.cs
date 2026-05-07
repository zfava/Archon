using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Perception;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArchonAI.OrganizationState;

public sealed class OrganizationStateSignalListener : BackgroundService
{
    private readonly IEventBus _eventBus;
    private readonly IOrganizationStateEngine _stateEngine;
    private readonly ILogger<OrganizationStateSignalListener> _logger;

    public OrganizationStateSignalListener(
        IEventBus eventBus,
        IOrganizationStateEngine stateEngine,
        ILogger<OrganizationStateSignalListener> logger)
    {
        _eventBus = eventBus;
        _stateEngine = stateEngine;
        _logger = logger;
    }

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrganizationState signal listener started, subscribing to perception observations");

        await _eventBus.SubscribeAsync("perception.observation.created", async (systemEvent, ct) =>
        {
            try
            {
                var observation = MapToObservation(systemEvent);
                if (observation is not null)
                {
                    await _stateEngine.ApplyObservationAsync(observation, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply observation from event {EventId}", systemEvent.Id);
            }
        }, stoppingToken);

        await global::System.Threading.Tasks.Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private static OperationalObservation? MapToObservation(SystemEvent systemEvent)
    {
        var payload = systemEvent.Payload;

        if (!payload.TryGetValue("observationId", out var obsIdStr) ||
            !Guid.TryParse(obsIdStr, out var obsId))
        {
            return null;
        }

        Guid.TryParse(payload.GetValueOrDefault("sourceSignalId", ""), out var sourceSignalId);
        Enum.TryParse<SourceSystem>(payload.GetValueOrDefault("sourceSystem", "CRM"), true, out var sourceSystem);
        Enum.TryParse<ObservationCategory>(payload.GetValueOrDefault("category", "SystemHealth"), true, out var category);
        Enum.TryParse<ObservationSeverity>(payload.GetValueOrDefault("severity", "Info"), true, out var severity);

        return new OperationalObservation(
            ObservationId: obsId,
            SourceSignalId: sourceSignalId,
            SourceSystem: sourceSystem,
            Category: category,
            Severity: severity,
            EntityId: payload.GetValueOrDefault("entityId", ""),
            Summary: payload.GetValueOrDefault("summary", ""),
            StructuredData: payload,
            ObservedAtUtc: systemEvent.OccurredAtUtc,
            ProcessedAtUtc: DateTimeOffset.UtcNow);
    }
}
