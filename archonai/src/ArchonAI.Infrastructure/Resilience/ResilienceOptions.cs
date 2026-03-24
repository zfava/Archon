namespace ArchonAI.Infrastructure.Resilience;

/// <summary>
/// Configuration for circuit breaker, bulkhead, and timeout policies per integration category.
/// </summary>
public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public ConnectorResilienceOptions Connectors { get; set; } = new();
    public ProviderResilienceOptions Providers { get; set; } = new();
    public EventBusResilienceOptions EventBus { get; set; } = new();
    public DatabaseResilienceOptions Database { get; set; } = new();
}

public sealed class ConnectorResilienceOptions
{
    /// <summary>Number of failures before circuit opens.</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>Duration the circuit stays open before transitioning to half-open.</summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    /// <summary>Maximum concurrent calls allowed (bulkhead).</summary>
    public int MaxConcurrentCalls { get; set; } = 10;

    /// <summary>Maximum queue depth when bulkhead is full.</summary>
    public int MaxQueueDepth { get; set; } = 20;

    /// <summary>Overall timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class ProviderResilienceOptions
{
    public int CircuitBreakerThreshold { get; set; } = 3;
    public int CircuitBreakerDurationSeconds { get; set; } = 15;
    public int MaxConcurrentCalls { get; set; } = 5;
    public int MaxQueueDepth { get; set; } = 20;
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class EventBusResilienceOptions
{
    public int CircuitBreakerThreshold { get; set; } = 10;
    public int CircuitBreakerDurationSeconds { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 5;
}

public sealed class DatabaseResilienceOptions
{
    public int TimeoutSeconds { get; set; } = 10;
}
