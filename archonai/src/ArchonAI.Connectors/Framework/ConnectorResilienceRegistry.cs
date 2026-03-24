using System.Collections.Concurrent;
using ArchonAI.Infrastructure.Resilience;
using Polly.Wrap;

namespace ArchonAI.Connectors.Framework;

/// <summary>
/// Holds named circuit breaker + bulkhead + timeout pipelines for each connector.
/// Connectors call GetOrCreatePipeline() to get their specific resilience pipeline.
/// </summary>
public sealed class ConnectorResilienceRegistry
{
    private readonly ResiliencePipelineFactory _factory;
    private readonly ConcurrentDictionary<string, AsyncPolicyWrap<System.Net.Http.HttpResponseMessage>> _pipelines = new();

    public ConnectorResilienceRegistry(ResiliencePipelineFactory factory)
    {
        _factory = factory;
    }

    public AsyncPolicyWrap<System.Net.Http.HttpResponseMessage> GetOrCreatePipeline(string connectorName)
    {
        return _pipelines.GetOrAdd(connectorName, name => _factory.CreateConnectorHttpPipeline(name));
    }
}
