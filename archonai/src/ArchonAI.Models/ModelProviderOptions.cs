namespace ArchonAI.Models;

public sealed class ModelProviderOptions
{
    public string DefaultModel { get; set; } = "openai.gpt-4.1-mini";

    public OpenAiOptions OpenAI { get; set; } = new();

    public AzureOpenAiOptions AzureOpenAI { get; set; } = new();

    public AnthropicOptions Anthropic { get; set; } = new();

    public LocalModelOptions Local { get; set; } = new();
}

public sealed class OpenAiOptions
{
    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://api.openai.com";
}

public sealed class AzureOpenAiOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Deployment { get; set; } = "gpt-4o-mini";
}

public sealed class AnthropicOptions
{
    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://api.anthropic.com";
}

public sealed class LocalModelOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string DefaultModel { get; set; } = "local.default";
}
