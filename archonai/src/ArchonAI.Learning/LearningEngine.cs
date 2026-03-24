using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Learning;
using ArchonAI.Core.Models.Patterns;
using Microsoft.Extensions.Options;

namespace ArchonAI.Learning;

public sealed class LearningEngine : ILearningEngine
{
    private readonly ConcurrentDictionary<string, PatternAggregate> _aggregates = new(StringComparer.OrdinalIgnoreCase);
    private readonly IMemoryStore _memoryStore;
    private readonly IMultiTenantContext _tenantContext;
    private readonly LearningOptions _options;

    public LearningEngine(
        IMemoryStore memoryStore,
        IMultiTenantContext tenantContext,
        IOptions<LearningOptions> options)
    {
        _memoryStore = memoryStore;
        _tenantContext = tenantContext;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task IngestPatternsAsync(
        string tenantId,
        IReadOnlyList<OperationalPattern> patterns,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (patterns.Count == 0)
        {
            return;
        }

        string tenantHash = HashTenant(tenantId);

        foreach (OperationalPattern pattern in patterns)
        {
            _aggregates.AddOrUpdate(
                pattern.PatternType,
                _ => PatternAggregate.Create(pattern.PatternType, tenantHash, pattern.Score),
                (_, current) => current.Add(tenantHash, pattern.Score));
        }

        IReadOnlyList<LearningInsight> insights = BuildInsights();

        using IDisposable globalScope = _tenantContext.BeginTenantScope(_options.GlobalTenantId);
        foreach (LearningInsight insight in insights)
        {
            var record = new MemoryRecord(
                Id: Guid.NewGuid(),
                MemoryType: "intelligence-learning",
                Scope: "learning:global",
                Content: insight.Summary,
                Metadata: new Dictionary<string, string>(insight.Metadata)
                {
                    ["insightType"] = insight.InsightType,
                    ["score"] = insight.Score.ToString("F4"),
                    ["tenantCoverage"] = insight.TenantCoverage.ToString()
                },
                CreatedAtUtc: insight.GeneratedAtUtc,
                ExpiresAtUtc: null);

            await _memoryStore.SaveAsync(record, cancellationToken);
        }
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<LearningInsight>> GetGlobalInsightsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return global::System.Threading.Tasks.Task.FromResult(BuildInsights());
    }

    private IReadOnlyList<LearningInsight> BuildInsights()
    {
        return _aggregates.Values
            .Where(aggregate => aggregate.TenantHashes.Count >= _options.MinTenantCoverageForGlobalPattern)
            .OrderByDescending(aggregate => aggregate.AverageScore)
            .Take(Math.Max(1, _options.MaxInsights))
            .Select(aggregate => new LearningInsight(
                InsightType: aggregate.PatternType,
                Summary: $"Pattern '{aggregate.PatternType}' observed across {aggregate.TenantHashes.Count} tenants. Avg score {aggregate.AverageScore:F2}.",
                Score: aggregate.AverageScore,
                TenantCoverage: aggregate.TenantHashes.Count,
                Metadata: new Dictionary<string, string>
                {
                    ["occurrences"] = aggregate.Occurrences.ToString(),
                    ["totalScore"] = aggregate.TotalScore.ToString("F4")
                },
                GeneratedAtUtc: DateTimeOffset.UtcNow))
            .ToArray();
    }

    private static string HashTenant(string tenantId)
    {
        string normalized = string.IsNullOrWhiteSpace(tenantId) ? "unknown" : tenantId.Trim();
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes[..8]);
    }

    private sealed record PatternAggregate(
        string PatternType,
        int Occurrences,
        double TotalScore,
        IReadOnlySet<string> TenantHashes)
    {
        public double AverageScore => Occurrences <= 0 ? 0 : TotalScore / Occurrences;

        public static PatternAggregate Create(string patternType, string tenantHash, double score)
        {
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tenantHash };
            return new PatternAggregate(patternType, 1, score, hashes);
        }

        public PatternAggregate Add(string tenantHash, double score)
        {
            var hashes = new HashSet<string>(TenantHashes, StringComparer.OrdinalIgnoreCase) { tenantHash };
            return this with
            {
                Occurrences = Occurrences + 1,
                TotalScore = TotalScore + score,
                TenantHashes = hashes
            };
        }
    }
}
