using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Models.Routing;

namespace ArchonAI.Core.Interfaces;

public interface IModelRouter
{
    ModelRouteDecision Route(ModelRequest request);
}
