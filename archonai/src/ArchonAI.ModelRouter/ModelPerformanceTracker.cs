using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models.Routing;
using ArchonAI.Core.Models.Trace;
using Microsoft.Extensions.Options;

namespace ArchonAI.ModelRouter;

public sealed class ModelPerformanceTracker : IModelPerformanceTracker
{
    private readonly ConcurrentDictionary<string, ModelMetrics> _metrics = new(StringComparer.OrdinalIgnoreCase);
    private readonly ITraceStore _traceStore;
    private readonly ModelRouterOptions _options;

    public ModelPerformanceTracker(ITraceStore traceStore, IOptions<ModelRouterOptions> options)
    {
        _traceStore = traceStore;
        _options = options.Value;
    }

    public void RecordOutcome(string provider, string model, bool success, double latencyMs, double cost, double? accuracy)
    {
        string key = BuildKey(provider, model);
        _metrics.AddOrUpdate(
            key,
            _ => new ModelMetrics(provider, model, success, latencyMs, cost, accuracy),
            (_, existing) =>
            {
                existing.Record(success, latencyMs, cost, accuracy);
                return existing;
            });

        _ = TraceOutcomeAsync(provider, model, success, latencyMs, cost, accuracy);
    }

    public ModelPerformanceScore? GetScore(string provider, string model)
    {
        string key = BuildKey(provider, model);
        return _metrics.TryGetValue(key, out ModelMetrics? metrics) ? metrics.ToScore() : null;
    }

    public IReadOnlyList<ModelPerformanceScore> GetAllScores()
    {
        return _metrics.Values.Select(m => m.ToScore()).ToList();
    }

    public ModelPerformanceScore? GetBestModelForStrategy(string strategy)
    {
        var scores = GetAllScores();
        if (scores.Count == 0) return null;

        var eligible = scores.Where(s => s.SampleCount >= _options.MinSamplesForAdaptive).ToList();
        if (eligible.Count == 0) return null;

        return strategy.ToLowerInvariant() switch
        {
            "cost" => eligible.OrderBy(s => s.AverageCostPerRequest).ThenByDescending(s => s.SuccessRate).FirstOrDefault(),
            "latency" => eligible.OrderBy(s => s.AverageLatencyMs).ThenByDescending(s => s.SuccessRate).FirstOrDefault(),
            "quality" => eligible.OrderByDescending(s => s.AccuracyRate).ThenByDescending(s => s.SuccessRate).FirstOrDefault(),
            _ => eligible.OrderByDescending(s => s.CompositeScore).FirstOrDefault()
        };
    }

    private async System.Threading.Tasks.Task TraceOutcomeAsync(string provider, string model, bool success, double latencyMs, double cost, double? accuracy)
    {
        try
        {
            await _traceStore.RecordAsync(new TraceEntry(
                Id: Guid.NewGuid(),
                Scope: "model-router",
                Category: "performance-tracking",
                Message: $"Model outcome recorded: {provider}/{model} success={success}",
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = provider,
                    ["model"] = model,
                    ["success"] = success.ToString(),
                    ["latencyMs"] = latencyMs.ToString("F1"),
                    ["cost"] = cost.ToString("F6"),
                    ["accuracy"] = accuracy?.ToString("F3") ?? "N/A"
                },
                RecordedAtUtc: DateTimeOffset.UtcNow));
        }
        catch
        {
            // Telemetry failures must not break routing
        }
    }

    private static string BuildKey(string provider, string model) => $"{provider}::{model}";

    private sealed class ModelMetrics
    {
        private readonly object _lock = new();
        private double _totalLatencyMs;
        private double _totalCost;
        private double _totalAccuracy;
        private int _accuracySamples;
        private int _totalCount;
        private int _successCount;

        public string Provider { get; }
        public string Model { get; }

        public ModelMetrics(string provider, string model, bool success, double latencyMs, double cost, double? accuracy)
        {
            Provider = provider;
            Model = model;
            Record(success, latencyMs, cost, accuracy);
        }

        public void Record(bool success, double latencyMs, double cost, double? accuracy)
        {
            lock (_lock)
            {
                _totalCount++;
                if (success) _successCount++;
                _totalLatencyMs += latencyMs;
                _totalCost += cost;
                if (accuracy.HasValue)
                {
                    _totalAccuracy += accuracy.Value;
                    _accuracySamples++;
                }
            }
        }

        public ModelPerformanceScore ToScore()
        {
            lock (_lock)
            {
                double avgLatency = _totalCount > 0 ? _totalLatencyMs / _totalCount : 0;
                double avgCost = _totalCount > 0 ? _totalCost / _totalCount : 0;
                double accuracyRate = _accuracySamples > 0 ? _totalAccuracy / _accuracySamples : 0;
                double successRate = _totalCount > 0 ? (double)_successCount / _totalCount : 0;

                double composite = ComputeComposite(avgLatency, avgCost, accuracyRate, successRate);

                return new ModelPerformanceScore(
                    Provider: Provider,
                    Model: Model,
                    AverageLatencyMs: avgLatency,
                    AverageCostPerRequest: avgCost,
                    AccuracyRate: accuracyRate,
                    SuccessRate: successRate,
                    SampleCount: _totalCount,
                    CompositeScore: composite,
                    LastUpdatedUtc: DateTimeOffset.UtcNow);
            }
        }

        private static double ComputeComposite(double latency, double cost, double accuracy, double successRate)
        {
            // Normalize latency: lower is better, cap at 10s
            double latencyScore = Math.Max(0, 1.0 - (latency / 10_000.0));
            // Normalize cost: lower is better, cap at $1
            double costScore = Math.Max(0, 1.0 - (cost / 1.0));

            // Weighted composite: success and accuracy matter most
            return (successRate * 0.35)
                + (accuracy * 0.30)
                + (latencyScore * 0.20)
                + (costScore * 0.15);
        }
    }
}
