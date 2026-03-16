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

    // HubSpot connector metrics
    public static readonly Counter<long> HubSpotAuthAttempts = Meter.CreateCounter<long>("archonai.hubspot.auth.attempts");
    public static readonly Counter<long> HubSpotQueryOps = Meter.CreateCounter<long>("archonai.hubspot.query.ops");
    public static readonly Counter<long> HubSpotWriteOps = Meter.CreateCounter<long>("archonai.hubspot.write.ops");
    public static readonly Counter<long> HubSpotErrors = Meter.CreateCounter<long>("archonai.hubspot.errors");
    public static readonly Counter<long> HubSpotRetries = Meter.CreateCounter<long>("archonai.hubspot.retries");
    public static readonly Histogram<double> HubSpotRateLimitRemaining = Meter.CreateHistogram<double>("archonai.hubspot.ratelimit.remaining");

    // QuickBooks connector metrics
    public static readonly Counter<long> QuickBooksAuthAttempts = Meter.CreateCounter<long>("archonai.quickbooks.auth.attempts");
    public static readonly Counter<long> QuickBooksQueryOps = Meter.CreateCounter<long>("archonai.quickbooks.query.ops");
    public static readonly Counter<long> QuickBooksWriteOps = Meter.CreateCounter<long>("archonai.quickbooks.write.ops");
    public static readonly Counter<long> QuickBooksErrors = Meter.CreateCounter<long>("archonai.quickbooks.errors");
    public static readonly Counter<long> QuickBooksRetries = Meter.CreateCounter<long>("archonai.quickbooks.retries");
    public static readonly Histogram<double> QuickBooksRateLimitRemaining = Meter.CreateHistogram<double>("archonai.quickbooks.ratelimit.remaining");

    // Slack connector metrics
    public static readonly Counter<long> SlackAuthAttempts = Meter.CreateCounter<long>("archonai.slack.auth.attempts");
    public static readonly Counter<long> SlackMessagesSent = Meter.CreateCounter<long>("archonai.slack.messages.sent");
    public static readonly Counter<long> SlackAlertsSent = Meter.CreateCounter<long>("archonai.slack.alerts.sent");
    public static readonly Counter<long> SlackQueryOps = Meter.CreateCounter<long>("archonai.slack.query.ops");
    public static readonly Counter<long> SlackWebhookEvents = Meter.CreateCounter<long>("archonai.slack.webhook.events");
    public static readonly Counter<long> SlackErrors = Meter.CreateCounter<long>("archonai.slack.errors");
    public static readonly Counter<long> SlackRetries = Meter.CreateCounter<long>("archonai.slack.retries");
    public static readonly Histogram<double> SlackRateLimitRemaining = Meter.CreateHistogram<double>("archonai.slack.ratelimit.remaining");
}
