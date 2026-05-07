using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using ArchonAI.Infrastructure.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchonAI.Connectors.Tests.Resilience;

public class FallbackChainTests
{
    private sealed class FakeProvider : IModelProvider
    {
        private readonly bool _shouldFail;
        private readonly bool _circuitOpen;

        public FakeProvider(string name, bool shouldFail = false, bool circuitOpen = false)
        {
            ProviderName = name;
            _shouldFail = shouldFail;
            _circuitOpen = circuitOpen;
        }

        public string ProviderName { get; }

        public bool CanHandle(string model) => true;

        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            if (_circuitOpen)
                throw new ProviderCircuitOpenException(ProviderName);

            if (_shouldFail)
                throw new HttpRequestException($"Provider {ProviderName} unavailable");

            return Task.FromResult(new ModelResponse(
                Provider: ProviderName,
                Model: request.Model ?? "test",
                IsSuccess: true,
                Content: $"Response from {ProviderName}",
                Warnings: Array.Empty<string>(),
                Errors: Array.Empty<string>(),
                CompletedAtUtc: DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public async Task CompositeProvider_Routes_To_Next_When_Circuit_Open()
    {
        // Arrange: first provider has circuit open, second is healthy
        var providers = new IModelProvider[]
        {
            new FakeProvider("openai", circuitOpen: true),
            new FakeProvider("anthropic")
        };

        var composite = new CompositeModelProvider(providers,
            NullLogger<CompositeModelProvider>.Instance);

        var request = new ModelRequest(
            Model: "test-model",
            Prompt: "test",
            Parameters: new Dictionary<string, string>(),
            RequestedBy: "test-user",
            RequestedAtUtc: DateTimeOffset.UtcNow);

        // Act
        var response = await composite.GenerateAsync(request);

        // Assert: routed to anthropic (fallback)
        Assert.Equal("anthropic", response.Provider);
        Assert.Contains("anthropic", response.Content);
    }

    [Fact]
    public async Task CompositeProvider_Throws_Clear_Error_When_All_Down()
    {
        var providers = new IModelProvider[]
        {
            new FakeProvider("openai", circuitOpen: true),
            new FakeProvider("anthropic", circuitOpen: true),
            new FakeProvider("local", shouldFail: true)
        };

        var composite = new CompositeModelProvider(providers,
            NullLogger<CompositeModelProvider>.Instance);

        var request = new ModelRequest(
            Model: "test-model",
            Prompt: "test",
            Parameters: new Dictionary<string, string>(),
            RequestedBy: "test-user",
            RequestedAtUtc: DateTimeOffset.UtcNow);

        // Act & Assert: should throw AllProvidersUnavailableException, not return an echo stub
        var ex = await Assert.ThrowsAsync<AllProvidersUnavailableException>(
            () => composite.GenerateAsync(request));

        Assert.Contains("All model providers are unavailable", ex.Message);
        Assert.Contains("trust-tier downgrade", ex.Message);
    }

    [Fact]
    public async Task CompositeProvider_Uses_First_Available()
    {
        var providers = new IModelProvider[]
        {
            new FakeProvider("openai"),
            new FakeProvider("anthropic"),
        };

        var composite = new CompositeModelProvider(providers,
            NullLogger<CompositeModelProvider>.Instance);

        var request = new ModelRequest(
            Model: "test-model",
            Prompt: "test",
            Parameters: new Dictionary<string, string>(),
            RequestedBy: "test-user",
            RequestedAtUtc: DateTimeOffset.UtcNow);

        var response = await composite.GenerateAsync(request);

        Assert.Equal("openai", response.Provider);
    }
}
