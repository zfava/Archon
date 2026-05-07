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
            WorkflowsCreated: [],
            EstimatedReadyMinutes: estimatedMinutes);
    }

    public async Task<OnboardingDeployResult> DeployTemplateAsync(OnboardingTemplateDeployRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Template deployment started — template={Template}, agents={AgentCount}, workflows={WorkflowCount}",
            request.TemplateId, request.Agents.Count, request.Workflows.Count);

        // 1. Store template identity
        await _controlPlane.SetConfigurationAsync("default", "onboarding", "templateId", request.TemplateId, ct: ct);

        // 2. Configure automation level
        await _controlPlane.SetConfigurationAsync("default", "execution", "mode", request.AutomationLevel, ct: ct);

        // 3. Configure departments
        foreach (var dept in request.Departments)
        {
            await _controlPlane.SetConfigurationAsync(
                "default",
                $"department.{dept.Name.ToLowerInvariant()}",
                "automationLevel",
                dept.Level,
                ct: ct);
        }

        // 4. Register connected systems
        foreach (var system in request.ConnectedSystems)
        {
            await _controlPlane.SetConfigurationAsync("default", "integrations", system, "connected", ct: ct);
        }

        // 5. Store business type
        await _controlPlane.SetConfigurationAsync("default", "organization", "businessType", request.BusinessType, ct: ct);

        // 6. Register all template agents
        foreach (var agent in request.Agents)
        {
            await _controlPlane.SetConfigurationAsync("default", "agents", agent.ToLowerInvariant().Replace(' ', '-'), "enabled", ct: ct);
        }

        // 7. Register all template workflows
        var workflowNames = new List<string>();
        foreach (var workflow in request.Workflows)
        {
            var stepList = string.Join("|", workflow.Steps);
            await _controlPlane.SetConfigurationAsync(
                "default",
                "workflows",
                workflow.Name.ToLowerInvariant().Replace(' ', '-'),
                stepList,
                ct: ct);
            workflowNames.Add(workflow.Name);
        }

        // 8. Register strategies
        foreach (var strategy in request.Strategies)
        {
            await _controlPlane.SetConfigurationAsync("default", "strategies", strategy, "active", ct: ct);
        }

        var estimatedMinutes = Math.Min(10 + request.Agents.Count * 2 + request.Workflows.Count * 3, 45);

        _logger.LogInformation(
            "Template deployment complete — template={Template}, agents={AgentCount}, workflows={WorkflowCount}, strategies={StrategyCount}, eta={Eta}min",
            request.TemplateId, request.Agents.Count, workflowNames.Count, request.Strategies.Count, estimatedMinutes);

        return new OnboardingDeployResult(
            Success: true,
            AgentsConfigured: request.Agents.ToList(),
            StrategiesApplied: request.Strategies.ToList(),
            IntegrationsActive: request.ConnectedSystems.ToList(),
            WorkflowsCreated: workflowNames,
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
