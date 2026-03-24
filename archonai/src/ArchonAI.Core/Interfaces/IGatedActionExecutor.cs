using ArchonAI.Core.Models.Governance;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Executes the original action after an approval gate is approved.
/// </summary>
public interface IGatedActionExecutor
{
    /// <summary>
    /// Dispatches execution of the approved gate's stored action intent.
    /// Returns a result indicating success or failure with an optional error message.
    /// </summary>
    Task<GatedActionResult> ExecuteAsync(ApprovalGate gate, CancellationToken ct = default);
}

public sealed record GatedActionResult(bool Success, string? Error = null);
