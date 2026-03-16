using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ArchonAI.Common.Observability;

public static class Telemetry
{
    public const string ServiceName = "ArchonAI";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> TasksQueued = Meter.CreateCounter<long>("archonai.tasks.queued");
    public static readonly Counter<long> TasksExecuted = Meter.CreateCounter<long>("archonai.tasks.executed");
    public static readonly Counter<long> TasksFailed = Meter.CreateCounter<long>("archonai.tasks.failed");
    public static readonly Histogram<double> TaskExecutionDurationMs = Meter.CreateHistogram<double>("archonai.task.execution.duration.ms");
    public static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>("archonai.events.published");
    public static readonly Counter<long> EventsDeadLettered = Meter.CreateCounter<long>("archonai.events.dlq");
    public static readonly Counter<long> MemoryQueries = Meter.CreateCounter<long>("archonai.memory.queries");
}
