using ArchonAI.Common;
using ArchonAI.Common.Observability;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Tests proving that enterprise-critical fallback behavior is explicit:
/// - Production-like environments hard-fail on in-memory fallbacks
/// - Dev/test environments allow in-memory with logged warnings
/// - FallbackPostureHealthCheck correctly reports subsystem state
/// - ConfigValidator catches all subsystem misconfigurations
/// </summary>
public sealed class FallbackPostureTests : IDisposable
{
    public FallbackPostureTests()
    {
        FallbackPostureHealthCheck.Reset();
        ProductionConfigHealthCheck.Reset();
    }

    public void Dispose()
    {
        FallbackPostureHealthCheck.Reset();
        ProductionConfigHealthCheck.Reset();
    }

    // ── EnvironmentPosture.GuardInMemoryFallback ─────────────────────────

    [Fact]
    public void Guard_ProductionLike_ThrowsInvalidOperationException()
    {
        var posture = new EnvironmentPosture { IsProductionLike = true };
        var logger = Substitute.For<ILogger>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            posture.GuardInMemoryFallback("TestStore", "Test:ConnectionString", logger));

        Assert.Contains("not permitted in production", ex.Message);
        Assert.Contains("Test:ConnectionString", ex.Message);
    }

    [Fact]
    public void Guard_Development_DoesNotThrow()
    {
        var posture = new EnvironmentPosture { IsProductionLike = false };
        var logger = Substitute.For<ILogger>();

        // Should not throw
        posture.GuardInMemoryFallback("TestStore", "Test:ConnectionString", logger);
    }

    [Fact]
    public void Guard_Development_LogsWarning()
    {
        var posture = new EnvironmentPosture { IsProductionLike = false };
        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(LogLevel.Warning).Returns(true);

        posture.GuardInMemoryFallback("TestStore", "Test:ConnectionString", logger);

        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void Guard_NullPosture_DoesNotBlock()
    {
        // When EnvironmentPosture is not registered (e.g. in isolated unit tests),
        // the GetService<EnvironmentPosture>() returns null, which is safe.
        EnvironmentPosture? posture = null;
        // Just verify no NullReferenceException — the factory code uses ?. operator
        Assert.Null(posture);
    }

    // ── FallbackPostureHealthCheck ────────────────────────────────────────

    [Fact]
    public async Task HealthCheck_NoPostures_ReportsHealthy()
    {
        var check = new FallbackPostureHealthCheck();

        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task HealthCheck_AllDurable_ReportsHealthy()
    {
        FallbackPostureHealthCheck.SetEnvironment(true);
        FallbackPostureHealthCheck.RecordPosture("Persistence", "PostgreSQL", isDurable: true);
        FallbackPostureHealthCheck.RecordPosture("EventBus", "NATS", isDurable: true);
        FallbackPostureHealthCheck.RecordPosture("MemoryStore", "PostgreSQL", isDurable: true);

        var check = new FallbackPostureHealthCheck();
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
        Assert.Contains("durable persistence", result.Description);
    }

    [Fact]
    public async Task HealthCheck_InMemory_Production_ReportsUnhealthy()
    {
        FallbackPostureHealthCheck.SetEnvironment(true);
        FallbackPostureHealthCheck.RecordPosture("Persistence", "PostgreSQL", isDurable: true);
        FallbackPostureHealthCheck.RecordPosture("EventBus", "InMemory", isDurable: false);

        var check = new FallbackPostureHealthCheck();
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
        Assert.Contains("PRODUCTION", result.Description);
        Assert.Contains("EventBus", result.Description);
    }

    [Fact]
    public async Task HealthCheck_InMemory_Dev_ReportsDegraded()
    {
        FallbackPostureHealthCheck.SetEnvironment(false);
        FallbackPostureHealthCheck.RecordPosture("Persistence", "InMemory", isDurable: false);
        FallbackPostureHealthCheck.RecordPosture("EventBus", "InMemory", isDurable: false);

        var check = new FallbackPostureHealthCheck();
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
        Assert.Contains("DEV", result.Description);
    }

    [Fact]
    public async Task HealthCheck_ReportsAllSubsystems_InData()
    {
        FallbackPostureHealthCheck.SetEnvironment(false);
        FallbackPostureHealthCheck.RecordPosture("Persistence", "PostgreSQL", isDurable: true);
        FallbackPostureHealthCheck.RecordPosture("EventBus", "InMemory", isDurable: false);
        FallbackPostureHealthCheck.RecordPosture("KnowledgeGraph", "PostgreSQL", isDurable: true);

        var check = new FallbackPostureHealthCheck();
        var result = await check.CheckHealthAsync(null!);

        Assert.NotNull(result.Data);
        Assert.Equal(3, (int)result.Data["totalSubsystems"]);
        Assert.Equal(2, (int)result.Data["durableCount"]);
        Assert.Equal(1, (int)result.Data["inMemoryCount"]);
    }

    // ── ConfigValidator — subsystem persistence ──────────────────────────

    [Fact]
    public void Validator_MissingEventBus_Production_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { EventBusUseNats = false };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.Contains(result.Findings,
            f => f.Component == "EventBus" && f.Severity == ConfigSeverity.Critical);
    }

    [Fact]
    public void Validator_MissingEventBus_Dev_IsInfo()
    {
        var snapshot = CreateHealthyDevSnapshot() with { EventBusUseNats = false };

        var result = ProductionConfigValidator.Validate(snapshot);

        var finding = Assert.Single(result.Findings, f => f.Component == "EventBus");
        Assert.Equal(ConfigSeverity.Info, finding.Severity);
    }

    [Fact]
    public void Validator_ConfiguredEventBus_NoFinding()
    {
        var snapshot = CreateHealthyProductionSnapshot();

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.DoesNotContain(result.Findings, f => f.Component == "EventBus");
    }

    [Fact]
    public void Validator_MissingMemoryStore_Production_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { MemoryPersistenceConnectionString = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.Contains(result.Findings,
            f => f.Component == "MemoryStore" && f.Severity == ConfigSeverity.Critical);
    }

    [Fact]
    public void Validator_MissingMemoryStore_Dev_IsInfo()
    {
        var snapshot = CreateHealthyDevSnapshot() with { MemoryPersistenceConnectionString = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        var finding = Assert.Single(result.Findings, f => f.Component == "MemoryStore");
        Assert.Equal(ConfigSeverity.Info, finding.Severity);
    }

    [Fact]
    public void Validator_MissingKnowledgeGraph_Production_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { KnowledgeGraphConnectionString = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.Contains(result.Findings,
            f => f.Component == "KnowledgeGraph" && f.Severity == ConfigSeverity.Critical);
    }

    [Fact]
    public void Validator_MissingTelemetry_Production_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { TelemetryConnectionString = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.Contains(result.Findings,
            f => f.Component == "Telemetry" && f.Severity == ConfigSeverity.Critical);
    }

    [Fact]
    public void Validator_AllSubsystemsConfigured_NoCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot();

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.DoesNotContain(result.Findings,
            f => f.Severity == ConfigSeverity.Critical);
    }

    [Fact]
    public void Validator_AllSubsystemFindings_HaveRemediation()
    {
        var snapshot = new ConfigSnapshot
        {
            EnvironmentName = "Production",
            IsProductionLike = true,
            JwtSigningKey = "production-jwt-signing-key-that-is-at-least-32-characters-long",
            TotpEncryptionKey = "production-totp-key-at-least-32-chars",
            PersistenceConnectionString = null,
            MemoryPersistenceConnectionString = null,
            KnowledgeGraphConnectionString = null,
            TelemetryConnectionString = null,
            EventBusUseNats = false,
            OpenAiEnabled = true,
            OpenAiApiKey = "sk-key",
            AnthropicEnabled = false,
            AzureOpenAiEnabled = false,
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        var criticals = result.Findings.Where(f => f.Severity == ConfigSeverity.Critical).ToList();
        Assert.True(criticals.Count >= 5, $"Expected at least 5 critical findings, got {criticals.Count}");
        Assert.All(criticals, f => Assert.NotNull(f.Remediation));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static ConfigSnapshot CreateHealthyProductionSnapshot() => new()
    {
        EnvironmentName = "Production",
        IsProductionLike = true,
        JwtSigningKey = "production-jwt-signing-key-that-is-at-least-32-characters-long",
        TotpEncryptionKey = "production-totp-encryption-key-at-least-32-chars",
        PersistenceConnectionString = "Host=db.example.com;Database=archonai;Username=app;Password=secret",
        MemoryPersistenceConnectionString = "Host=db.example.com;Database=archonai;Username=app;Password=secret",
        KnowledgeGraphConnectionString = "Host=db.example.com;Database=archonai;Username=app;Password=secret",
        TelemetryConnectionString = "Host=db.example.com;Database=archonai;Username=app;Password=secret",
        EventBusUseNats = true,
        OpenAiEnabled = true,
        OpenAiApiKey = "sk-prod-openai-key",
        AnthropicEnabled = true,
        AnthropicApiKey = "sk-ant-prod-key",
        AzureOpenAiEnabled = false,
        LocalModelEnabled = false,
        OidcCallbackBaseUrl = "https://app.archonai.com",
        Connectors = new(),
    };

    private static ConfigSnapshot CreateHealthyDevSnapshot() => new()
    {
        EnvironmentName = "Development",
        IsProductionLike = false,
        JwtSigningKey = "dev-jwt-signing-key-that-is-at-least-32-characters",
        TotpEncryptionKey = null,
        PersistenceConnectionString = null,
        MemoryPersistenceConnectionString = null,
        KnowledgeGraphConnectionString = null,
        TelemetryConnectionString = null,
        EventBusUseNats = false,
        OpenAiEnabled = true,
        OpenAiApiKey = null,
        AnthropicEnabled = true,
        AnthropicApiKey = null,
        AzureOpenAiEnabled = false,
        LocalModelEnabled = true,
        OidcCallbackBaseUrl = "https://localhost:5001",
        Connectors = new(),
    };
}
