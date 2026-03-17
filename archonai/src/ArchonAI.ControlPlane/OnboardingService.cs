using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Onboarding;
using Microsoft.Extensions.Logging;

namespace ArchonAI.ControlPlane;

public sealed class OnboardingService : IOnboardingService
{
    private readonly IControlPlaneService _controlPlane;
    private readonly ILogger<OnboardingService> _logger;

    public OnboardingService(
        IControlPlaneService controlPlane,
        ILogger<OnboardingService> logger)
    {
        _controlPlane = controlPlane;
        _logger = logger;
    }

    public async Task<OnboardingDeployResult> DeployAsync(OnboardingDeployRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Onboarding deployment started — systems={Systems}, businessType={BusinessType}, level={Level}",
            string.Join(",", request.ConnectedSystems), request.BusinessType, request.AutomationLevel);

        // 1. Configure automation level via control plane
        await _controlPlane.SetConfigurationAsync("default", "execution", "mode", request.AutomationLevel, ct: ct);

        // 2. Configure department-level overrides
        foreach (var dept in request.Departments)
        {
            await _controlPlane.SetConfigurationAsync(
                "default",
                $"department.{dept.Name.ToLowerInvariant()}",
                "automationLevel",
                dept.Level,
                ct: ct);
        }

        // 3. Register connected systems
        foreach (var system in request.ConnectedSystems)
        {
            await _controlPlane.SetConfigurationAsync("default", "integrations", system, "connected", ct: ct);
        }

        // 4. Store business type
        await _controlPlane.SetConfigurationAsync("default", "organization", "businessType", request.BusinessType, ct: ct);

        // 5. Derive agents, strategies, and integrations for the business type
        var agents = ResolveAgents(request.BusinessType, request.ConnectedSystems);
        var strategies = ResolveStrategies(request.BusinessType, request.AutomationLevel);
        var integrations = request.ConnectedSystems.ToList();

        // 6. Persist agent configuration
        foreach (var agent in agents)
        {
            await _controlPlane.SetConfigurationAsync("default", "agents", agent.ToLowerInvariant().Replace(' ', '-'), "enabled", ct: ct);
        }

        var estimatedMinutes = EstimateSetupTime(request);

        _logger.LogInformation(
            "Onboarding deployment complete — agents={AgentCount}, strategies={StrategyCount}, integrations={IntegrationCount}, eta={Eta}min",
            agents.Count, strategies.Count, integrations.Count, estimatedMinutes);

        return new OnboardingDeployResult(
            Success: true,
            AgentsConfigured: agents,
            StrategiesApplied: strategies,
            IntegrationsActive: integrations,
            EstimatedReadyMinutes: estimatedMinutes);
    }

    private static List<string> ResolveAgents(string businessType, IReadOnlyList<string> systems)
    {
        var agents = new List<string> { "Operations Coordinator", "Insight Analyst" };

        switch (businessType)
        {
            case "saas":
                agents.AddRange(["Sales Pipeline Agent", "Churn Prediction Agent", "Revenue Ops Agent"]);
                break;
            case "ecommerce":
                agents.AddRange(["Inventory Agent", "Marketing Spend Agent", "Customer Lifecycle Agent"]);
                break;
            case "healthcare":
                agents.AddRange(["Compliance Agent", "Scheduling Agent", "Revenue Cycle Agent"]);
                break;
            case "financial-services":
                agents.AddRange(["Risk Assessment Agent", "Compliance Agent", "Portfolio Ops Agent"]);
                break;
            case "manufacturing":
                agents.AddRange(["Supply Chain Agent", "Quality Control Agent", "Logistics Agent"]);
                break;
            case "professional-services":
                agents.AddRange(["Resource Planning Agent", "Billing Agent", "Client Engagement Agent"]);
                break;
        }

        if (systems.Contains("salesforce") || systems.Contains("hubspot"))
            agents.Add("CRM Sync Agent");
        if (systems.Contains("slack"))
            agents.Add("Notification Agent");
        if (systems.Contains("quickbooks"))
            agents.Add("Finance Sync Agent");

        return agents;
    }

    private static List<string> ResolveStrategies(string businessType, string automationLevel)
    {
        var strategies = new List<string> { "balanced" };

        if (automationLevel == "autonomous")
            strategies.Add("throughput-optimized");
        if (automationLevel == "approval")
            strategies.Add("safe-mode");

        switch (businessType)
        {
            case "saas":
            case "ecommerce":
                strategies.Add("growth-focused");
                break;
            case "healthcare":
            case "financial-services":
                strategies.Add("compliance-first");
                break;
            case "manufacturing":
                strategies.Add("cost-optimized");
                break;
        }

        return strategies;
    }

    private static int EstimateSetupTime(OnboardingDeployRequest request)
    {
        var baseMinutes = 15;
        baseMinutes += request.ConnectedSystems.Count * 5;
        baseMinutes += request.Departments.Count * 2;
        return Math.Min(baseMinutes, 55);
    }
}
