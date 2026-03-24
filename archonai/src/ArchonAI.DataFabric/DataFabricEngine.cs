using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.DataFabric;
using Microsoft.Extensions.Options;

namespace ArchonAI.DataFabric;

public sealed class DataFabricEngine : IDataFabricEngine
{
    private readonly IMemoryStore _memoryStore;
    private readonly IReadOnlyDictionary<string, IConnector> _connectors;
    private readonly DataFabricOptions _options;

    public DataFabricEngine(
        IMemoryStore memoryStore,
        IEnumerable<IConnector> connectors,
        IOptions<DataFabricOptions> options)
    {
        _memoryStore = memoryStore;
        _connectors = connectors.ToDictionary(c => c.SystemName, StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task<DataFabricQueryResult> QueryEnterpriseDataAsync(
        string source,
        IReadOnlyDictionary<string, string> filters,
        IReadOnlyDictionary<string, string> schemaMapping,
        IReadOnlyList<string> permissions,
        string consumerType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var grantedPermissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> requiredPermissions = consumerType.Equals("planner", StringComparison.OrdinalIgnoreCase)
            ? _options.PlannerPermissions
            : _options.AgentPermissions;

        if (!requiredPermissions.Any(grantedPermissions.Contains))
        {
            return new DataFabricQueryResult(
                IsAllowed: false,
                Reason: $"Missing required data-fabric permission ({string.Join('|', requiredPermissions)}).",
                Rows: Array.Empty<IReadOnlyDictionary<string, string>>(),
                AppliedSchemaMapping: schemaMapping,
                QueriedAtUtc: DateTimeOffset.UtcNow);
        }

        List<string> sources = ResolveSources(source);
        var rows = new List<IReadOnlyDictionary<string, string>>();

        foreach (string resolvedSource in sources)
        {
            string scope = $"enterprise-data:{resolvedSource}";
            IReadOnlyList<MemoryRecord> records = await _memoryStore.QueryByScopeAsync(scope, cancellationToken);

            foreach (MemoryRecord record in records.Take(_options.MaxRows))
            {
                var parsed = ParseRecord(record);
                var normalized = NormalizeAndMap(parsed, schemaMapping);

                if (!MatchesFilters(normalized, filters))
                {
                    continue;
                }

                var enriched = new Dictionary<string, string>(normalized, StringComparer.OrdinalIgnoreCase)
                {
                    ["source"] = resolvedSource,
                    ["connectorAvailable"] = _connectors.ContainsKey(resolvedSource).ToString()
                };

                rows.Add(enriched);
            }
        }

        return new DataFabricQueryResult(
            IsAllowed: true,
            Reason: "ok",
            Rows: rows.Take(_options.MaxRows).ToArray(),
            AppliedSchemaMapping: schemaMapping,
            QueriedAtUtc: DateTimeOffset.UtcNow);
    }

    private List<string> ResolveSources(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source == "*")
        {
            return _options.AllowedSources.ToList();
        }

        var requested = source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return requested
            .Where(item => _options.AllowedSources.Contains(item, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> ParseRecord(MemoryRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.Content))
        {
            try
            {
                var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(record.Content);
                if (json is not null && json.Count > 0)
                {
                    return json.ToDictionary(
                        kv => kv.Key,
                        kv => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? string.Empty : kv.Value.ToString(),
                        StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (JsonException)
            {
                // Content is not valid JSON — fall back to metadata/content wrapping
            }
        }

        var fallback = new Dictionary<string, string>(record.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["content"] = record.Content
        };

        return fallback;
    }

    private static IReadOnlyDictionary<string, string> NormalizeAndMap(
        IReadOnlyDictionary<string, string> input,
        IReadOnlyDictionary<string, string> mapping)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in input)
        {
            string normalizedKey = NormalizeKey(key);
            string targetKey = mapping.GetValueOrDefault(normalizedKey, mapping.GetValueOrDefault(key, normalizedKey));
            normalized[targetKey] = value?.Trim() ?? string.Empty;
        }

        return normalized;
    }

    private static bool MatchesFilters(IReadOnlyDictionary<string, string> row, IReadOnlyDictionary<string, string> filters)
    {
        foreach ((string key, string expected) in filters)
        {
            if (!row.TryGetValue(key, out string? actual))
            {
                return false;
            }

            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return key.Trim().Replace(" ", "_", StringComparison.Ordinal).ToLowerInvariant();
    }
}
