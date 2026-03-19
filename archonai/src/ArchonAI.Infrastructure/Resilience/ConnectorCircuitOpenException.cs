namespace ArchonAI.Infrastructure.Resilience;

/// <summary>
/// Thrown when a connector's circuit breaker is open.
/// Callers should surface a ConnectorUnavailable status and queue for retry.
/// </summary>
public sealed class ConnectorCircuitOpenException : Exception
{
    public string ConnectorName { get; }

    public ConnectorCircuitOpenException(string connectorName, Exception? innerException = null)
        : base($"Circuit breaker is open for connector '{connectorName}'. Request queued for retry.", innerException)
    {
        ConnectorName = connectorName;
    }
}
