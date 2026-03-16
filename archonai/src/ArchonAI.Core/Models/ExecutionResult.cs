namespace ArchonAI.Core.Models;
public sealed record ExecutionResult(Guid TaskId,bool IsSuccess,string Summary,IReadOnlyDictionary<string,string> Outputs,IReadOnlyList<string> Warnings,IReadOnlyList<string> Errors,DateTimeOffset CompletedAtUtc);
