using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Context;
using Microsoft.Extensions.Options;

namespace ArchonAI.Context;

public sealed class ContextEngine : IContextEngine
{
    private readonly IMemoryStore _memoryStore;
    private readonly ContextOptions _options;

    public ContextEngine(IMemoryStore memoryStore, IOptions<ContextOptions> options)
    {
        _memoryStore = memoryStore;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task<SystemContext> GetContextAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MemoryRecord> records = await _memoryStore.QueryByScopeAsync(_options.Scope, cancellationToken);
        MemoryRecord? latest = records.OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault();

        if (latest is null)
        {
            return new SystemContext(
                OrganizationalGoals: _options.OrganizationalGoals,
                OperationalPriorities: _options.OperationalPriorities,
                EnvironmentConstraints: _options.EnvironmentConstraints,
                HistoricalKnowledge: _options.HistoricalKnowledge,
                UpdatedAtUtc: DateTimeOffset.UtcNow);
        }

        return JsonSerializer.Deserialize<SystemContext>(latest.Content)
               ?? new SystemContext(_options.OrganizationalGoals, _options.OperationalPriorities, _options.EnvironmentConstraints, _options.HistoricalKnowledge, DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task UpdateContextAsync(SystemContext context, CancellationToken cancellationToken = default)
    {
        var record = new MemoryRecord(
            Id: Guid.NewGuid(),
            MemoryType: "system-context",
            Scope: _options.Scope,
            Content: JsonSerializer.Serialize(context),
            Metadata: new Dictionary<string, string>
            {
                ["organizationalGoalsCount"] = context.OrganizationalGoals.Count.ToString(),
                ["operationalPrioritiesCount"] = context.OperationalPriorities.Count.ToString(),
                ["environmentConstraintsCount"] = context.EnvironmentConstraints.Count.ToString(),
                ["historicalKnowledgeCount"] = context.HistoricalKnowledge.Count.ToString()
            },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null);

        await _memoryStore.SaveAsync(record, cancellationToken);
    }
}
