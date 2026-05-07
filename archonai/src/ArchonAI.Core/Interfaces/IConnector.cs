using ArchonAI.Core.Models;
namespace ArchonAI.Core.Interfaces;
public interface IConnector { string SystemName { get; } global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default); }
