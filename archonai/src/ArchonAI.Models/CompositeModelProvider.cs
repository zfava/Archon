using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Options;

namespace ArchonAI.Models;

public sealed class CompositeModelProvider : IModelProvider
{
    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly ModelProviderOptions _options;
    private readonly IModelRouter _modelRouter;

    public CompositeModelProvider(
        IOptions<ModelProviderOptions> options,
        OpenAiModelProvider openAi,
        AzureOpenAiModelProvider azureOpenAi,
        AnthropicModelProvider anthropic,
        LocalModelProvider local,
        IModelRouter modelRouter)
    {
        _options = options.Value;
        _providers = new IModelProvider[] { openAi, azureOpenAi, anthropic, local };
        _modelRouter = modelRouter;
    }

    public string ProviderName => "composite";

    public bool CanHandle(string model) => _providers.Any(provider => provider.CanHandle(model));

    public async global::System.Threading.Tasks.Task<ModelResponse> GenerateAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var route = _modelRouter.Route(request);
        string model = string.IsNullOrWhiteSpace(route.Model) ? _options.DefaultModel : route.Model;

        var normalizedRequest = request with
        {
            Model = model,
            Parameters = new Dictionary<string, string>(request.Parameters)
            {
                ["routedProvider"] = route.Provider,
                ["routingReason"] = route.Reason,
                ["costOptimized"] = route.CostOptimized.ToString(),
                ["latencyOptimized"] = route.LatencyOptimized.ToString()
            }
        };

        IModelProvider provider = _providers.FirstOrDefault(p => p.ProviderName.Equals(route.Provider, StringComparison.OrdinalIgnoreCase) && p.CanHandle(model))
            ?? _providers.FirstOrDefault(p => p.CanHandle(model))
            ?? _providers.First(p => p is LocalModelProvider);

        return await provider.GenerateAsync(normalizedRequest, cancellationToken);
    }
}
