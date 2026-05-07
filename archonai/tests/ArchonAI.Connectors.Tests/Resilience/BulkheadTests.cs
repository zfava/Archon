using ArchonAI.Infrastructure.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.Bulkhead;
using Xunit;

namespace ArchonAI.Connectors.Tests.Resilience;

public class BulkheadTests
{
    [Fact]
    public async Task Bulkhead_Rejects_When_Capacity_Exceeded()
    {
        // Arrange: very small bulkhead (1 concurrent, 0 queue)
        var options = new ResilienceOptions
        {
            Connectors = new ConnectorResilienceOptions
            {
                CircuitBreakerThreshold = 100, // high threshold so circuit doesn't open
                CircuitBreakerDurationSeconds = 30,
                MaxConcurrentCalls = 1,
                MaxQueueDepth = 0,
                TimeoutSeconds = 30
            }
        };

        var statePublisher = new CircuitBreakerStatePublisher(
            NullLogger<CircuitBreakerStatePublisher>.Instance);

        var factory = new ResiliencePipelineFactory(
            options, statePublisher, NullLogger<ResiliencePipelineFactory>.Instance);

        var pipeline = factory.CreateConnectorHttpPipeline("test-bulkhead");

        // Act: fire many concurrent requests — expect some to be rejected
        var tcs = new TaskCompletionSource<bool>();
        var tasks = new List<Task<System.Net.Http.HttpResponseMessage>>();
        var rejections = 0;

        // First task blocks inside the bulkhead
        var blockingTask = pipeline.ExecuteAsync(async ct =>
        {
            await tcs.Task; // block until released
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }, CancellationToken.None);

        // Give the first task time to enter the bulkhead
        await Task.Delay(50);

        // Second task should be rejected (bulkhead full, queue = 0)
        try
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK);
            }, CancellationToken.None);
        }
        catch (BulkheadRejectedException)
        {
            rejections++;
        }

        // Release the blocking task
        tcs.SetResult(true);
        await blockingTask;

        // Assert
        Assert.True(rejections > 0, "Expected at least one bulkhead rejection");
    }
}
