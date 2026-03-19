using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Infrastructure.Secrets;

/// <summary>
/// Reads secrets from files mounted by Vault Agent sidecar or Kubernetes
/// projected volumes. Supports runtime rotation via file-watch.
/// Secret files are expected at {basePath}/{key} with the value as content.
/// </summary>
public sealed class FileSecretProvider : ISecretProvider, ISecretRotationNotifier, IDisposable
{
    private readonly string _basePath;
    private readonly ILogger<FileSecretProvider> _logger;
    private readonly FileSystemWatcher? _watcher;
    private readonly List<Action<string>> _callbacks = new();
    private readonly object _lock = new();

    public FileSecretProvider(string basePath, ILogger<FileSecretProvider> logger)
    {
        _basePath = basePath;
        _logger = logger;

        if (Directory.Exists(basePath))
        {
            _watcher = new FileSystemWatcher(basePath)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _logger.LogInformation("File secret watcher started: path={BasePath}", basePath);
        }
        else
        {
            _logger.LogWarning("Secret base path does not exist: path={BasePath}. File-based secrets unavailable.", basePath);
        }
    }

    public bool SupportsRotation => true;

    public string? GetSecret(string key)
    {
        var filePath = Path.Combine(_basePath, key);
        _logger.LogDebug("Secret access: key={SecretKey}, provider=file, path={FilePath}", key, filePath);

        if (!File.Exists(filePath))
            return null;

        return File.ReadAllText(filePath).Trim();
    }

    public string GetRequiredSecret(string key)
    {
        var value = GetSecret(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            _logger.LogError("Required secret not found: key={SecretKey}, provider=file, basePath={BasePath}", key, _basePath);
            throw new InvalidOperationException(
                $"Required secret '{key}' not found at '{Path.Combine(_basePath, key)}'. Ensure Vault Agent or projected volume is configured.");
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

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var secretKey = Path.GetFileName(e.FullPath);
        _logger.LogInformation("Secret file changed: key={SecretKey}, changeType={ChangeType}", secretKey, e.ChangeType);

        List<Action<string>> callbacksCopy;
        lock (_lock)
        {
            callbacksCopy = new List<Action<string>>(_callbacks);
        }

        foreach (var callback in callbacksCopy)
        {
            try
            {
                callback(secretKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Secret rotation callback failed: key={SecretKey}", secretKey);
            }
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }

    private sealed class CallbackDisposable : IDisposable
    {
        private readonly Action _onDispose;
        public CallbackDisposable(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}
