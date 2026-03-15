using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;

namespace ArchonAI.Infrastructure.Services;

public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<string, List<Func<SystemEvent, CancellationToken, global::System.Threading.Tasks.Task>>> _subscriptions = new();

    public async global::System.Threading.Tasks.Task PublishAsync(SystemEvent systemEvent, CancellationToken cancellationToken = default)
    {
        if (_subscriptions.TryGetValue(systemEvent.EventType, out var handlers))
        {
            foreach (var handler in handlers)
            {
                await handler(systemEvent, cancellationToken);
            }
        }
    }

    public global::System.Threading.Tasks.Task SubscribeAsync(string eventType, Func<SystemEvent, CancellationToken, global::System.Threading.Tasks.Task> handler, CancellationToken cancellationToken = default)
    {
        var handlers = _subscriptions.GetOrAdd(eventType, _ => []);
        lock (handlers)
        {
            handlers.Add(handler);
        }

        return global::System.Threading.Tasks.Task.CompletedTask;
    }
}
