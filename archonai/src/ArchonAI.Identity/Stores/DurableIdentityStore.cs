using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Identity.Stores;

/// <summary>
/// File-backed persistent identity store. Persists users, organizations,
/// memberships, refresh tokens, and invite tokens to a single JSON file on disk.
/// </summary>
public sealed class DurableIdentityStore : IUserStore, IOrganizationStore, IMembershipStore,
    IRefreshTokenStore, IInviteTokenStore, IDisposable
{
    private readonly ConcurrentDictionary<Guid, UserIdentity> _users = new();
    private readonly ConcurrentDictionary<Guid, Organization> _orgs = new();
    private readonly ConcurrentDictionary<(Guid UserId, Guid OrgId), Membership> _memberships = new();
    private readonly ConcurrentDictionary<Guid, RefreshToken> _tokens = new();
    private readonly ConcurrentDictionary<Guid, InviteToken> _invites = new();

    private readonly string _filePath;
    private readonly ILogger<DurableIdentityStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public DurableIdentityStore(
        IOptions<IdentityOptions> options,
        ILogger<DurableIdentityStore> logger)
    {
        _logger = logger;
        _filePath = options.Value.PersistencePath ?? Path.Combine(
            AppContext.BaseDirectory, "data", "identity-state.json");
        LoadFromDisk();
    }

    // ── IUserStore ────────────────────────────────────────────

    global::System.Threading.Tasks.Task<UserIdentity?> IUserStore.GetByIdAsync(Guid userId, CancellationToken ct)
    {
        _users.TryGetValue(userId, out var user);
        return global::System.Threading.Tasks.Task.FromResult(user);
    }

    global::System.Threading.Tasks.Task<UserIdentity?> IUserStore.GetByEmailAsync(string email, CancellationToken ct)
    {
        var user = _users.Values.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        return global::System.Threading.Tasks.Task.FromResult(user);
    }

    global::System.Threading.Tasks.Task<UserIdentity> IUserStore.CreateAsync(UserIdentity user, CancellationToken ct)
    {
        if (!_users.TryAdd(user.Id, user))
            throw new InvalidOperationException($"User {user.Id} already exists.");
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(user);
    }

    global::System.Threading.Tasks.Task<UserIdentity> IUserStore.UpdateAsync(UserIdentity user, CancellationToken ct)
    {
        _users[user.Id] = user;
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(user);
    }

    global::System.Threading.Tasks.Task<IReadOnlyList<UserIdentity>> IUserStore.ListByOrganizationAsync(Guid orgId, CancellationToken ct)
    {
        IReadOnlyList<UserIdentity> result = _users.Values.Where(u => u.OrganizationId == orgId).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    // ── IOrganizationStore ────────────────────────────────────

    global::System.Threading.Tasks.Task<Organization?> IOrganizationStore.GetByIdAsync(Guid orgId, CancellationToken ct)
    {
        _orgs.TryGetValue(orgId, out var org);
        return global::System.Threading.Tasks.Task.FromResult(org);
    }

    global::System.Threading.Tasks.Task<Organization?> IOrganizationStore.GetBySlugAsync(string slug, CancellationToken ct)
    {
        var org = _orgs.Values.FirstOrDefault(o => o.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        return global::System.Threading.Tasks.Task.FromResult(org);
    }

    global::System.Threading.Tasks.Task<Organization> IOrganizationStore.CreateAsync(Organization org, CancellationToken ct)
    {
        if (!_orgs.TryAdd(org.Id, org))
            throw new InvalidOperationException($"Organization {org.Id} already exists.");
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(org);
    }

    global::System.Threading.Tasks.Task<IReadOnlyList<Organization>> IOrganizationStore.ListAsync(CancellationToken ct)
    {
        IReadOnlyList<Organization> result = _orgs.Values.ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    // ── IMembershipStore ──────────────────────────────────────

    global::System.Threading.Tasks.Task<Membership> IMembershipStore.CreateAsync(Membership membership, CancellationToken ct)
    {
        var key = (membership.UserId, membership.OrganizationId);
        if (!_memberships.TryAdd(key, membership))
            throw new InvalidOperationException("Membership already exists.");
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(membership);
    }

    global::System.Threading.Tasks.Task<Membership?> IMembershipStore.GetAsync(Guid userId, Guid orgId, CancellationToken ct)
    {
        _memberships.TryGetValue((userId, orgId), out var m);
        return global::System.Threading.Tasks.Task.FromResult(m);
    }

    global::System.Threading.Tasks.Task<IReadOnlyList<Membership>> IMembershipStore.ListByUserAsync(Guid userId, CancellationToken ct)
    {
        IReadOnlyList<Membership> result = _memberships.Values.Where(m => m.UserId == userId).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    global::System.Threading.Tasks.Task<IReadOnlyList<Membership>> IMembershipStore.ListByOrganizationAsync(Guid orgId, CancellationToken ct)
    {
        IReadOnlyList<Membership> result = _memberships.Values.Where(m => m.OrganizationId == orgId).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    global::System.Threading.Tasks.Task IMembershipStore.RemoveAsync(Guid userId, Guid orgId, CancellationToken ct)
    {
        _memberships.TryRemove((userId, orgId), out _);
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    // ── IRefreshTokenStore ────────────────────────────────────

    global::System.Threading.Tasks.Task<RefreshToken> IRefreshTokenStore.CreateAsync(RefreshToken token, CancellationToken ct)
    {
        _tokens[token.Id] = token;
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(token);
    }

    global::System.Threading.Tasks.Task<RefreshToken?> IRefreshTokenStore.GetByHashAsync(string tokenHash, CancellationToken ct)
    {
        var token = _tokens.Values.FirstOrDefault(t =>
            t.TokenHash == tokenHash && !t.IsRevoked && t.ExpiresAtUtc > DateTimeOffset.UtcNow);
        return global::System.Threading.Tasks.Task.FromResult(token);
    }

    global::System.Threading.Tasks.Task IRefreshTokenStore.RevokeAsync(Guid tokenId, CancellationToken ct)
    {
        if (_tokens.TryGetValue(tokenId, out var existing))
        {
            _tokens[tokenId] = existing with { IsRevoked = true };
            ScheduleFlush();
        }
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    global::System.Threading.Tasks.Task IRefreshTokenStore.RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        foreach (var kvp in _tokens.Where(t => t.Value.UserId == userId && !t.Value.IsRevoked))
            _tokens[kvp.Key] = kvp.Value with { IsRevoked = true };
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    // ── IInviteTokenStore ─────────────────────────────────────

    global::System.Threading.Tasks.Task<InviteToken> IInviteTokenStore.CreateAsync(InviteToken invite, CancellationToken ct)
    {
        _invites[invite.Id] = invite;
        ScheduleFlush();
        return global::System.Threading.Tasks.Task.FromResult(invite);
    }

    global::System.Threading.Tasks.Task<InviteToken?> IInviteTokenStore.GetByHashAsync(string tokenHash, CancellationToken ct)
    {
        var invite = _invites.Values.FirstOrDefault(i =>
            i.TokenHash == tokenHash && !i.IsAccepted && i.ExpiresAtUtc > DateTimeOffset.UtcNow);
        return global::System.Threading.Tasks.Task.FromResult(invite);
    }

    global::System.Threading.Tasks.Task IInviteTokenStore.AcceptAsync(Guid inviteId, CancellationToken ct)
    {
        if (_invites.TryGetValue(inviteId, out var existing))
        {
            _invites[inviteId] = existing with { IsAccepted = true };
            ScheduleFlush();
        }
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    // ── Persistence ───────────────────────────────────────────

    private void ScheduleFlush() => _ = FlushAsync();

    internal async global::System.Threading.Tasks.Task FlushAsync()
    {
        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)))
            return;
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var snapshot = new IdentitySnapshot(
                _users.Values.ToList(),
                _orgs.Values.ToList(),
                _memberships.Values.ToList(),
                _tokens.Values.ToList(),
                _invites.Values.ToList());
            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush identity state to {Path}", _filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No identity state file at {Path}, starting fresh", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var snapshot = JsonSerializer.Deserialize<IdentitySnapshot>(json, JsonOpts);
            if (snapshot is not null)
            {
                foreach (var u in snapshot.Users) _users[u.Id] = u;
                foreach (var o in snapshot.Organizations) _orgs[o.Id] = o;
                foreach (var m in snapshot.Memberships) _memberships[(m.UserId, m.OrganizationId)] = m;
                foreach (var t in snapshot.RefreshTokens) _tokens[t.Id] = t;
                foreach (var i in snapshot.InviteTokens) _invites[i.Id] = i;

                _logger.LogInformation(
                    "Loaded identity state: {Users} users, {Orgs} orgs, {Memberships} memberships",
                    snapshot.Users.Count, snapshot.Organizations.Count, snapshot.Memberships.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load identity state from {Path}", _filePath);
        }
    }

    public void Dispose() => _writeLock.Dispose();

    private sealed record IdentitySnapshot(
        List<UserIdentity> Users,
        List<Organization> Organizations,
        List<Membership> Memberships,
        List<RefreshToken> RefreshTokens,
        List<InviteToken> InviteTokens);
}
