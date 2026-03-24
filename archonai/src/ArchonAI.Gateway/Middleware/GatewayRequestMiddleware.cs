using System.Diagnostics;

namespace ArchonAI.Gateway.Middleware;

public sealed class GatewayRequestMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GatewayRequestMiddleware> _logger;
    private readonly GatewayMetrics _metrics;

    public GatewayRequestMiddleware(RequestDelegate next, ILogger<GatewayRequestMiddleware> logger, GatewayMetrics metrics)
    {
        _next = next;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        _metrics.IncrementTotalRequests();

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _metrics.IncrementFailedRequests();
            _logger.LogError(ex, "Gateway request failed: {Method} {Path}", context.Request.Method, context.Request.Path);
            throw;
        }
        finally
        {
            sw.Stop();
            _metrics.RecordLatency(sw.Elapsed.TotalMilliseconds);

            var statusCode = context.Response.StatusCode;
            if (statusCode == 401) _metrics.IncrementAuthFailures();
            if (statusCode == 429) _metrics.IncrementRateLimitHits();
            if (statusCode >= 500) _metrics.IncrementFailedRequests();

            CategorizeRoute(context.Request.Path, _metrics);
        }
    }

    private static void CategorizeRoute(PathString path, GatewayMetrics metrics)
    {
        string p = path.Value ?? "";
        if (p.StartsWith("/api/v1/connectors", StringComparison.OrdinalIgnoreCase))
            metrics.IncrementRouteHits("connectors");
        else if (p.StartsWith("/api/v1/admin", StringComparison.OrdinalIgnoreCase))
            metrics.IncrementRouteHits("admin");
        else if (p.StartsWith("/api/v1/observability", StringComparison.OrdinalIgnoreCase))
            metrics.IncrementRouteHits("observability");
        else if (p.StartsWith("/api/v1/audit", StringComparison.OrdinalIgnoreCase))
            metrics.IncrementRouteHits("audit");
        else if (p.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            metrics.IncrementRouteHits("api");
        else
            metrics.IncrementRouteHits("other");
    }
}
