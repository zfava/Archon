using System.Text;
using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace ArchonAI.Infrastructure.Secrets;

/// <summary>
/// Manages JWT signing keys with dual-key validation during rotation.
/// When a key rotation is detected, the previous key remains valid for
/// token validation (but not signing) until the rollover window expires.
/// </summary>
public sealed class RotatingJwtSecurityKeyProvider : IDisposable
{
    private readonly ISecretProvider _secretProvider;
    private readonly ILogger<RotatingJwtSecurityKeyProvider> _logger;
    private readonly string _secretKey;
    private readonly TimeSpan _rolloverWindow;
    private readonly object _lock = new();
    private IDisposable? _rotationSubscription;

    private SymmetricSecurityKey _currentKey;
    private SymmetricSecurityKey? _previousKey;
    private DateTimeOffset _previousKeyExpiry;

    public RotatingJwtSecurityKeyProvider(
        ISecretProvider secretProvider,
        ILogger<RotatingJwtSecurityKeyProvider> logger,
        string secretKey = "ARCHONAI_JWT_SIGNING_KEY",
        TimeSpan? rolloverWindow = null)
    {
        _secretProvider = secretProvider;
        _logger = logger;
        _secretKey = secretKey;
        _rolloverWindow = rolloverWindow ?? TimeSpan.FromHours(1);

        var keyValue = secretProvider.GetRequiredSecret(secretKey);
        _currentKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyValue));

        if (secretProvider is ISecretRotationNotifier notifier)
        {
            _rotationSubscription = notifier.OnSecretChanged(OnSecretRotated);
        }
    }

    /// <summary>The current key used for signing new tokens.</summary>
    public SymmetricSecurityKey SigningKey
    {
        get { lock (_lock) return _currentKey; }
    }

    /// <summary>All keys valid for token validation (current + previous during rollover).</summary>
    public IEnumerable<SecurityKey> ValidationKeys
    {
        get
        {
            lock (_lock)
            {
                yield return _currentKey;
                if (_previousKey is not null && DateTimeOffset.UtcNow < _previousKeyExpiry)
                    yield return _previousKey;
            }
        }
    }

    private void OnSecretRotated(string changedKey)
    {
        if (!string.Equals(changedKey, _secretKey, StringComparison.OrdinalIgnoreCase))
            return;

        var newValue = _secretProvider.GetSecret(_secretKey);
        if (string.IsNullOrWhiteSpace(newValue) || newValue.Length < 32)
        {
            _logger.LogError("JWT key rotation rejected: new key is missing or too short");
            return;
        }

        lock (_lock)
        {
            _previousKey = _currentKey;
            _previousKeyExpiry = DateTimeOffset.UtcNow + _rolloverWindow;
            _currentKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(newValue));
        }

        _logger.LogInformation(
            "JWT signing key rotated. Previous key valid until {PreviousKeyExpiry} for validation only.",
            _previousKeyExpiry);
    }

    public void Dispose()
    {
        _rotationSubscription?.Dispose();
    }
}
