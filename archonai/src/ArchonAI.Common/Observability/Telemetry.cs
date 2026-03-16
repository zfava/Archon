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

    // Salesforce connector metrics
    public static readonly Counter<long> SalesforceAuthAttempts = Meter.CreateCounter<long>("archonai.salesforce.auth.attempts");
    public static readonly Counter<long> SalesforceQueryOps = Meter.CreateCounter<long>("archonai.salesforce.query.ops");
    public static readonly Counter<long> SalesforceWriteOps = Meter.CreateCounter<long>("archonai.salesforce.write.ops");
    public static readonly Counter<long> SalesforceErrors = Meter.CreateCounter<long>("archonai.salesforce.errors");
    public static readonly Counter<long> SalesforceRetries = Meter.CreateCounter<long>("archonai.salesforce.retries");
    public static readonly Histogram<double> SalesforceRateLimitRemaining = Meter.CreateHistogram<double>("archonai.salesforce.ratelimit.remaining");
}
