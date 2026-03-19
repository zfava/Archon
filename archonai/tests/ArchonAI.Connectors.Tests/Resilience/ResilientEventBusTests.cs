using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Infrastructure.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Xunit;

namespace ArchonAI.Connectors.Tests.Resilience;

public class ResilientEventBusTests
{
    private sealed class FailingEventBus : IEventBus
    {
        public int PublishCallCount { get; private set; }

        public Task PublishAsync(SystemEvent systemEvent, CancellationToken cancellationToken = default)
        {
            PublishCallCount++;
            throw new Exception("NATS connection lost");
        }

        public Task SubscribeAsync(string eventType, Func<SystemEvent, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ResilientEventBus_Swallows_CircuitOpen_On_Publish()
    {
        // Arrange: create a pipeline with very low threshold
        var options = new ResilienceOptions
        {
            EventBus = new EventBusResilienceOptions
            {
                CircuitBreakerThreshold = 2,
                CircuitBreakerDurationSeconds = 30,
                TimeoutSeconds = 5
            }
        };

        var statePublisher = new CircuitBreakerStatePublisher(
            NullLogger<CircuitBreakerStatePublisher>.Instance);
        var factory = new ResiliencePipelineFactory(
            options, statePublisher, NullLogger<ResiliencePipelineFactory>.Instance);
        var pipeline = factory.CreateEventBusPipeline();

        var failingBus = new FailingEventBus();
        var resilientBus = new ResilientEventBus(failingBus, pipeline,
            NullLogger<ResilientEventBus>.Instance);

        var evt = new SystemEvent(Guid.NewGuid(), "test.event", "test",
            Guid.NewGuid(), new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        // Act: publish multiple times — circuit should open after threshold
        for (int i = 0; i < 10; i++)
        {
            // Should NOT throw — resilient bus swallows BrokenCircuitException
            await resilientBus.PublishAsync(evt);
        }

        // Assert: only some publishes reached the inner bus (rest were short-circuited)
        Assert.True(failingBus.PublishCallCount >= 2, "Inner bus should have been called at least threshold times");
        Assert.True(failingBus.PublishCallCount < 10, "Circuit should have opened and stopped calling inner bus");
    }

    [Fact]
    public async Task ResilientEventBus_Subscribe_Not_Wrapped()
    {
        // Subscribe should pass through directly to inner bus
        var innerBus = new FailingEventBus();

        var options = new ResilienceOptions();
        var statePublisher = new CircuitBreakerStatePublisher(
            NullLogger<CircuitBreakerStatePublisher>.Instance);
        var factory = new ResiliencePipelineFactory(
            options, statePublisher, NullLogger<ResiliencePipelineFactory>.Instance);
        var pipeline = factory.CreateEventBusPipeline();

        var resilientBus = new ResilientEventBus(innerBus, pipeline,
            NullLogger<ResilientEventBus>.Instance);

        // Should not throw
        await resilientBus.SubscribeAsync("test.event", (_, _) => Task.CompletedTask);
    }
}
