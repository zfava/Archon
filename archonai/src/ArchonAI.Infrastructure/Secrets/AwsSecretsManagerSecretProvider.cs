using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Infrastructure.Secrets;

/// <summary>
/// Reads secrets from AWS Secrets Manager using the SDK credential chain.
/// No hardcoded credentials — relies on IAM roles, env vars, or instance profiles.
/// Implements graceful degradation: returns null on transient failures.
/// </summary>
public sealed class AwsSecretsManagerSecretProvider : ISecretProvider, ISecretRotationNotifier, IDisposable
{
    private readonly ILogger<AwsSecretsManagerSecretProvider> _logger;
    private readonly string _region;
    private readonly string _secretNamePrefix;
    private readonly int _pollingIntervalSeconds;
    private readonly List<Action<string>> _callbacks = new();
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, string> _versionCache = new();
    private Task? _pollingTask;

    // AWS SDK types are loaded dynamically to avoid hard dependency when not configured
    private readonly dynamic? _client;

    public AwsSecretsManagerSecretProvider(
        IConfiguration configuration,
        ILogger<AwsSecretsManagerSecretProvider> logger)
    {
        _logger = logger;
        _region = configuration["Aws:SecretsManager:Region"]
            ?? throw new InvalidOperationException("Aws:SecretsManager:Region is required for AwsSecretsManagerSecretProvider.");
        _secretNamePrefix = configuration["Aws:SecretsManager:SecretNamePrefix"] ?? "";
        _pollingIntervalSeconds = int.TryParse(configuration["Aws:SecretsManager:PollingIntervalSeconds"], out var pi) ? pi : 300;

        try
        {
            // Create AmazonSecretsManagerClient using the default credential chain
            var regionType = Type.GetType("Amazon.RegionEndpoint, AWSSDK.Core");
            var clientType = Type.GetType("Amazon.SecretsManager.AmazonSecretsManagerClient, AWSSDK.SecretsManager");

            if (regionType is not null && clientType is not null)
            {
                var region = regionType.GetField(_region.Replace("-", "").ToUpperInvariant())?.GetValue(null)
                    ?? regionType.GetMethod("GetBySystemName")?.Invoke(null, new object[] { _region });
                _client = Activator.CreateInstance(clientType, region);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AWS Secrets Manager SDK not available — provider will return null for all lookups");
        }

        _logger.LogInformation(
            "AWS Secrets Manager provider initialized: region={Region}, prefix={Prefix}, sdkAvailable={SdkAvailable}",
            _region, _secretNamePrefix, _client is not null);

        _pollingTask = Task.Run(() => RotationPollingLoopAsync(_cts.Token));
    }

    public bool SupportsRotation => true;

    // Sync wrapper implements ISecretProvider.GetSecret contract.
    // Vault calls are infrequent (startup, key rotation), cached by provider,
    // and not on the hot request path. ChainedSecretProvider falls through
    // on transient failure. Async-only would require cascading changes to
    // 10+ consumers including security-critical paths (JWT signing, TOTP encryption).
    public string? GetSecret(string key)
    {
        _logger.LogDebug("Secret access: key={SecretKey}, provider=aws-secrets-manager", key);
        try
        {
            return GetSecretAsync(key).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (IsTransientFailure(ex))
        {
            _logger.LogWarning(ex, "AWS Secrets Manager transient failure for key={SecretKey} — returning null for fallback", key);
            return null;
        }
    }

    public string GetRequiredSecret(string key)
    {
        var value = GetSecret(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required secret '{key}' not found in AWS Secrets Manager (region={_region}).");
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
            var secretName = string.IsNullOrEmpty(_secretNamePrefix) ? key : $"{_secretNamePrefix}/{key}";

            // Use reflection to call GetSecretValueAsync
            var requestType = Type.GetType("Amazon.SecretsManager.Model.GetSecretValueRequest, AWSSDK.SecretsManager");
            if (requestType is null) return null;

            dynamic request = Activator.CreateInstance(requestType)!;
            request.SecretId = secretName;

            dynamic response = await _client.GetSecretValueAsync(request);
            string? secretString = response.SecretString;
            string? versionId = response.VersionId;

            if (versionId is not null)
            {
                lock (_lock)
                {
                    _versionCache[key] = versionId;
                }
            }

            return secretString;
        }
        catch (Exception ex) when (IsTransientFailure(ex))
        {
            _logger.LogWarning(ex, "AWS Secrets Manager transient error for key={SecretKey}", key);
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
                        var secretName = string.IsNullOrEmpty(_secretNamePrefix) ? key : $"{_secretNamePrefix}/{key}";
                        var requestType = Type.GetType("Amazon.SecretsManager.Model.GetSecretValueRequest, AWSSDK.SecretsManager");
                        if (requestType is null) continue;

                        dynamic request = Activator.CreateInstance(requestType)!;
                        request.SecretId = secretName;

                        dynamic response = await _client.GetSecretValueAsync(request);
                        string? currentVersionId = response.VersionId;

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
                        _logger.LogDebug(ex, "AWS rotation poll transient error for key={SecretKey}", key);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AWS rotation polling loop error");
            }
        }
    }

    private void NotifyCallbacks(string key)
    {
        _logger.LogInformation("AWS secret rotated: key={SecretKey}", key);

        List<Action<string>> callbacksCopy;
        lock (_lock)
        {
            callbacksCopy = new List<Action<string>>(_callbacks);
        }

        foreach (var callback in callbacksCopy)
        {
            try { callback(key); }
            catch (Exception ex) { _logger.LogError(ex, "AWS rotation callback failed: key={SecretKey}", key); }
        }
    }

    private static bool IsTransientFailure(Exception ex)
        => ex is HttpRequestException or TaskCanceledException or TimeoutException
            || ex.GetType().Name.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase)
            || ex.GetType().Name.Contains("Throttling", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        (_client as IDisposable)?.Dispose();
    }

    private sealed class CallbackDisposable(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
