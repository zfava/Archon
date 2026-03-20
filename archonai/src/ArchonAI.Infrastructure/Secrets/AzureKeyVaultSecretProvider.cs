using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Infrastructure.Secrets;

/// <summary>
/// Reads secrets from Azure Key Vault using DefaultAzureCredential (Managed Identity).
/// Implements graceful degradation: returns null on transient failures.
/// </summary>
public sealed class AzureKeyVaultSecretProvider : ISecretProvider, ISecretRotationNotifier, IDisposable
{
    private readonly ILogger<AzureKeyVaultSecretProvider> _logger;
    private readonly string _vaultUri;
    private readonly int _pollingIntervalSeconds;
    private readonly List<Action<string>> _callbacks = new();
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, string> _versionCache = new();
    private Task? _pollingTask;

    // Azure SDK types loaded dynamically to avoid hard dependency when not configured
    private readonly dynamic? _client;

    public AzureKeyVaultSecretProvider(
        IConfiguration configuration,
        ILogger<AzureKeyVaultSecretProvider> logger)
    {
        _logger = logger;
        _vaultUri = configuration["Azure:KeyVault:VaultUri"]
            ?? throw new InvalidOperationException("Azure:KeyVault:VaultUri is required for AzureKeyVaultSecretProvider.");
        _pollingIntervalSeconds = int.TryParse(configuration["Azure:KeyVault:PollingIntervalSeconds"], out var pi) ? pi : 300;

        try
        {
            // Create SecretClient using DefaultAzureCredential
            var credentialType = Type.GetType("Azure.Identity.DefaultAzureCredential, Azure.Identity");
            var clientType = Type.GetType("Azure.Security.KeyVault.Secrets.SecretClient, Azure.Security.KeyVault.Secrets");

            if (credentialType is not null && clientType is not null)
            {
                var credential = Activator.CreateInstance(credentialType);
                _client = Activator.CreateInstance(clientType, new Uri(_vaultUri), credential);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure Key Vault SDK not available — provider will return null for all lookups");
        }

        _logger.LogInformation(
            "Azure Key Vault provider initialized: vaultUri={VaultUri}, sdkAvailable={SdkAvailable}",
            _vaultUri, _client is not null);

        _pollingTask = Task.Run(() => RotationPollingLoopAsync(_cts.Token));
    }

    public bool SupportsRotation => true;

    public string? GetSecret(string key)
    {
        _logger.LogDebug("Secret access: key={SecretKey}, provider=azure-keyvault", key);
        try
        {
            return GetSecretAsync(key).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (IsTransientFailure(ex))
        {
            _logger.LogWarning(ex, "Azure Key Vault transient failure for key={SecretKey} — returning null for fallback", key);
            return null;
        }
    }

    public string GetRequiredSecret(string key)
    {
        var value = GetSecret(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required secret '{key}' not found in Azure Key Vault at {_vaultUri}.");
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
        if (_client is null) return null;

        try
        {
            // Azure Key Vault uses hyphens not underscores in secret names
            var secretName = key.Replace('_', '-');
            dynamic response = await _client.GetSecretAsync(secretName);
            dynamic secret = response.Value;
            string? value = secret.Value;
            string? versionId = secret.Properties?.Version;

            if (versionId is not null)
            {
                lock (_lock)
                {
                    _versionCache[key] = versionId;
                }
            }

            return value;
        }
        catch (Exception ex) when (IsTransientFailure(ex))
        {
            _logger.LogWarning(ex, "Azure Key Vault transient error for key={SecretKey}", key);
            return null;
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("SecretNotFound", StringComparison.OrdinalIgnoreCase)
            || ex.GetType().Name.Contains("ResourceNotFound", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Azure Key Vault secret not found: key={SecretKey}", key);
            return null;
        }
    }

    private async Task RotationPollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_pollingIntervalSeconds), ct);

                if (_client is null) continue;

                Dictionary<string, string> snapshot;
                lock (_lock)
                {
                    snapshot = new Dictionary<string, string>(_versionCache);
                }

                foreach (var (key, lastVersionId) in snapshot)
                {
                    try
                    {
                        var secretName = key.Replace('_', '-');
                        dynamic response = await _client.GetSecretAsync(secretName, cancellationToken: ct);
                        dynamic secret = response.Value;
                        string? currentVersionId = secret.Properties?.Version;

                        if (currentVersionId is not null && currentVersionId != lastVersionId)
                        {
                            lock (_lock)
                            {
                                _versionCache[key] = currentVersionId;
                            }
                            NotifyCallbacks(key);
                        }
                    }
                    catch (Exception ex) when (IsTransientFailure(ex))
                    {
                        _logger.LogDebug(ex, "Azure rotation poll transient error for key={SecretKey}", key);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Azure rotation polling loop error");
            }
        }
    }

    private void NotifyCallbacks(string key)
    {
        _logger.LogInformation("Azure Key Vault secret rotated: key={SecretKey}", key);

        List<Action<string>> callbacksCopy;
        lock (_lock)
        {
            callbacksCopy = new List<Action<string>>(_callbacks);
        }

        foreach (var callback in callbacksCopy)
        {
            try { callback(key); }
            catch (Exception ex) { _logger.LogError(ex, "Azure rotation callback failed: key={SecretKey}", key); }
        }
    }

    private static bool IsTransientFailure(Exception ex)
        => ex is HttpRequestException or TaskCanceledException or TimeoutException
            || ex.GetType().Name.Contains("RequestFailed", StringComparison.OrdinalIgnoreCase)
            || ex.GetType().Name.Contains("Throttled", StringComparison.OrdinalIgnoreCase);

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
