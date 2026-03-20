using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Infrastructure.Secrets;

/// <summary>
/// Reads secrets from HashiCorp Vault KV v2 secrets engine via HTTP API.
/// Uses AppRole authentication with automatic token renewal.
/// Implements graceful degradation: returns null on transient failures.
/// </summary>
public sealed class HashiCorpVaultSecretProvider : ISecretProvider, ISecretRotationNotifier, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HashiCorpVaultSecretProvider> _logger;
    private readonly string _endpoint;
    private readonly string _mountPath;
    private readonly string _roleId;
    private readonly string _secretId;
    private readonly int _renewIntervalSeconds;
    private readonly List<Action<string>> _callbacks = new();
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, int> _secretVersions = new();
    private string? _clientToken;
    private DateTimeOffset _tokenExpiresAtUtc;
    private Task? _renewalTask;
    private Task? _pollingTask;

    public HashiCorpVaultSecretProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HashiCorpVaultSecretProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _endpoint = configuration["Vault:Endpoint"]
            ?? throw new InvalidOperationException("Vault:Endpoint is required for HashiCorpVaultSecretProvider.");
        _mountPath = configuration["Vault:MountPath"] ?? "secret";
        _roleId = configuration["Vault:AppRoleRoleId"]
            ?? throw new InvalidOperationException("Vault:AppRoleRoleId is required for HashiCorpVaultSecretProvider.");
        _secretId = configuration["Vault:AppRoleSecretId"]
            ?? throw new InvalidOperationException("Vault:AppRoleSecretId is required for HashiCorpVaultSecretProvider.");
        _renewIntervalSeconds = int.TryParse(configuration["Vault:RenewIntervalSeconds"], out var ri) ? ri : 60;

        _logger.LogInformation(
            "HashiCorp Vault provider initialized: endpoint={Endpoint}, mount={MountPath}",
            _endpoint, _mountPath);

        _renewalTask = Task.Run(() => TokenRenewalLoopAsync(_cts.Token));
        _pollingTask = Task.Run(() => RotationPollingLoopAsync(_cts.Token));
    }

    public bool SupportsRotation => true;

    public string? GetSecret(string key)
    {
        _logger.LogDebug("Secret access: key={SecretKey}, provider=vault", key);
        try
        {
            return GetSecretAsync(key).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (IsTransientFailure(ex))
        {
            _logger.LogWarning(ex, "Vault transient failure for key={SecretKey} — returning null for fallback", key);
            return null;
        }
    }

    public string GetRequiredSecret(string key)
    {
        var value = GetSecret(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required secret '{key}' not found in HashiCorp Vault at {_endpoint}/{_mountPath}.");
        }
        return value;
    }

    public IDisposable OnSecretChanged(Action<string> callback)
    {
        lock (_lock)
        {
            _callbacks.Add(callback);
        }
        return new CallbackDisposable(() =>
        {
            lock (_lock)
            {
                _callbacks.Remove(callback);
            }
        });
    }

    private async Task<string?> GetSecretAsync(string key)
    {
        await EnsureAuthenticatedAsync();
        if (_clientToken is null) return null;

        var client = _httpClientFactory.CreateClient("vault");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_endpoint}/v1/{_mountPath}/data/{key}");
        request.Headers.Add("X-Vault-Token", _clientToken);

        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Vault GET failed: key={SecretKey}, status={StatusCode}", key, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<JsonNode>();
        var version = json?["data"]?["metadata"]?["version"]?.GetValue<int>();
        var value = json?["data"]?["data"]?[key]?.GetValue<string>();

        if (version.HasValue)
        {
            lock (_lock)
            {
                _secretVersions[key] = version.Value;
            }
        }

        return value;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_clientToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAtUtc)
            return;

        try
        {
            var client = _httpClientFactory.CreateClient("vault");
            var loginPayload = new { role_id = _roleId, secret_id = _secretId };
            using var response = await client.PostAsJsonAsync(
                $"{_endpoint}/v1/auth/approle/login", loginPayload);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Vault AppRole login failed: status={StatusCode}", response.StatusCode);
                _clientToken = null;
                return;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonNode>();
            _clientToken = json?["auth"]?["client_token"]?.GetValue<string>();
            var leaseDuration = json?["auth"]?["lease_duration"]?.GetValue<int>() ?? 3600;
            _tokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(leaseDuration);

            _logger.LogInformation("Vault AppRole login succeeded. Token TTL={LeaseDuration}s", leaseDuration);
        }
        catch (Exception ex) when (IsTransientFailure(ex))
        {
            _logger.LogWarning(ex, "Vault AppRole login transient failure");
            _clientToken = null;
        }
    }

    private async Task TokenRenewalLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_clientToken is not null)
                {
                    var timeToExpiry = _tokenExpiresAtUtc - DateTimeOffset.UtcNow;
                    var renewAt = timeToExpiry * 0.75;
                    if (renewAt > TimeSpan.Zero)
                    {
                        await Task.Delay(renewAt, ct);
                    }

                    var client = _httpClientFactory.CreateClient("vault");
                    using var request = new HttpRequestMessage(HttpMethod.Post,
                        $"{_endpoint}/v1/auth/token/renew-self");
                    request.Headers.Add("X-Vault-Token", _clientToken);
                    request.Content = JsonContent.Create(new { });

                    using var response = await client.SendAsync(request, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
                        var leaseDuration = json?["auth"]?["lease_duration"]?.GetValue<int>() ?? 3600;
                        _tokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(leaseDuration);
                        _logger.LogDebug("Vault token renewed. New TTL={LeaseDuration}s", leaseDuration);
                    }
                    else
                    {
                        _logger.LogWarning("Vault token renewal failed: {StatusCode}. Re-authenticating.", response.StatusCode);
                        _clientToken = null;
                        await EnsureAuthenticatedAsync();
                    }
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vault token renewal loop error");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }
    }

    private async Task RotationPollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_renewIntervalSeconds), ct);

                Dictionary<string, int> snapshot;
                lock (_lock)
                {
                    snapshot = new Dictionary<string, int>(_secretVersions);
                }

                foreach (var (key, lastVersion) in snapshot)
                {
                    await EnsureAuthenticatedAsync();
                    if (_clientToken is null) break;

                    var client = _httpClientFactory.CreateClient("vault");
                    using var request = new HttpRequestMessage(HttpMethod.Get,
                        $"{_endpoint}/v1/{_mountPath}/data/{key}");
                    request.Headers.Add("X-Vault-Token", _clientToken);

                    using var response = await client.SendAsync(request, ct);
                    if (!response.IsSuccessStatusCode) continue;

                    var json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
                    var currentVersion = json?["data"]?["metadata"]?["version"]?.GetValue<int>();

                    if (currentVersion.HasValue && currentVersion.Value != lastVersion)
                    {
                        lock (_lock)
                        {
                            _secretVersions[key] = currentVersion.Value;
                        }
                        NotifyCallbacks(key);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vault rotation polling error");
            }
        }
    }

    private void NotifyCallbacks(string key)
    {
        _logger.LogInformation("Vault secret rotated: key={SecretKey}", key);

        List<Action<string>> callbacksCopy;
        lock (_lock)
        {
            callbacksCopy = new List<Action<string>>(_callbacks);
        }

        foreach (var callback in callbacksCopy)
        {
            try { callback(key); }
            catch (Exception ex) { _logger.LogError(ex, "Vault rotation callback failed: key={SecretKey}", key); }
        }
    }

    private static bool IsTransientFailure(Exception ex)
        => ex is HttpRequestException or TaskCanceledException or TimeoutException;

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private sealed class CallbackDisposable(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
