namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Provides structured system prompts for each phase of the intelligence loop.
/// Each prompt is designed to elicit valid, parseable JSON from any major LLM.
/// </summary>
public interface ISystemPromptProvider
{
    /// <summary>Returns the system prompt for the given intelligence loop phase.</summary>
    string GetPrompt(string phase);

    /// <summary>Returns the expected JSON schema for the given phase's output.</summary>
    string GetSchema(string phase);

    /// <summary>Returns all known phase names.</summary>
    IReadOnlyList<string> GetPhases();
}
