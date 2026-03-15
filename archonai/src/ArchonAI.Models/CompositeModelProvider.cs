using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Options;

namespace ArchonAI.Models;

public sealed class CompositeModelProvider : IModelProvider
{
    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly ModelProviderOptions _options;

    public CompositeModelProvider(
        IOptions<ModelProviderOptions> options,
        OpenAiModelProvider openAi,
        AzureOpenAiModelProvider azureOpenAi,
        AnthropicModelProvider anthropic,
        LocalModelProvider local)
    {
        _options = options.Value;
        _providers = new IModelProvider[] { openAi, azureOpenAi, anthropic, local };
    }

    public string ProviderName => "composite";

    public bool CanHandle(string model) => _providers.Any(provider => provider.CanHandle(model));

    public async global::System.Threading.Tasks.Task<ModelResponse> GenerateAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string model = string.IsNullOrWhiteSpace(request.Model) ? _options.DefaultModel : request.Model;
        var normalizedRequest = request with { Model = model };

        IModelProvider provider = _providers.FirstOrDefault(p => p.CanHandle(model))
            ?? _providers.First(p => p is LocalModelProvider);

        return await provider.GenerateAsync(normalizedRequest, cancellationToken);
    }
}
