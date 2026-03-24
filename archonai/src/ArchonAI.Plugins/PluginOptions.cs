namespace ArchonAI.Plugins;

public sealed class PluginOptions
{
    public const string SectionName = "Plugins";

    public bool AutoDiscoverInDirectories { get; set; } = true;

    public List<string> PluginDirectories { get; set; } = new();

    public List<string> AgentAssemblies { get; set; } = new();

    public List<string> ToolAssemblies { get; set; } = new();

    public List<string> ConnectorAssemblies { get; set; } = new();

    public List<string> EvaluationAssemblies { get; set; } = new();
}
