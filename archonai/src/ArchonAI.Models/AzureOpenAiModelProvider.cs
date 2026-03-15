using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Options;

namespace ArchonAI.Models;

public sealed class AzureOpenAiModelProvider : IModelProvider
{
    private readonly ModelProviderOptions _options;

    public AzureOpenAiModelProvider(IOptions<ModelProviderOptions> options)
    {
        _options = options.Value;
    }

    public string ProviderName => "azure-openai";

    public bool CanHandle(string model) => model.StartsWith("azure", StringComparison.OrdinalIgnoreCase);

    public global::System.Threading.Tasks.Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_options.AzureOpenAI.ApiKey) || string.IsNullOrWhiteSpace(_options.AzureOpenAI.Endpoint))
        {
            return global::System.Threading.Tasks.Task.FromResult(new ModelResponse(
                Provider: ProviderName,
                Model: request.Model,
                IsSuccess: false,
                Content: string.Empty,
                Warnings: Array.Empty<string>(),
                Errors: new[] { "Azure OpenAI endpoint/API key are not configured." },
                CompletedAtUtc: DateTimeOffset.UtcNow));
        }

        return global::System.Threading.Tasks.Task.FromResult(new ModelResponse(
            Provider: ProviderName,
            Model: request.Model,
            IsSuccess: true,
            Content: $"[azure-openai:{request.Model}] {request.Prompt}",
            Warnings: Array.Empty<string>(),
            Errors: Array.Empty<string>(),
            CompletedAtUtc: DateTimeOffset.UtcNow));
    }
}
