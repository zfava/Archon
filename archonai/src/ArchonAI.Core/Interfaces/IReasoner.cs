using ArchonAI.Core.Models;
namespace ArchonAI.Core.Interfaces;
public interface IReasoner { global::System.Threading.Tasks.Task<string> EvaluateAsync(Objective objective, IReadOnlyList<ExecutionResult> executionResults, CancellationToken cancellationToken = default); }
