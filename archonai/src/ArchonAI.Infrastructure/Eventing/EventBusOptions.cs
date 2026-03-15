namespace ArchonAI.Infrastructure.Eventing;

public sealed class EventBusOptions
{
    public bool UseNats { get; set; }
    public string Url { get; set; } = "nats://localhost:4222";
    public int HandlerRetryCount { get; set; } = 3;
    public int BaseRetryDelayMs { get; set; } = 200;
    public string DeadLetterSuffix { get; set; } = ".dlq";
}
