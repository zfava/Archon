using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArchonAI.Api.Security;

/// <summary>
/// Writes health check results as structured JSON suitable for K8s probes and dashboards.
/// </summary>
public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            timestamp = DateTimeOffset.UtcNow,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds,
                exception = entry.Value.Exception?.Message,
                tags = entry.Value.Tags,
            }),
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }
}
