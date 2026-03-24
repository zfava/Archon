using System.Collections.Concurrent;

namespace ArchonAI.Connectors.Framework;

public sealed record ConnectorCounters(
    long TotalRequests,
    long SuccessfulRequests,
    long FailedRequests,
    long RateLimitHits,
    double AverageLatencyMs,
    DateTimeOffset LastRequestAtUtc);

public sealed class ConnectorShadowMetrics
{
    private readonly ConcurrentDictionary<string, MutableCounters> _counters = new();

    public void RecordRequest(string systemName, bool success, double latencyMs)
    {
        var c = _counters.GetOrAdd(systemName, _ => new MutableCounters());

        Interlocked.Increment(ref c.TotalRequests);

        if (success)
        {
            Interlocked.Increment(ref c.SuccessfulRequests);
        }
        else
        {
            Interlocked.Increment(ref c.FailedRequests);
        }

        // Accumulate latency (stored as ticks * 100 to preserve two decimal places)
        Interlocked.Add(ref c.TotalLatencyTicksX100, (long)(latencyMs * 100));
        c.LastRequestAtUtc = DateTimeOffset.UtcNow;
    }

    public void RecordRateLimitHit(string systemName)
    {
        var c = _counters.GetOrAdd(systemName, _ => new MutableCounters());
        Interlocked.Increment(ref c.RateLimitHits);
    }

    public ConnectorCounters GetCounters(string systemName)
    {
        if (!_counters.TryGetValue(systemName, out var c))
        {
            return new ConnectorCounters(0, 0, 0, 0, 0.0, DateTimeOffset.MinValue);
        }

        return c.ToSnapshot();
    }

    public IReadOnlyDictionary<string, ConnectorCounters> GetAll()
    {
        var snapshot = new Dictionary<string, ConnectorCounters>(_counters.Count);
        foreach (var (key, value) in _counters)
        {
            snapshot[key] = value.ToSnapshot();
        }

        return snapshot;
    }

    public void Reset(string systemName)
    {
        _counters.TryRemove(systemName, out _);
    }

    private sealed class MutableCounters
    {
        public long TotalRequests;
        public long SuccessfulRequests;
        public long FailedRequests;
        public long RateLimitHits;
        public long TotalLatencyTicksX100;
        public DateTimeOffset LastRequestAtUtc;

        public ConnectorCounters ToSnapshot()
        {
            long total = Interlocked.Read(ref TotalRequests);
            long success = Interlocked.Read(ref SuccessfulRequests);
            long failed = Interlocked.Read(ref FailedRequests);
            long rateLimitHits = Interlocked.Read(ref RateLimitHits);
            long latencyTicks = Interlocked.Read(ref TotalLatencyTicksX100);

            double avgLatency = total > 0
                ? Math.Round((double)latencyTicks / 100.0 / total, 2)
                : 0.0;

            return new ConnectorCounters(total, success, failed, rateLimitHits, avgLatency, LastRequestAtUtc);
        }
    }
}
