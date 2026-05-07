using System.Diagnostics;
using Observability = ArchonAI.Common.Observability;

namespace ArchonAI.Api.Security;

/// <summary>
/// Enriches every API request with structured correlation properties for distributed tracing.
/// Propagates X-Correlation-Id, extracts tenant/user context from JWT claims,
/// and creates an Activity span for end-to-end request tracing.
/// </summary>
public sealed class RequestCorrelationMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const string WorkflowIdHeader = "X-Workflow-Id";
    private const string AgentIdHeader = "X-Agent-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestCorrelationMiddleware> _logger;

    public RequestCorrelationMiddleware(RequestDelegate next, ILogger<RequestCorrelationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // --- Correlation ID ---
        if (!context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId)
            || string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }
        var corrId = correlationId.ToString();
        context.Items["CorrelationId"] = corrId;
        context.Response.Headers[CorrelationIdHeader] = corrId;

        // --- Optional workflow / agent context from headers ---
        var workflowId = context.Request.Headers.TryGetValue(WorkflowIdHeader, out var wfId) ? wfId.ToString() : null;
        var agentId = context.Request.Headers.TryGetValue(AgentIdHeader, out var aId) ? aId.ToString() : null;

        // --- Tenant / user from JWT claims ---
        var tenantId = context.User?.FindFirst("tenant_id")?.Value;
        var userId = context.User?.FindFirst("sub")?.Value ?? context.User?.FindFirst("user_id")?.Value;
        var userRole = context.User?.FindFirst("role")?.Value;

        // --- Create structured log scope ---
        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = corrId,
            ["TenantId"] = tenantId,
            ["UserId"] = userId,
            ["UserRole"] = userRole,
            ["WorkflowId"] = workflowId,
            ["AgentId"] = agentId,
            ["RequestMethod"] = context.Request.Method,
            ["RequestPath"] = context.Request.Path.Value,
        });

        // --- Create Activity span for distributed tracing ---
        using var activity = Observability.Telemetry.ActivitySource.StartActivity(
            $"HTTP {context.Request.Method} {context.Request.Path}",
            ActivityKind.Server);

        if (activity is not null)
        {
            activity.SetTag("correlation.id", corrId);
            activity.SetTag("http.method", context.Request.Method);
            activity.SetTag("http.path", context.Request.Path.Value);
            if (tenantId is not null) activity.SetTag("tenant.id", tenantId);
            if (userId is not null) activity.SetTag("user.id", userId);
            if (workflowId is not null) activity.SetTag("workflow.id", workflowId);
            if (agentId is not null) activity.SetTag("agent.id", agentId);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            activity?.SetTag("error", true);
            activity?.SetTag("error.type", ex.GetType().Name);
            _logger.LogError(ex, "Request failed: {Method} {Path} [CorrelationId={CorrelationId}]",
                context.Request.Method, context.Request.Path, corrId);
            throw;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("http.status_code", context.Response.StatusCode);
            activity?.SetTag("http.duration_ms", sw.Elapsed.TotalMilliseconds);

            if (context.Response.StatusCode >= 500)
            {
                _logger.LogWarning("Request completed with server error: {StatusCode} {Method} {Path} ({DurationMs}ms)",
                    context.Response.StatusCode, context.Request.Method, context.Request.Path, sw.Elapsed.TotalMilliseconds);
            }
        }
    }
}
