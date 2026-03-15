using ArchonAI.Core.Models.Models;

namespace ArchonAI.Core.Interfaces;

public interface IModelProvider
{
    string ProviderName { get; }

    bool CanHandle(string model);

    global::System.Threading.Tasks.Task<ModelResponse> GenerateAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default);
}
