using ArchonAI.Core.Models.DataFabric;

namespace ArchonAI.Core.Interfaces;

public interface IDataFabricEngine
{
    global::System.Threading.Tasks.Task<DataFabricQueryResult> QueryEnterpriseDataAsync(
        string source,
        IReadOnlyDictionary<string, string> filters,
        IReadOnlyDictionary<string, string> schemaMapping,
        IReadOnlyList<string> permissions,
        string consumerType,
        CancellationToken cancellationToken = default);
}
