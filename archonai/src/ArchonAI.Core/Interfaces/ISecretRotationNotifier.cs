namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Notifies subscribers when secrets have been rotated.
/// Used by components that need to refresh keys, connection pools, etc.
/// </summary>
public interface ISecretRotationNotifier
{
    /// <summary>
    /// Registers a callback to be invoked when a secret changes.
    /// The callback receives the secret key that changed (never the value).
    /// Returns a disposable that unregisters the callback.
    /// </summary>
    IDisposable OnSecretChanged(Action<string> callback);
}
