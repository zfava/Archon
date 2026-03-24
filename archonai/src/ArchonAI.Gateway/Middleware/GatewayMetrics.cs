using System.Collections.Concurrent;
using ArchonAI.Common.Observability;

namespace ArchonAI.Gateway.Middleware;

public sealed class GatewayMetrics
{
    private long _totalRequests;
    private long _failedRequests;
    private long _authFailures;
    private long _rateLimitHits;
    private double _totalLatencyMs;
    private long _latencySamples;
    private readonly ConcurrentDictionary<string, long> _routeHits = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;

    public void IncrementTotalRequests()
    {
        Interlocked.Increment(ref _totalRequests);
        Telemetry.GatewayRequestsTotal.Add(1);
    }

    public void IncrementFailedRequests()
    {
        Interlocked.Increment(ref _failedRequests);
        Telemetry.GatewayRequestsFailed.Add(1);
    }

    public void IncrementAuthFailures()
    {
        Interlocked.Increment(ref _authFailures);
        Telemetry.GatewayAuthFailures.Add(1);
    }

    public void IncrementRateLimitHits()
    {
        Interlocked.Increment(ref _rateLimitHits);
        Telemetry.GatewayRateLimitHits.Add(1);
    }

    public void RecordLatency(double ms)
    {
        // Approximate running total (not lock-free perfect, but sufficient for metrics)
        Interlocked.Exchange(ref _totalLatencyMs, _totalLatencyMs + ms);
        Interlocked.Increment(ref _latencySamples);
        Telemetry.GatewayRequestDurationMs.Record(ms);
    }

    public void IncrementRouteHits(string route)
    {
        _routeHits.AddOrUpdate(route, 1, (_, count) => count + 1);
    }

    public object GetSnapshot()
    {
        long total = Interlocked.Read(ref _totalRequests);
        long failed = Interlocked.Read(ref _failedRequests);
        long samples = Interlocked.Read(ref _latencySamples);
        double avgLatency = samples > 0 ? _totalLatencyMs / samples : 0;

        return new
        {
            Status = "active",
            TotalRequests = total,
            FailedRequests = failed,
            AuthFailures = Interlocked.Read(ref _authFailures),
            RateLimitHits = Interlocked.Read(ref _rateLimitHits),
            AverageLatencyMs = Math.Round(avgLatency, 2),
            RouteHits = _routeHits.ToDictionary(kv => kv.Key, kv => kv.Value),
            UptimeSeconds = (long)(DateTimeOffset.UtcNow - _startedAtUtc).TotalSeconds,
            StartedAtUtc = _startedAtUtc
        };
    }
}
