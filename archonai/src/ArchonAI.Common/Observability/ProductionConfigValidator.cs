using Microsoft.Extensions.Logging;

namespace ArchonAI.Common.Observability;

/// <summary>
/// Severity level for a configuration validation finding.
/// </summary>
public enum ConfigSeverity
{
    /// <summary>Informational — no action required.</summary>
    Info,
    /// <summary>Acceptable in dev/test but not recommended for production.</summary>
    Warning,
    /// <summary>Will cause operational failure or data loss in production.</summary>
    Critical,
}

/// <summary>
/// A single configuration validation finding with structured operator-facing diagnostics.
/// </summary>
public sealed record ConfigValidationFinding(
    string Component,
    ConfigSeverity Severity,
    string Message,
    string? Remediation = null);

/// <summary>
/// Result of a full configuration validation pass.
/// </summary>
public sealed class ConfigValidationResult
{
    public bool IsProductionMode { get; init; }
    public string Environment { get; init; } = "Unknown";
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<ConfigValidationFinding> Findings { get; init; } = [];

    public bool HasCriticalFindings => Findings.Any(f => f.Severity == ConfigSeverity.Critical);
    public bool HasWarnings => Findings.Any(f => f.Severity == ConfigSeverity.Warning);

    /// <summary>
    /// Returns true if the system is safe to accept traffic.
    /// In production mode, any critical finding means the system is NOT healthy.
    /// In dev/test mode, critical findings are downgraded to warnings.
    /// </summary>
    public bool IsHealthy => !IsProductionMode || !HasCriticalFindings;
}

/// <summary>
/// Configuration state snapshot consumed by <see cref="ProductionConfigValidator"/>.
/// Populated by the host at startup from IConfiguration / environment variables.
/// </summary>
public sealed record ConfigSnapshot
{
    // Environment
    public string EnvironmentName { get; init; } = "Development";
    public bool IsProductionLike { get; init; }

    // JWT
    public string? JwtSigningKey { get; init; }

    // TOTP
    public string? TotpEncryptionKey { get; init; }

    // PostgreSQL persistence
    public string? PersistenceConnectionString { get; init; }

    // Subsystem persistence — each independently configurable
    public string? MemoryPersistenceConnectionString { get; init; }
    public string? KnowledgeGraphConnectionString { get; init; }
    public string? TelemetryConnectionString { get; init; }
    public bool EventBusUseNats { get; init; }

    // Model providers
    public bool OpenAiEnabled { get; init; }
    public string? OpenAiApiKey { get; init; }
    public bool AnthropicEnabled { get; init; }
    public string? AnthropicApiKey { get; init; }
    public bool AzureOpenAiEnabled { get; init; }
    public string? AzureOpenAiApiKey { get; init; }
    public string? AzureOpenAiEndpoint { get; init; }
    public bool LocalModelEnabled { get; init; }

    // OIDC
    public string? OidcCallbackBaseUrl { get; init; }

    // Connectors — keyed by name, value is whether OAuth client credentials are present
    public Dictionary<string, ConnectorConfigState> Connectors { get; init; } = new();
}

/// <summary>
/// Summarized configuration state for a single connector.
/// </summary>
public sealed class ConnectorConfigState
{
    public bool Enabled { get; init; }
    public bool HasClientId { get; init; }
    public bool HasClientSecret { get; init; }
    public bool HasEndpoint { get; init; }
}

/// <summary>
/// Validates enterprise-critical configuration at startup and produces structured diagnostics.
///
/// Design principles:
/// - In production/staging: critical misconfigurations cause the validator to report unhealthy,
///   which flows into the readiness health check so K8s will not route traffic.
/// - In development/test: the same misconfigurations emit warnings but do not block startup,
///   preserving local-dev ergonomics.
/// - Every finding includes operator-friendly remediation guidance.
/// </summary>
public static class ProductionConfigValidator
{
    public static ConfigValidationResult Validate(ConfigSnapshot config)
    {
        var findings = new List<ConfigValidationFinding>();

        ValidateJwt(config, findings);
        ValidateTotp(config, findings);
        ValidatePersistence(config, findings);
        ValidateSubsystemPersistence(config, findings);
        ValidateModelProviders(config, findings);
        ValidateOidc(config, findings);
        ValidateConnectors(config, findings);

        return new ConfigValidationResult
        {
            IsProductionMode = config.IsProductionLike,
            Environment = config.EnvironmentName,
            Findings = findings.AsReadOnly(),
        };
    }

    /// <summary>
    /// Logs all findings to the provided logger with appropriate severity levels.
    /// </summary>
    public static void LogResult(ConfigValidationResult result, ILogger logger)
    {
        logger.LogInformation(
            "╔══════════════════════════════════════════════════════════════════════╗");
        logger.LogInformation(
            "║            PRODUCTION CONFIGURATION VALIDATION                     ║");
        logger.LogInformation(
            "║  Environment: {Env,-15}  Mode: {Mode,-18}             ║",
            result.Environment,
            result.IsProductionMode ? "PRODUCTION" : "DEVELOPMENT");
        logger.LogInformation(
            "╠══════════════════════════════════════════════════════════════════════╣");

        if (result.Findings.Count == 0)
        {
            logger.LogInformation(
                "║  All configuration checks passed.                                 ║");
        }

        foreach (var finding in result.Findings)
        {
            switch (finding.Severity)
            {
                case ConfigSeverity.Critical:
                    logger.LogCritical(
                        "[{Component}] {Message}{Remediation}",
                        finding.Component, finding.Message,
                        finding.Remediation is not null ? $" Remediation: {finding.Remediation}" : "");
                    break;
                case ConfigSeverity.Warning:
                    logger.LogWarning(
                        "[{Component}] {Message}{Remediation}",
                        finding.Component, finding.Message,
                        finding.Remediation is not null ? $" Remediation: {finding.Remediation}" : "");
                    break;
                default:
                    logger.LogInformation(
                        "[{Component}] {Message}",
                        finding.Component, finding.Message);
                    break;
            }
        }

        var status = result.HasCriticalFindings
            ? (result.IsProductionMode ? "UNHEALTHY — startup blocked" : "DEGRADED — acceptable for dev")
            : "HEALTHY";

        logger.LogInformation(
            "╠══════════════════════════════════════════════════════════════════════╣");
        logger.LogInformation(
            "║  Result: {Status,-58} ║", status);
        logger.LogInformation(
            "║  Critical: {Critical}  Warnings: {Warnings}  Info: {Info}{Padding}║",
            result.Findings.Count(f => f.Severity == ConfigSeverity.Critical),
            result.Findings.Count(f => f.Severity == ConfigSeverity.Warning),
            result.Findings.Count(f => f.Severity == ConfigSeverity.Info),
            new string(' ', 30));
        logger.LogInformation(
            "╚══════════════════════════════════════════════════════════════════════╝");
    }

    // ── Individual validators ────────────────────────────────────────────────

    private static void ValidateJwt(ConfigSnapshot config, List<ConfigValidationFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(config.JwtSigningKey))
        {
            findings.Add(new("JWT", ConfigSeverity.Critical,
                "JWT signing key is not configured.",
                "Set ARCHONAI_JWT_SIGNING_KEY (>=32 chars) or Security:Jwt:SigningKey via secure config."));
            return;
        }

        if (config.JwtSigningKey.Length < 32)
        {
            findings.Add(new("JWT", ConfigSeverity.Critical,
                $"JWT signing key is too short ({config.JwtSigningKey.Length} chars, minimum 32).",
                "Set ARCHONAI_JWT_SIGNING_KEY to a value with at least 32 characters."));
        }
    }

    private static void ValidateTotp(ConfigSnapshot config, List<ConfigValidationFinding> findings)
    {
        bool hasDedicatedKey = !string.IsNullOrWhiteSpace(config.TotpEncryptionKey);
        bool hasJwtFallback = !string.IsNullOrWhiteSpace(config.JwtSigningKey);

        if (!hasDedicatedKey && !hasJwtFallback)
        {
            findings.Add(new("TOTP", ConfigSeverity.Critical,
                "No TOTP encryption key and no JWT signing key available. MFA will fail at runtime.",
                "Set ARCHONAI_TOTP_ENCRYPTION_KEY (preferred) or ensure ARCHONAI_JWT_SIGNING_KEY is set (dev only)."));
        }
        else if (!hasDedicatedKey && config.IsProductionLike)
        {
            // Critical in production: JWT fallback creates lifecycle coupling where
            // JWT key rotation destroys all TOTP secrets. The encryptor will refuse
            // to operate without a dedicated key in production-like environments.
            findings.Add(new("TOTP", ConfigSeverity.Critical,
                "TOTP encryption key (ARCHONAI_TOTP_ENCRYPTION_KEY) is not configured in production. " +
                "JWT key fallback is not permitted — TOTP enrollment and verification will fail. " +
                "Rotating the JWT signing key would destroy all existing TOTP secrets.",
                "Set ARCHONAI_TOTP_ENCRYPTION_KEY to a unique, high-entropy value: openssl rand -base64 48"));
        }
        else if (!hasDedicatedKey)
        {
            findings.Add(new("TOTP", ConfigSeverity.Info,
                "TOTP encryption using JWT key fallback (acceptable for development)."));
        }
    }

    private static void ValidatePersistence(ConfigSnapshot config, List<ConfigValidationFinding> findings)
    {
        bool hasConnectionString = !string.IsNullOrWhiteSpace(config.PersistenceConnectionString);

        if (!hasConnectionString && config.IsProductionLike)
        {
            findings.Add(new("Persistence", ConfigSeverity.Critical,
                "PostgreSQL connection string is not configured in a production-like environment. " +
                "In-memory persistence will cause data loss on restart.",
                "Set ArchonAIPersistence:ConnectionString to a valid PostgreSQL connection string."));
        }
        else if (!hasConnectionString)
        {
            findings.Add(new("Persistence", ConfigSeverity.Info,
                "Using in-memory persistence (acceptable for local development)."));
        }
    }

    private static void ValidateSubsystemPersistence(ConfigSnapshot config, List<ConfigValidationFinding> findings)
    {
        // Event Bus
        if (!config.EventBusUseNats && config.IsProductionLike)
        {
            findings.Add(new("EventBus", ConfigSeverity.Critical,
                "Event bus is configured to use in-memory transport in a production-like environment. " +
                "Events will not be shared across instances and will be lost on restart.",
                "Set EventBus:UseNats=true and configure EventBus:Url to a NATS server."));
        }
        else if (!config.EventBusUseNats)
        {
            findings.Add(new("EventBus", ConfigSeverity.Info,
                "Using in-memory event bus (acceptable for local development)."));
        }

        // Memory store
        if (string.IsNullOrWhiteSpace(config.MemoryPersistenceConnectionString) && config.IsProductionLike)
        {
            findings.Add(new("MemoryStore", ConfigSeverity.Critical,
                "Agent memory store is using in-memory persistence in a production-like environment. " +
                "Memory records and embeddings will be lost on restart.",
                "Set MemoryPersistence:ConnectionString to a valid PostgreSQL connection string."));
        }
        else if (string.IsNullOrWhiteSpace(config.MemoryPersistenceConnectionString))
        {
            findings.Add(new("MemoryStore", ConfigSeverity.Info,
                "Using in-memory memory store (acceptable for local development)."));
        }

        // Knowledge graph
        if (string.IsNullOrWhiteSpace(config.KnowledgeGraphConnectionString) && config.IsProductionLike)
        {
            findings.Add(new("KnowledgeGraph", ConfigSeverity.Critical,
                "Knowledge graph is using in-memory persistence in a production-like environment. " +
                "Graph data will be lost on restart.",
                "Set KnowledgeGraph:ConnectionString to a valid PostgreSQL connection string."));
        }
        else if (string.IsNullOrWhiteSpace(config.KnowledgeGraphConnectionString))
        {
            findings.Add(new("KnowledgeGraph", ConfigSeverity.Info,
                "Using in-memory knowledge graph (acceptable for local development)."));
        }

        // Telemetry
        if (string.IsNullOrWhiteSpace(config.TelemetryConnectionString) && config.IsProductionLike)
        {
            findings.Add(new("Telemetry", ConfigSeverity.Critical,
                "Task telemetry store is using in-memory persistence in a production-like environment. " +
                "Telemetry data will be lost on restart and bounded to a fixed queue size.",
                "Set TelemetryPersistence:ConnectionString to a valid PostgreSQL connection string."));
        }
        else if (string.IsNullOrWhiteSpace(config.TelemetryConnectionString))
        {
            findings.Add(new("Telemetry", ConfigSeverity.Info,
                "Using in-memory telemetry store (acceptable for local development)."));
        }
    }

    private static void ValidateModelProviders(ConfigSnapshot config, List<ConfigValidationFinding> findings)
    {
        bool hasAnyCloudProvider = false;

        if (config.OpenAiEnabled && string.IsNullOrWhiteSpace(config.OpenAiApiKey))
        {
            findings.Add(new("ModelProviders", ConfigSeverity.Warning,
                "OpenAI is enabled but OPENAI_API_KEY is not set. Provider will be inactive.",
                "Set OPENAI_API_KEY or disable OpenAI in ModelProviders:OpenAI:Enabled."));
        }
        else if (config.OpenAiEnabled && !string.IsNullOrWhiteSpace(config.OpenAiApiKey))
        {
            hasAnyCloudProvider = true;
        }

        if (config.AnthropicEnabled && string.IsNullOrWhiteSpace(config.AnthropicApiKey))
        {
            findings.Add(new("ModelProviders", ConfigSeverity.Warning,
                "Anthropic is enabled but ANTHROPIC_API_KEY is not set. Provider will be inactive.",
                "Set ANTHROPIC_API_KEY or disable Anthropic in ModelProviders:Anthropic:Enabled."));
        }
        else if (config.AnthropicEnabled && !string.IsNullOrWhiteSpace(config.AnthropicApiKey))
        {
            hasAnyCloudProvider = true;
        }

        if (config.AzureOpenAiEnabled)
        {
            if (string.IsNullOrWhiteSpace(config.AzureOpenAiApiKey))
            {
                findings.Add(new("ModelProviders", ConfigSeverity.Warning,
                    "Azure OpenAI is enabled but AZURE_OPENAI_API_KEY is not set.",
                    "Set AZURE_OPENAI_API_KEY or disable AzureOpenAI in ModelProviders:AzureOpenAI:Enabled."));
            }
            else if (string.IsNullOrWhiteSpace(config.AzureOpenAiEndpoint))
            {
                findings.Add(new("ModelProviders", ConfigSeverity.Warning,
                    "Azure OpenAI API key is set but endpoint is missing.",
                    "Set AZURE_OPENAI_ENDPOINT to your Azure OpenAI resource URL."));
            }
            else
            {
                hasAnyCloudProvider = true;
            }
        }

        if (!hasAnyCloudProvider && config.IsProductionLike)
        {
            findings.Add(new("ModelProviders", ConfigSeverity.Critical,
                "No cloud AI provider credentials are configured in production mode. " +
                "All AI endpoints will fail unless a local model server is reachable.",
                "Configure at least one of: OPENAI_API_KEY, ANTHROPIC_API_KEY, " +
                "or AZURE_OPENAI_API_KEY + AZURE_OPENAI_ENDPOINT."));
        }
    }

    private static void ValidateOidc(ConfigSnapshot config, List<ConfigValidationFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(config.OidcCallbackBaseUrl))
            return; // OIDC not configured at all — fine

        bool isLocalhostCallback = config.OidcCallbackBaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || config.OidcCallbackBaseUrl.Contains("127.0.0.1", StringComparison.Ordinal);

        if (config.IsProductionLike && isLocalhostCallback)
        {
            findings.Add(new("OIDC", ConfigSeverity.Critical,
                $"OIDC CallbackBaseUrl is set to a localhost address ({config.OidcCallbackBaseUrl}) in production. " +
                "OIDC redirects will fail for external users.",
                "Set Oidc:CallbackBaseUrl to the production-accessible URL (e.g. https://app.archonai.com)."));
        }

        if (!config.OidcCallbackBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && config.IsProductionLike)
        {
            findings.Add(new("OIDC", ConfigSeverity.Warning,
                "OIDC CallbackBaseUrl does not use HTTPS. Most identity providers require HTTPS redirect URIs.",
                "Set Oidc:CallbackBaseUrl to an HTTPS URL."));
        }
    }

    private static void ValidateConnectors(ConfigSnapshot config, List<ConfigValidationFinding> findings)
    {
        foreach (var (name, state) in config.Connectors)
        {
            if (!state.Enabled)
                continue;

            var missing = new List<string>();
            if (!state.HasClientId) missing.Add("ClientId");
            if (!state.HasClientSecret) missing.Add("ClientSecret");

            if (missing.Count > 0 && config.IsProductionLike)
            {
                findings.Add(new("Connectors", ConfigSeverity.Critical,
                    $"Connector '{name}' is enabled but missing: {string.Join(", ", missing)}. " +
                    "OAuth authentication will fail at runtime.",
                    $"Set Connectors:{name}:ClientId and Connectors:{name}:ClientSecret, or disable the connector."));
            }
            else if (missing.Count > 0)
            {
                findings.Add(new("Connectors", ConfigSeverity.Info,
                    $"Connector '{name}' is enabled but missing credentials (acceptable for development)."));
            }
        }
    }
}
