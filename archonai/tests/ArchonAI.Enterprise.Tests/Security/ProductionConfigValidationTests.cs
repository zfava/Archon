using ArchonAI.Common.Observability;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Tests for production configuration validation — verifying that the startup health gate
/// correctly distinguishes healthy vs unhealthy configurations in production and dev modes.
/// </summary>
public sealed class ProductionConfigValidationTests : IDisposable
{
    public ProductionConfigValidationTests()
    {
        // Ensure clean state for each test
        ProductionConfigHealthCheck.Reset();
    }

    public void Dispose()
    {
        ProductionConfigHealthCheck.Reset();
    }

    // ── Healthy Production Configuration ─────────────────────────────────

    [Fact]
    public void FullyConfiguredProduction_ReportsHealthy()
    {
        var snapshot = CreateHealthyProductionSnapshot();

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.True(result.IsHealthy);
        Assert.False(result.HasCriticalFindings);
        Assert.True(result.IsProductionMode);
    }

    [Fact]
    public void FullyConfiguredDev_ReportsHealthy()
    {
        var snapshot = CreateHealthyDevSnapshot();

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.True(result.IsHealthy);
        Assert.False(result.HasCriticalFindings);
        Assert.False(result.IsProductionMode);
    }

    // ── JWT Validation ───────────────────────────────────────────────────

    [Fact]
    public void MissingJwtKey_Production_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { JwtSigningKey = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.True(result.HasCriticalFindings);
        Assert.False(result.IsHealthy);
        var finding = Assert.Single(result.Findings, f => f.Component == "JWT");
        Assert.Equal(ConfigSeverity.Critical, finding.Severity);
        Assert.Contains("not configured", finding.Message);
        Assert.NotNull(finding.Remediation);
    }

    [Fact]
    public void ShortJwtKey_Production_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { JwtSigningKey = "too-short" };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.True(result.HasCriticalFindings);
        var finding = Assert.Single(result.Findings, f => f.Component == "JWT");
        Assert.Equal(ConfigSeverity.Critical, finding.Severity);
        Assert.Contains("too short", finding.Message);
    }

    [Fact]
    public void ValidJwtKey_NoFinding()
    {
        var snapshot = CreateHealthyProductionSnapshot();

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.DoesNotContain(result.Findings, f => f.Component == "JWT");
    }

    // ── TOTP Validation ──────────────────────────────────────────────────

    [Fact]
    public void MissingTotpAndJwtKey_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with
        {
            TotpEncryptionKey = null,
            JwtSigningKey = null,
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.Contains(result.Findings,
            f => f.Component == "TOTP" && f.Severity == ConfigSeverity.Critical);
    }

    [Fact]
    public void MissingTotpKey_WithJwtFallback_Production_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { TotpEncryptionKey = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        var finding = Assert.Single(result.Findings, f => f.Component == "TOTP");
        Assert.Equal(ConfigSeverity.Critical, finding.Severity);
        Assert.Contains("not configured in production", finding.Message);
    }

    [Fact]
    public void MissingTotpKey_WithJwtFallback_Dev_IsInfo()
    {
        var snapshot = CreateHealthyDevSnapshot() with { TotpEncryptionKey = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        var finding = Assert.Single(result.Findings, f => f.Component == "TOTP");
        Assert.Equal(ConfigSeverity.Info, finding.Severity);
    }

    [Fact]
    public void DedicatedTotpKey_NoFinding()
    {
        var snapshot = CreateHealthyProductionSnapshot();

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.DoesNotContain(result.Findings, f => f.Component == "TOTP");
    }

    // ── Persistence Validation ───────────────────────────────────────────

    [Fact]
    public void MissingPostgres_Production_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { PersistenceConnectionString = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.True(result.HasCriticalFindings);
        var finding = Assert.Single(result.Findings, f => f.Component == "Persistence");
        Assert.Equal(ConfigSeverity.Critical, finding.Severity);
        Assert.Contains("data loss", finding.Message);
    }

    [Fact]
    public void MissingPostgres_Dev_IsInfo()
    {
        var snapshot = CreateHealthyDevSnapshot() with { PersistenceConnectionString = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        var finding = Assert.Single(result.Findings, f => f.Component == "Persistence");
        Assert.Equal(ConfigSeverity.Info, finding.Severity);
    }

    [Fact]
    public void ConfiguredPostgres_NoFinding()
    {
        var snapshot = CreateHealthyProductionSnapshot();

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.DoesNotContain(result.Findings, f => f.Component == "Persistence");
    }

    // ── Model Provider Validation ────────────────────────────────────────

    [Fact]
    public void NoCloudProviders_Production_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with
        {
            OpenAiApiKey = null,
            AnthropicApiKey = null,
            AzureOpenAiEnabled = false,
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.Contains(result.Findings,
            f => f.Component == "ModelProviders" && f.Severity == ConfigSeverity.Critical);
    }

    [Fact]
    public void NoCloudProviders_Dev_IsNotCritical()
    {
        var snapshot = CreateHealthyDevSnapshot() with
        {
            OpenAiApiKey = null,
            AnthropicApiKey = null,
            AzureOpenAiEnabled = false,
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.DoesNotContain(result.Findings,
            f => f.Component == "ModelProviders" && f.Severity == ConfigSeverity.Critical);
    }

    [Fact]
    public void EnabledProvider_MissingKey_IsWarning()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { OpenAiApiKey = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.Contains(result.Findings,
            f => f.Component == "ModelProviders" && f.Severity == ConfigSeverity.Warning
                && f.Message.Contains("OpenAI"));
    }

    [Fact]
    public void AzureOpenAi_MissingEndpoint_IsWarning()
    {
        var snapshot = CreateHealthyProductionSnapshot() with
        {
            AzureOpenAiEnabled = true,
            AzureOpenAiApiKey = "test-azure-key",
            AzureOpenAiEndpoint = null,
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.Contains(result.Findings,
            f => f.Component == "ModelProviders" && f.Severity == ConfigSeverity.Warning
                && f.Message.Contains("endpoint"));
    }

    [Fact]
    public void SingleCloudProvider_Sufficient_NoCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with
        {
            OpenAiApiKey = null,
            // Anthropic still has key
            AzureOpenAiEnabled = false,
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.DoesNotContain(result.Findings,
            f => f.Component == "ModelProviders" && f.Severity == ConfigSeverity.Critical);
    }

    // ── OIDC Validation ──────────────────────────────────────────────────

    [Fact]
    public void OidcLocalhostCallback_Production_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot() with
        {
            OidcCallbackBaseUrl = "https://localhost:5001",
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.Contains(result.Findings,
            f => f.Component == "OIDC" && f.Severity == ConfigSeverity.Critical);
    }

    [Fact]
    public void OidcLocalhostCallback_Dev_NoCritical()
    {
        var snapshot = CreateHealthyDevSnapshot() with
        {
            OidcCallbackBaseUrl = "https://localhost:5001",
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.DoesNotContain(result.Findings,
            f => f.Component == "OIDC" && f.Severity == ConfigSeverity.Critical);
    }

    [Fact]
    public void OidcHttpCallback_Production_IsWarning()
    {
        var snapshot = CreateHealthyProductionSnapshot() with
        {
            OidcCallbackBaseUrl = "http://app.archonai.com",
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.Contains(result.Findings,
            f => f.Component == "OIDC" && f.Severity == ConfigSeverity.Warning
                && f.Message.Contains("HTTPS"));
    }

    [Fact]
    public void OidcNotConfigured_NoFinding()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { OidcCallbackBaseUrl = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.DoesNotContain(result.Findings, f => f.Component == "OIDC");
    }

    // ── Connector Validation ─────────────────────────────────────────────

    [Fact]
    public void EnabledConnector_MissingCredentials_Production_IsCritical()
    {
        var snapshot = CreateHealthyProductionSnapshot();
        snapshot.Connectors["Salesforce"] = new ConnectorConfigState
        {
            Enabled = true,
            HasClientId = false,
            HasClientSecret = false,
            HasEndpoint = true,
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.Contains(result.Findings,
            f => f.Component == "Connectors" && f.Severity == ConfigSeverity.Critical
                && f.Message.Contains("Salesforce"));
    }

    [Fact]
    public void EnabledConnector_MissingCredentials_Dev_IsInfo()
    {
        var snapshot = CreateHealthyDevSnapshot();
        snapshot.Connectors["Salesforce"] = new ConnectorConfigState
        {
            Enabled = true,
            HasClientId = false,
            HasClientSecret = false,
            HasEndpoint = true,
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        var finding = Assert.Single(result.Findings, f => f.Component == "Connectors");
        Assert.Equal(ConfigSeverity.Info, finding.Severity);
    }

    [Fact]
    public void DisabledConnector_MissingCredentials_NoFinding()
    {
        var snapshot = CreateHealthyProductionSnapshot();
        snapshot.Connectors["Salesforce"] = new ConnectorConfigState
        {
            Enabled = false,
            HasClientId = false,
            HasClientSecret = false,
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.DoesNotContain(result.Findings,
            f => f.Component == "Connectors" && f.Message.Contains("Salesforce"));
    }

    [Fact]
    public void FullyConfiguredConnector_NoFinding()
    {
        var snapshot = CreateHealthyProductionSnapshot();
        snapshot.Connectors["Salesforce"] = new ConnectorConfigState
        {
            Enabled = true,
            HasClientId = true,
            HasClientSecret = true,
            HasEndpoint = true,
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.DoesNotContain(result.Findings,
            f => f.Component == "Connectors" && f.Message.Contains("Salesforce"));
    }

    // ── Health Check Integration ─────────────────────────────────────────

    [Fact]
    public async Task HealthCheck_BeforeValidation_ReportsUnhealthy()
    {
        var check = new ProductionConfigHealthCheck();

        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
        Assert.Contains("not run yet", result.Description);
    }

    [Fact]
    public async Task HealthCheck_CriticalInProduction_ReportsUnhealthy()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { PersistenceConnectionString = null };
        var validationResult = ProductionConfigValidator.Validate(snapshot);
        ProductionConfigHealthCheck.SetResult(validationResult);

        var check = new ProductionConfigHealthCheck();
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Critical issues", result.Description);
    }

    [Fact]
    public async Task HealthCheck_CriticalInDev_ReportsDegraded()
    {
        var snapshot = CreateHealthyDevSnapshot() with { PersistenceConnectionString = null };
        // Also remove cloud providers to add model provider warnings
        var validationResult = ProductionConfigValidator.Validate(snapshot);
        ProductionConfigHealthCheck.SetResult(validationResult);

        // Persistence finding is Info in dev, not critical. Need to create a critical finding.
        // In dev mode, missing persistence is Info. Let's remove JWT key instead.
        var snapshot2 = CreateHealthyDevSnapshot() with { JwtSigningKey = null };
        var validationResult2 = ProductionConfigValidator.Validate(snapshot2);
        ProductionConfigHealthCheck.SetResult(validationResult2);

        var check = new ProductionConfigHealthCheck();
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
        Assert.Contains("critical in production", result.Description);
    }

    [Fact]
    public async Task HealthCheck_HealthyProduction_ReportsHealthy()
    {
        var snapshot = CreateHealthyProductionSnapshot();
        var validationResult = ProductionConfigValidator.Validate(snapshot);
        ProductionConfigHealthCheck.SetResult(validationResult);

        var check = new ProductionConfigHealthCheck();
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task HealthCheck_WarningsOnly_ReportsDegraded()
    {
        // Use a warning-level finding (enabled OpenAI without API key) — not a critical one
        var snapshot = CreateHealthyProductionSnapshot() with { OpenAiApiKey = null };
        var validationResult = ProductionConfigValidator.Validate(snapshot);
        // Confirm the snapshot produces only warnings, not criticals
        Assert.True(validationResult.HasWarnings, "Test setup: expected at least one warning");
        Assert.False(validationResult.HasCriticalFindings, "Test setup: expected no critical findings");
        ProductionConfigHealthCheck.SetResult(validationResult);

        var check = new ProductionConfigHealthCheck();
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
        Assert.Contains("warnings", result.Description);
    }

    // ── IsHealthy semantics ──────────────────────────────────────────────

    [Fact]
    public void IsHealthy_Production_CriticalFindings_IsFalse()
    {
        var snapshot = CreateHealthyProductionSnapshot() with { PersistenceConnectionString = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.False(result.IsHealthy);
    }

    [Fact]
    public void IsHealthy_Dev_CriticalFindings_IsTrue()
    {
        // Dev mode critical findings (like missing JWT) should NOT block IsHealthy
        var snapshot = CreateHealthyDevSnapshot() with { JwtSigningKey = null };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.True(result.HasCriticalFindings);
        Assert.True(result.IsHealthy, "Dev mode should not block on critical findings");
    }

    // ── Multiple concurrent issues ───────────────────────────────────────

    [Fact]
    public void MultipleIssues_AllReported()
    {
        var snapshot = new ConfigSnapshot
        {
            EnvironmentName = "Production",
            IsProductionLike = true,
            JwtSigningKey = "short",
            TotpEncryptionKey = null,
            PersistenceConnectionString = null,
            OpenAiEnabled = true,
            OpenAiApiKey = null,
            AnthropicEnabled = true,
            AnthropicApiKey = null,
            AzureOpenAiEnabled = false,
            OidcCallbackBaseUrl = "http://localhost:5001",
            Connectors = new()
            {
                ["Salesforce"] = new ConnectorConfigState
                {
                    Enabled = true,
                    HasClientId = false,
                    HasClientSecret = false,
                },
            },
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.True(result.HasCriticalFindings);
        Assert.False(result.IsHealthy);

        // Should have findings across multiple components
        var components = result.Findings.Select(f => f.Component).Distinct().ToList();
        Assert.Contains("JWT", components);
        Assert.Contains("Persistence", components);
        Assert.Contains("ModelProviders", components);
        Assert.Contains("OIDC", components);
        Assert.Contains("Connectors", components);

        // Every finding should have a non-empty message
        Assert.All(result.Findings, f => Assert.False(string.IsNullOrWhiteSpace(f.Message)));

        // Critical findings should have remediation guidance
        Assert.All(
            result.Findings.Where(f => f.Severity == ConfigSeverity.Critical),
            f => Assert.NotNull(f.Remediation));
    }

    // ── Staging treated as production ────────────────────────────────────

    [Fact]
    public void Staging_TreatedAsProduction()
    {
        var snapshot = CreateHealthyProductionSnapshot() with
        {
            EnvironmentName = "Staging",
            IsProductionLike = true,
            PersistenceConnectionString = null,
        };

        var result = ProductionConfigValidator.Validate(snapshot);

        Assert.True(result.IsProductionMode);
        Assert.True(result.HasCriticalFindings);
        Assert.False(result.IsHealthy);
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
