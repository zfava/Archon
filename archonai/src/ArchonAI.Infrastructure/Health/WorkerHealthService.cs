using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Infrastructure.Health;

/// <summary>
/// Lightweight Kestrel-based health endpoint for worker services.
/// Serves /healthz/live (liveness) and /healthz/ready (readiness) on port 8081.
/// Designed to respond in &lt;50ms even under full load.
/// </summary>
public sealed class WorkerHealthService : BackgroundService
{
    private readonly HttpListener _listener;
    private readonly ILogger<WorkerHealthService> _logger;
    private readonly Func<CancellationToken, Task<HealthProbeResult>> _readinessCheck;
    private readonly int _port;

    public WorkerHealthService(
        ILogger<WorkerHealthService> logger,
        Func<CancellationToken, Task<HealthProbeResult>> readinessCheck,
        int port = 8081)
    {
        _logger = logger;
        _readinessCheck = readinessCheck;
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{_port}/");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _listener.Start();
            _logger.LogInformation("Worker health endpoint listening on port {Port}", _port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start worker health endpoint on port {Port}", _port);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(stoppingToken);
                _ = HandleRequestAsync(context, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Health endpoint listener error");
            }
        }

        _listener.Stop();
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string path = context.Request.Url?.AbsolutePath ?? "/";

        try
        {
            if (path == "/healthz/live")
            {
                await WriteJsonResponse(context.Response, 200, new { status = "alive", timestamp = DateTimeOffset.UtcNow });
            }
            else if (path == "/healthz/ready")
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMilliseconds(40)); // Stay under 50ms budget

                try
                {
                    var result = await _readinessCheck(cts.Token);
                    int statusCode = result.IsReady ? 200 : 503;
                    await WriteJsonResponse(context.Response, statusCode, new
                    {
                        status = result.IsReady ? "ready" : "not_ready",
                        checks = result.Checks,
                        timestamp = DateTimeOffset.UtcNow
                    });
                }
                catch (OperationCanceledException)
                {
                    await WriteJsonResponse(context.Response, 503, new
                    {
                        status = "not_ready",
                        reason = "readiness check timed out",
                        timestamp = DateTimeOffset.UtcNow
                    });
                }
            }
            else
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error handling health request for {Path}", path);
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch { /* response already sent */ }
        }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds > 50)
            {
                _logger.LogWarning("Health probe for {Path} took {Ms}ms (exceeds 50ms budget)", path, sw.ElapsedMilliseconds);
            }
        }
    }

    private static async Task WriteJsonResponse(HttpListenerResponse response, int statusCode, object body)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(body,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public override void Dispose()
    {
        _listener.Close();
        base.Dispose();
    }
}

public sealed class HealthProbeResult
{
    public bool IsReady { get; init; }
    public Dictionary<string, HealthCheckEntry> Checks { get; init; } = new();
}

public sealed class HealthCheckEntry
{
    public string Status { get; init; } = "unknown";
    public string? Detail { get; init; }
}
