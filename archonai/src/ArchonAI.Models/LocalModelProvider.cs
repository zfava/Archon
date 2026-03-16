using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Options;

namespace ArchonAI.Models;

public sealed class LocalModelProvider : IModelProvider
{
    private readonly ModelProviderOptions _options;

    public LocalModelProvider(IOptions<ModelProviderOptions> options)
    {
        _options = options.Value;
    }

    public string ProviderName => "local";

    public bool CanHandle(string model)
    {
        return model.StartsWith("local", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("ollama", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("gguf", StringComparison.OrdinalIgnoreCase);
    }

    public global::System.Threading.Tasks.Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string modelName = string.IsNullOrWhiteSpace(request.Model)
            ? _options.Local.DefaultModel
            : request.Model;

        return global::System.Threading.Tasks.Task.FromResult(new ModelResponse(
            Provider: ProviderName,
            Model: modelName,
            IsSuccess: true,
            Content: $"[local:{modelName}] {request.Prompt}",
            Warnings: Array.Empty<string>(),
            Errors: Array.Empty<string>(),
            CompletedAtUtc: DateTimeOffset.UtcNow));
    }
}
