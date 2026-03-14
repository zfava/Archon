using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Options;

namespace ArchonAI.Models;

public sealed class AnthropicModelProvider : IModelProvider
{
    private readonly ModelProviderOptions _options;

    public AnthropicModelProvider(IOptions<ModelProviderOptions> options)
    {
        _options = options.Value;
    }

    public string ProviderName => "anthropic";

    public bool CanHandle(string model) => model.StartsWith("anthropic", StringComparison.OrdinalIgnoreCase);

    public global::System.Threading.Tasks.Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_options.Anthropic.ApiKey))
        {
            return global::System.Threading.Tasks.Task.FromResult(new ModelResponse(
                Provider: ProviderName,
                Model: request.Model,
                IsSuccess: false,
                Content: string.Empty,
                Warnings: Array.Empty<string>(),
                Errors: new[] { "Anthropic API key is not configured." },
                CompletedAtUtc: DateTimeOffset.UtcNow));
        }

        return global::System.Threading.Tasks.Task.FromResult(new ModelResponse(
            Provider: ProviderName,
            Model: request.Model,
            IsSuccess: true,
            Content: $"[anthropic:{request.Model}] {request.Prompt}",
            Warnings: Array.Empty<string>(),
            Errors: Array.Empty<string>(),
            CompletedAtUtc: DateTimeOffset.UtcNow));
    }
}
