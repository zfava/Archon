using ArchonAI.Core.Models.Context;

namespace ArchonAI.Core.Interfaces;

public interface IContextEngine
{
    global::System.Threading.Tasks.Task<SystemContext> GetContextAsync(CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task UpdateContextAsync(SystemContext context, CancellationToken cancellationToken = default);
}
