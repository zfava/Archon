using ArchonAI.Core.Models;
namespace ArchonAI.Core.Interfaces;
public interface IEventBus { global::System.Threading.Tasks.Task PublishAsync(SystemEvent systemEvent, CancellationToken cancellationToken = default); global::System.Threading.Tasks.Task SubscribeAsync(string eventType, Func<SystemEvent,CancellationToken,global::System.Threading.Tasks.Task> handler, CancellationToken cancellationToken = default); }
