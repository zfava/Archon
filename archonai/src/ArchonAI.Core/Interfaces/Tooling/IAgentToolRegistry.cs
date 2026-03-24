namespace ArchonAI.Core.Interfaces.Tooling;

public interface IAgentToolRegistry
{
    void RegisterTool(IAgentTool tool);

    IReadOnlyList<string> GetAvailableTools();
}
