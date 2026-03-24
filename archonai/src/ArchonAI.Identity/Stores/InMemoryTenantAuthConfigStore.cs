using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Identity.Stores;

public sealed class InMemoryTenantAuthConfigStore : ITenantAuthConfigStore
{
    private readonly ConcurrentDictionary<Guid, TenantAuthConfig> _configs = new();

    public Task<TenantAuthConfig?> GetByOrganizationIdAsync(Guid orgId, CancellationToken ct = default)
    {
        var config = _configs.Values.FirstOrDefault(c => c.OrganizationId == orgId && c.IsEnabled);
        return Task.FromResult(config);
    }

    public Task<TenantAuthConfig?> GetByIdAsync(Guid configId, CancellationToken ct = default)
    {
        _configs.TryGetValue(configId, out var config);
        return Task.FromResult(config);
    }

    public Task<IReadOnlyList<TenantAuthConfig>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<TenantAuthConfig> result = _configs.Values.ToList();
        return Task.FromResult(result);
    }

    public Task<TenantAuthConfig> CreateAsync(TenantAuthConfig config, CancellationToken ct = default)
    {
        if (!_configs.TryAdd(config.Id, config))
            throw new InvalidOperationException($"TenantAuthConfig {config.Id} already exists.");
        return Task.FromResult(config);
    }

    public Task<TenantAuthConfig> UpdateAsync(TenantAuthConfig config, CancellationToken ct = default)
    {
        _configs[config.Id] = config;
        return Task.FromResult(config);
    }

    public Task DeleteAsync(Guid configId, CancellationToken ct = default)
    {
        _configs.TryRemove(configId, out _);
        return Task.CompletedTask;
    }
}
