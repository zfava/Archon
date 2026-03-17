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

    // Memory compression and retrieval metrics
    public static readonly Counter<long> MemoryCompressionRuns = Meter.CreateCounter<long>("archonai.memory.compression.runs");
    public static readonly Counter<long> MemoryDuplicatesRemoved = Meter.CreateCounter<long>("archonai.memory.duplicates.removed");
    public static readonly Counter<long> MemoryClustersFormed = Meter.CreateCounter<long>("archonai.memory.clusters.formed");
    public static readonly Counter<long> MemorySummariesCreated = Meter.CreateCounter<long>("archonai.memory.summaries.created");
    public static readonly Counter<long> MemoryRetrievalQueries = Meter.CreateCounter<long>("archonai.memory.retrieval.queries");
    public static readonly Counter<long> MemoryGraphEnrichedQueries = Meter.CreateCounter<long>("archonai.memory.graph.enriched.queries");

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

    // Google Workspace connector metrics
    public static readonly Counter<long> GoogleWorkspaceAuthAttempts = Meter.CreateCounter<long>("archonai.google.workspace.auth.attempts");
    public static readonly Counter<long> GoogleWorkspaceQueryOps = Meter.CreateCounter<long>("archonai.google.workspace.query.ops");
    public static readonly Counter<long> GoogleWorkspaceWriteOps = Meter.CreateCounter<long>("archonai.google.workspace.write.ops");
    public static readonly Counter<long> GoogleWorkspaceErrors = Meter.CreateCounter<long>("archonai.google.workspace.errors");
    public static readonly Counter<long> GoogleWorkspaceRetries = Meter.CreateCounter<long>("archonai.google.workspace.retries");
    public static readonly Histogram<double> GoogleWorkspaceRateLimitRemaining = Meter.CreateHistogram<double>("archonai.google.workspace.ratelimit.remaining");

    // Microsoft 365 connector metrics
    public static readonly Counter<long> M365AuthAttempts = Meter.CreateCounter<long>("archonai.m365.auth.attempts");
    public static readonly Counter<long> M365QueryOps = Meter.CreateCounter<long>("archonai.m365.query.ops");
    public static readonly Counter<long> M365WriteOps = Meter.CreateCounter<long>("archonai.m365.write.ops");
    public static readonly Counter<long> M365Errors = Meter.CreateCounter<long>("archonai.m365.errors");
    public static readonly Counter<long> M365Retries = Meter.CreateCounter<long>("archonai.m365.retries");
    public static readonly Histogram<double> M365RateLimitRemaining = Meter.CreateHistogram<double>("archonai.m365.ratelimit.remaining");

    // Operations agent metrics
    public static readonly Counter<long> OperationsAnalyses = Meter.CreateCounter<long>("archonai.operations.analyses");
    public static readonly Counter<long> OperationsInefficiencyScans = Meter.CreateCounter<long>("archonai.operations.inefficiency.scans");
    public static readonly Counter<long> OperationsRecommendations = Meter.CreateCounter<long>("archonai.operations.recommendations");
    public static readonly Counter<long> OperationsCoordinations = Meter.CreateCounter<long>("archonai.operations.coordinations");
    public static readonly Counter<long> OperationsReasoningCycles = Meter.CreateCounter<long>("archonai.operations.reasoning.cycles");
    public static readonly Counter<long> OperationsDataFabricQueries = Meter.CreateCounter<long>("archonai.operations.datafabric.queries");
    public static readonly Counter<long> OperationsStrategyLookups = Meter.CreateCounter<long>("archonai.operations.strategy.lookups");
    public static readonly Histogram<double> OperationsCoordinationDurationMs = Meter.CreateHistogram<double>("archonai.operations.coordination.duration.ms");

    // Finance agent metrics
    public static readonly Counter<long> FinanceAnalyses = Meter.CreateCounter<long>("archonai.finance.analyses");
    public static readonly Counter<long> FinanceAnomalyScans = Meter.CreateCounter<long>("archonai.finance.anomaly.scans");
    public static readonly Counter<long> FinanceSummaries = Meter.CreateCounter<long>("archonai.finance.summaries");
    public static readonly Counter<long> FinanceBudgetWorkflows = Meter.CreateCounter<long>("archonai.finance.budget.workflows");
    public static readonly Counter<long> FinanceDataFabricQueries = Meter.CreateCounter<long>("archonai.finance.datafabric.queries");

    // Sales agent metrics
    public static readonly Counter<long> SalesPipelineAnalyses = Meter.CreateCounter<long>("archonai.sales.pipeline.analyses");
    public static readonly Counter<long> SalesOpportunitiesPrioritized = Meter.CreateCounter<long>("archonai.sales.opportunities.prioritized");
    public static readonly Counter<long> SalesOutreachRecommendations = Meter.CreateCounter<long>("archonai.sales.outreach.recommendations");
    public static readonly Counter<long> SalesMetricsGenerated = Meter.CreateCounter<long>("archonai.sales.metrics.generated");
    public static readonly Counter<long> SalesDataFabricQueries = Meter.CreateCounter<long>("archonai.sales.datafabric.queries");

    // Marketing agent metrics
    public static readonly Counter<long> MarketingCampaignAnalyses = Meter.CreateCounter<long>("archonai.marketing.campaign.analyses");
    public static readonly Counter<long> MarketingStrategyRecommendations = Meter.CreateCounter<long>("archonai.marketing.strategy.recommendations");
    public static readonly Counter<long> MarketingMetricsGenerated = Meter.CreateCounter<long>("archonai.marketing.metrics.generated");
    public static readonly Counter<long> MarketingDataFabricQueries = Meter.CreateCounter<long>("archonai.marketing.datafabric.queries");

    // Support agent metrics
    public static readonly Counter<long> SupportTicketAnalyses = Meter.CreateCounter<long>("archonai.support.ticket.analyses");
    public static readonly Counter<long> SupportRecurringIssueScans = Meter.CreateCounter<long>("archonai.support.recurring.issue.scans");
    public static readonly Counter<long> SupportAutoResponses = Meter.CreateCounter<long>("archonai.support.auto.responses");
    public static readonly Counter<long> SupportDataFabricQueries = Meter.CreateCounter<long>("archonai.support.datafabric.queries");

    // Admin service metrics
    public static readonly Counter<long> AdminAgentQueries = Meter.CreateCounter<long>("archonai.admin.agent.queries");
    public static readonly Counter<long> AdminWorkflowQueries = Meter.CreateCounter<long>("archonai.admin.workflow.queries");
    public static readonly Counter<long> AdminPolicyUpdates = Meter.CreateCounter<long>("archonai.admin.policy.updates");
    public static readonly Counter<long> AdminMonitoringSnapshots = Meter.CreateCounter<long>("archonai.admin.monitoring.snapshots");

    // RBAC metrics
    public static readonly Counter<long> RbacAccessChecks = Meter.CreateCounter<long>("archonai.rbac.access.checks");
    public static readonly Counter<long> RbacAccessDenials = Meter.CreateCounter<long>("archonai.rbac.access.denials");
    public static readonly Counter<long> RbacRoleChanges = Meter.CreateCounter<long>("archonai.rbac.role.changes");
    public static readonly Counter<long> RbacPolicyChanges = Meter.CreateCounter<long>("archonai.rbac.policy.changes");

    // Observability service metrics
    public static readonly Counter<long> ObservabilityTracesCollected = Meter.CreateCounter<long>("archonai.observability.traces.collected");
    public static readonly Counter<long> ObservabilityMetricsSnapshots = Meter.CreateCounter<long>("archonai.observability.metrics.snapshots");
    public static readonly Counter<long> ObservabilityAlerts = Meter.CreateCounter<long>("archonai.observability.alerts");

    // Audit log metrics
    public static readonly Counter<long> AuditEntriesRecorded = Meter.CreateCounter<long>("archonai.audit.entries.recorded");
    public static readonly Counter<long> AuditIntegrityChecks = Meter.CreateCounter<long>("archonai.audit.integrity.checks");

    // Workflow design metrics
    public static readonly Counter<long> WorkflowDesignsCreated = Meter.CreateCounter<long>("archonai.workflow.designs.created");
    public static readonly Counter<long> WorkflowDesignsValidated = Meter.CreateCounter<long>("archonai.workflow.designs.validated");
    public static readonly Counter<long> WorkflowDesignsExecuted = Meter.CreateCounter<long>("archonai.workflow.designs.executed");

    // Monitoring dashboard metrics
    public static readonly Counter<long> MonitoringDashboardsGenerated = Meter.CreateCounter<long>("archonai.monitoring.dashboards.generated");
    public static readonly Counter<long> MonitoringWorkflowMetricsQueries = Meter.CreateCounter<long>("archonai.monitoring.workflow.metrics.queries");
    public static readonly Counter<long> MonitoringAgentHealthQueries = Meter.CreateCounter<long>("archonai.monitoring.agent.health.queries");
    public static readonly Counter<long> MonitoringSystemPerformanceQueries = Meter.CreateCounter<long>("archonai.monitoring.system.performance.queries");

    // Control plane metrics
    public static readonly Counter<long> ControlPlaneTenantOps = Meter.CreateCounter<long>("archonai.controlplane.tenant.ops");
    public static readonly Counter<long> ControlPlaneWorkflowOps = Meter.CreateCounter<long>("archonai.controlplane.workflow.ops");
    public static readonly Counter<long> ControlPlaneAgentOps = Meter.CreateCounter<long>("archonai.controlplane.agent.ops");
    public static readonly Counter<long> ControlPlanePolicyOps = Meter.CreateCounter<long>("archonai.controlplane.policy.ops");
    public static readonly Counter<long> ControlPlaneConfigOps = Meter.CreateCounter<long>("archonai.controlplane.config.ops");

    // Agent registry metrics
    public static readonly Counter<long> AgentRegistryRegistrations = Meter.CreateCounter<long>("archonai.agentregistry.registrations");
    public static readonly Counter<long> AgentRegistryCapabilityUpdates = Meter.CreateCounter<long>("archonai.agentregistry.capability.updates");
    public static readonly Counter<long> AgentRegistryStatusChanges = Meter.CreateCounter<long>("archonai.agentregistry.status.changes");
    public static readonly Counter<long> AgentRegistryMetricsRecorded = Meter.CreateCounter<long>("archonai.agentregistry.metrics.recorded");

    // Strategy library metrics
    public static readonly Counter<long> StrategyLibraryCreated = Meter.CreateCounter<long>("archonai.strategylibrary.created");
    public static readonly Counter<long> StrategyLibraryExecutionsRecorded = Meter.CreateCounter<long>("archonai.strategylibrary.executions.recorded");
    public static readonly Counter<long> StrategyLibraryRankQueries = Meter.CreateCounter<long>("archonai.strategylibrary.rank.queries");
    public static readonly Counter<long> StrategyLibraryComparisons = Meter.CreateCounter<long>("archonai.strategylibrary.comparisons");

    // Workflow simulation metrics
    public static readonly Counter<long> WorkflowSimulationRuns = Meter.CreateCounter<long>("archonai.workflowsimulation.runs");
    public static readonly Counter<long> WorkflowSimulationPredictions = Meter.CreateCounter<long>("archonai.workflowsimulation.predictions");
    public static readonly Counter<long> WorkflowSimulationResourceEstimates = Meter.CreateCounter<long>("archonai.workflowsimulation.resource.estimates");
    public static readonly Counter<long> WorkflowSimulationLatencyEstimates = Meter.CreateCounter<long>("archonai.workflowsimulation.latency.estimates");

    // Runtime health metrics
    public static readonly Counter<long> RuntimeHealthChecks = Meter.CreateCounter<long>("archonai.runtime.health.checks");
    public static readonly Counter<long> RuntimeAgentFailures = Meter.CreateCounter<long>("archonai.runtime.agent.failures");
    public static readonly Counter<long> RuntimeTaskTimeouts = Meter.CreateCounter<long>("archonai.runtime.task.timeouts");
    public static readonly Counter<long> RuntimeRecoveriesAttempted = Meter.CreateCounter<long>("archonai.runtime.recoveries.attempted");
    public static readonly Counter<long> RuntimeRecoveriesSucceeded = Meter.CreateCounter<long>("archonai.runtime.recoveries.succeeded");
    public static readonly Counter<long> RuntimeAgentRestarts = Meter.CreateCounter<long>("archonai.runtime.agent.restarts");
    public static readonly Counter<long> RuntimeTaskRetries = Meter.CreateCounter<long>("archonai.runtime.task.retries");
    public static readonly Counter<long> RuntimeWorkflowRollbacks = Meter.CreateCounter<long>("archonai.runtime.workflow.rollbacks");
    public static readonly Histogram<double> RuntimeQueueBacklog = Meter.CreateHistogram<double>("archonai.runtime.queue.backlog");

    // Agent coordination metrics
    public static readonly Counter<long> CoordinationSupportRequestsSent = Meter.CreateCounter<long>("archonai.coordination.support.requests.sent");
    public static readonly Counter<long> CoordinationSupportRequestsReceived = Meter.CreateCounter<long>("archonai.coordination.support.requests.received");
    public static readonly Counter<long> CoordinationSupportRequestsCompleted = Meter.CreateCounter<long>("archonai.coordination.support.requests.completed");
    public static readonly Counter<long> CoordinationSupportRequestsTimedOut = Meter.CreateCounter<long>("archonai.coordination.support.requests.timedout");
    public static readonly Counter<long> CoordinationKnowledgeShares = Meter.CreateCounter<long>("archonai.coordination.knowledge.shares");
    public static readonly Counter<long> CoordinationDelegations = Meter.CreateCounter<long>("archonai.coordination.delegations");
    public static readonly Counter<long> CoordinationDelegationsCompleted = Meter.CreateCounter<long>("archonai.coordination.delegations.completed");
    public static readonly Counter<long> CoordinationDelegationsTimedOut = Meter.CreateCounter<long>("archonai.coordination.delegations.timedout");

    // Gateway metrics
    public static readonly Counter<long> GatewayRequestsTotal = Meter.CreateCounter<long>("archonai.gateway.requests.total");
    public static readonly Counter<long> GatewayRequestsFailed = Meter.CreateCounter<long>("archonai.gateway.requests.failed");
    public static readonly Counter<long> GatewayAuthFailures = Meter.CreateCounter<long>("archonai.gateway.auth.failures");
    public static readonly Counter<long> GatewayRateLimitHits = Meter.CreateCounter<long>("archonai.gateway.ratelimit.hits");
    public static readonly Histogram<double> GatewayRequestDurationMs = Meter.CreateHistogram<double>("archonai.gateway.request.duration.ms");
}
