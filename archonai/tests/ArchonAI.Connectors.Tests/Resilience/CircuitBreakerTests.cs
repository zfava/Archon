using System.Net;
using ArchonAI.Common.Observability;
using ArchonAI.Connectors.Framework;
using ArchonAI.Connectors.Implementations;
using ArchonAI.Connectors.Tests.Shared;
using ArchonAI.Core.Interfaces;
using ArchonAI.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ArchonAI.Connectors.Tests.Resilience;

public class CircuitBreakerTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();

    private ConnectorResilienceRegistry CreateRegistry(int threshold = 3, int durationSeconds = 1)
    {
        var options = new ResilienceOptions
        {
            Connectors = new ConnectorResilienceOptions
            {
                CircuitBreakerThreshold = threshold,
                CircuitBreakerDurationSeconds = durationSeconds,
                MaxConcurrentCalls = 10,
                MaxQueueDepth = 20,
                TimeoutSeconds = 30
            }
        };

        var statePublisher = new CircuitBreakerStatePublisher(
            NullLogger<CircuitBreakerStatePublisher>.Instance);

        var factory = new ResiliencePipelineFactory(
            options, statePublisher, NullLogger<ResiliencePipelineFactory>.Instance);

        return new ConnectorResilienceRegistry(factory);
    }

    [Fact]
    public async Task CircuitBreaker_Opens_After_Consecutive_Failures()
    {
        // Arrange: set up a handler that always returns 500
        var handler = new MockHttpMessageHandler();
        var responses = Enumerable.Range(0, 20)
            .Select(_ => (HttpStatusCode.InternalServerError, "{\"error\":\"server error\"}"))
            .ToList();
        handler.SetResponseSequence(responses);

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var registry = CreateRegistry(threshold: 3, durationSeconds: 2);
        var connector = new CrmConnector(httpClient, _eventBus,
            NullLogger<CrmConnector>.Instance, registry);

        // Act: send enough requests to trip the circuit
        var exceptions = new List<Exception>();
        for (int i = 0; i < 10; i++)
        {
            try
            {
                await connector.UpsertCustomerAsync($"cust-{i}", "{}", CancellationToken.None);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        // Assert: after threshold failures, we should see ConnectorCircuitOpenException
        Assert.Contains(exceptions, e => e is ConnectorCircuitOpenException);
    }

    [Fact]
    public void CircuitBreakerStatePublisher_Tracks_State_Transitions()
    {
        var publisher = new CircuitBreakerStatePublisher(
            NullLogger<CircuitBreakerStatePublisher>.Instance);

        // Initially closed
        Assert.Equal(Polly.CircuitBreaker.CircuitState.Closed, publisher.GetState("test-connector"));

        // Simulate break
        publisher.OnBreak("test-connector", TimeSpan.FromSeconds(30), new Exception("test"));
        Assert.Equal(Polly.CircuitBreaker.CircuitState.Open, publisher.GetState("test-connector"));

        // Simulate half-open
        publisher.OnHalfOpen("test-connector");
        Assert.Equal(Polly.CircuitBreaker.CircuitState.HalfOpen, publisher.GetState("test-connector"));

        // Simulate reset
        publisher.OnReset("test-connector");
        Assert.Equal(Polly.CircuitBreaker.CircuitState.Closed, publisher.GetState("test-connector"));
    }

    [Fact]
    public void CircuitBreakerStatePublisher_Tracks_Multiple_Integrations()
    {
        var publisher = new CircuitBreakerStatePublisher(
            NullLogger<CircuitBreakerStatePublisher>.Instance);

        publisher.OnBreak("salesforce", TimeSpan.FromSeconds(30), new Exception("sf down"));
        publisher.OnBreak("hubspot", TimeSpan.FromSeconds(15), new Exception("hs down"));
        publisher.OnReset("salesforce");

        var states = publisher.GetAllStates();
        Assert.Equal(Polly.CircuitBreaker.CircuitState.Closed, states["salesforce"]);
        Assert.Equal(Polly.CircuitBreaker.CircuitState.Open, states["hubspot"]);
    }
}
