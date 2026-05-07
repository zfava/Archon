using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Scenario;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

public sealed class ScenarioEngineTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<ScenarioService> _logger = Substitute.For<ILogger<ScenarioService>>();

    private ScenarioService CreateService() => new(_eventBus, _logger);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();

    private Scenario MakeScenario(
        ScenarioType type = ScenarioType.WhatIf,
        string title = "Test Scenario",
        ScenarioStatus status = ScenarioStatus.Draft,
        Guid? tenantId = null,
        IReadOnlyList<ScenarioAssumption>? assumptions = null,
        IReadOnlyList<ProjectedEffect>? effects = null,
        IReadOnlyList<ScenarioLink>? linkedKpis = null,
        IReadOnlyList<ScenarioLink>? linkedDecisions = null) =>
        new(
            Id: Guid.NewGuid(),
            TenantId: tenantId ?? _tenantId,
            Title: title,
            Description: $"{title} description",
            Type: type,
            Status: status,
            Assumptions: assumptions ?? Array.Empty<ScenarioAssumption>(),
            ProjectedEffects: effects ?? Array.Empty<ProjectedEffect>(),
            LinkedKpis: linkedKpis ?? Array.Empty<ScenarioLink>(),
            LinkedDecisions: linkedDecisions ?? Array.Empty<ScenarioLink>(),
            LinkedEntities: Array.Empty<ScenarioLink>(),
            CreatedBy: "test",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    // ── Create & Persistence ───────────────────────────────────

    [Fact]
    public async Task CreateScenario_StoresAndReturns()
    {
        var svc = CreateService();
        var scenario = MakeScenario();

        var result = await svc.CreateScenarioAsync(scenario);

        Assert.Equal(scenario.Id, result.Id);
        Assert.Equal(scenario.Title, result.Title);
    }

    [Fact]
    public async Task CreateScenario_PublishesEvent()
    {
        var svc = CreateService();
        await svc.CreateScenarioAsync(MakeScenario());

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "scenario.created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetScenario_ReturnsStoredScenario()
    {
        var svc = CreateService();
        var scenario = MakeScenario();
        await svc.CreateScenarioAsync(scenario);

        var result = await svc.GetScenarioAsync(scenario.Id, _tenantId);

        Assert.NotNull(result);
        Assert.Equal(scenario.Title, result!.Title);
    }

    [Fact]
    public async Task GetScenario_NonExistent_ReturnsNull()
    {
        var svc = CreateService();

        var result = await svc.GetScenarioAsync(Guid.NewGuid(), _tenantId);

        Assert.Null(result);
    }

    // ── Tenant Isolation ───────────────────────────────────────

    [Fact]
    public async Task GetScenario_WrongTenant_ReturnsNull()
    {
        var svc = CreateService();
        var scenario = MakeScenario();
        await svc.CreateScenarioAsync(scenario);

        var result = await svc.GetScenarioAsync(scenario.Id, _otherTenantId);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListScenarios_FiltersByTenant()
    {
        var svc = CreateService();
        await svc.CreateScenarioAsync(MakeScenario(title: "Mine"));
        await svc.CreateScenarioAsync(MakeScenario(title: "Other", tenantId: _otherTenantId));

        var results = await svc.ListScenariosAsync(_tenantId);

        Assert.Single(results);
        Assert.Equal("Mine", results[0].Title);
    }

    [Fact]
    public async Task DeleteScenario_WrongTenant_ReturnsFalse()
    {
        var svc = CreateService();
        var scenario = MakeScenario();
        await svc.CreateScenarioAsync(scenario);

        var deleted = await svc.DeleteScenarioAsync(scenario.Id, _otherTenantId);

        Assert.False(deleted);
        Assert.NotNull(await svc.GetScenarioAsync(scenario.Id, _tenantId));
    }

    // ── Delete ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteScenario_RemovesScenario()
    {
        var svc = CreateService();
        var scenario = MakeScenario();
        await svc.CreateScenarioAsync(scenario);

        var deleted = await svc.DeleteScenarioAsync(scenario.Id, _tenantId);

        Assert.True(deleted);
        Assert.Null(await svc.GetScenarioAsync(scenario.Id, _tenantId));
    }

    // ── List with Filters ──────────────────────────────────────

    [Fact]
    public async Task ListScenarios_FiltersByType()
    {
        var svc = CreateService();
        await svc.CreateScenarioAsync(MakeScenario(type: ScenarioType.WhatIf, title: "A"));
        await svc.CreateScenarioAsync(MakeScenario(type: ScenarioType.CostReduction, title: "B"));

        var results = await svc.ListScenariosAsync(_tenantId, type: ScenarioType.CostReduction);

        Assert.Single(results);
        Assert.Equal("B", results[0].Title);
    }

    [Fact]
    public async Task ListScenarios_FiltersByStatus()
    {
        var svc = CreateService();
        await svc.CreateScenarioAsync(MakeScenario(status: ScenarioStatus.Draft, title: "Draft"));
        await svc.CreateScenarioAsync(MakeScenario(status: ScenarioStatus.Active, title: "Active"));

        var results = await svc.ListScenariosAsync(_tenantId, status: ScenarioStatus.Active);

        Assert.Single(results);
        Assert.Equal("Active", results[0].Title);
    }

    // ── Update Assumptions ─────────────────────────────────────

    [Fact]
    public async Task UpdateAssumptions_UpdatesAndDerivesEffects()
    {
        var svc = CreateService();
        var scenario = MakeScenario();
        await svc.CreateScenarioAsync(scenario);

        var newAssumptions = new List<ScenarioAssumption>
        {
            new("Headcount", "50", "65", "people", "Hiring plan"),
            new("Budget", "500000", "650000", "USD", "Increased allocation"),
        };

        var updated = await svc.UpdateAssumptionsAsync(scenario.Id, _tenantId, newAssumptions);

        Assert.NotNull(updated);
        Assert.Equal(2, updated!.Assumptions.Count);
        Assert.Equal(2, updated.ProjectedEffects.Count);
    }

    [Fact]
    public async Task UpdateAssumptions_WrongTenant_ReturnsNull()
    {
        var svc = CreateService();
        var scenario = MakeScenario();
        await svc.CreateScenarioAsync(scenario);

        var result = await svc.UpdateAssumptionsAsync(
            scenario.Id, _otherTenantId,
            new List<ScenarioAssumption> { new("X", "1", "2", null, null) });

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAssumptions_NonExistent_ReturnsNull()
    {
        var svc = CreateService();

        var result = await svc.UpdateAssumptionsAsync(
            Guid.NewGuid(), _tenantId,
            new List<ScenarioAssumption> { new("X", "1", "2", null, null) });

        Assert.Null(result);
    }

    // ── Comparison ─────────────────────────────────────────────

    [Fact]
    public async Task CompareScenarios_BuildsAxesFromProjectedEffects()
    {
        var svc = CreateService();

        var s1 = MakeScenario(title: "Scenario A", effects: new List<ProjectedEffect>
        {
            new("Revenue", "Revenue", 100, 120, "USD", "Increase", "High"),
            new("Cost", "Cost", 50, 55, "USD", "Increase", "Medium"),
        });
        var s2 = MakeScenario(title: "Scenario B", effects: new List<ProjectedEffect>
        {
            new("Revenue", "Revenue", 100, 140, "USD", "Increase", "Medium"),
            new("Headcount", "Headcount", 10, 15, "people", "Increase", "High"),
        });

        await svc.CreateScenarioAsync(s1);
        await svc.CreateScenarioAsync(s2);

        var comparison = await svc.CompareScenariosAsync(
            new List<Guid> { s1.Id, s2.Id }, _tenantId);

        Assert.Equal(2, comparison.ScenarioIds.Count);
        // Union of metrics: Revenue, Cost, Headcount = 3 axes
        Assert.Equal(3, comparison.Axes.Count);
    }

    [Fact]
    public async Task CompareScenarios_PopulatesValuesPerScenario()
    {
        var svc = CreateService();

        var s1 = MakeScenario(title: "A", effects: new List<ProjectedEffect>
        {
            new("Rev", "Revenue", 100, 120, "USD", "Increase", "High"),
        });
        var s2 = MakeScenario(title: "B", effects: new List<ProjectedEffect>
        {
            new("Rev", "Revenue", 100, 150, "USD", "Increase", "Medium"),
        });

        await svc.CreateScenarioAsync(s1);
        await svc.CreateScenarioAsync(s2);

        var comparison = await svc.CompareScenariosAsync(
            new List<Guid> { s1.Id, s2.Id }, _tenantId);

        var revenueAxis = comparison.Axes.First(a => a.Metric == "Revenue");
        Assert.Equal(120.0, revenueAxis.ValuesByScenarioId[s1.Id]);
        Assert.Equal(150.0, revenueAxis.ValuesByScenarioId[s2.Id]);
    }

    [Fact]
    public async Task CompareScenarios_MarksComparedStatus()
    {
        var svc = CreateService();
        var s1 = MakeScenario(title: "A");
        var s2 = MakeScenario(title: "B");
        await svc.CreateScenarioAsync(s1);
        await svc.CreateScenarioAsync(s2);

        await svc.CompareScenariosAsync(new List<Guid> { s1.Id, s2.Id }, _tenantId);

        var fetched1 = await svc.GetScenarioAsync(s1.Id, _tenantId);
        var fetched2 = await svc.GetScenarioAsync(s2.Id, _tenantId);
        Assert.Equal(ScenarioStatus.Compared, fetched1!.Status);
        Assert.Equal(ScenarioStatus.Compared, fetched2!.Status);
    }

    [Fact]
    public async Task CompareScenarios_IgnoresOtherTenantScenarios()
    {
        var svc = CreateService();
        var s1 = MakeScenario(title: "Mine");
        var s2 = MakeScenario(title: "Other", tenantId: _otherTenantId);
        await svc.CreateScenarioAsync(s1);
        await svc.CreateScenarioAsync(s2);

        var comparison = await svc.CompareScenariosAsync(
            new List<Guid> { s1.Id, s2.Id }, _tenantId);

        // Only s1 is visible to _tenantId — s2 filtered out
        Assert.Empty(comparison.Axes);
    }

    // ── Linkage ────────────────────────────────────────────────

    [Fact]
    public async Task Scenario_PreservesLinkedKpis()
    {
        var svc = CreateService();
        var links = new List<ScenarioLink>
        {
            new("Kpi", Guid.NewGuid().ToString(), "throughput"),
            new("Kpi", Guid.NewGuid().ToString(), "latency"),
        };
        var scenario = MakeScenario(linkedKpis: links);
        await svc.CreateScenarioAsync(scenario);

        var result = await svc.GetScenarioAsync(scenario.Id, _tenantId);

        Assert.Equal(2, result!.LinkedKpis.Count);
    }

    [Fact]
    public async Task Scenario_PreservesLinkedDecisions()
    {
        var svc = CreateService();
        var links = new List<ScenarioLink>
        {
            new("Decision", Guid.NewGuid().ToString(), "pricing change"),
        };
        var scenario = MakeScenario(linkedDecisions: links);
        await svc.CreateScenarioAsync(scenario);

        var result = await svc.GetScenarioAsync(scenario.Id, _tenantId);

        Assert.Single(result!.LinkedDecisions);
        Assert.Equal("Decision", result.LinkedDecisions[0].ArtifactType);
    }

    // ── DeriveProjectedEffects ─────────────────────────────────

    [Fact]
    public void DeriveEffects_NumericAssumption_ProducesDirectionAndValues()
    {
        var scenario = MakeScenario();
        var assumptions = new List<ScenarioAssumption>
        {
            new("Revenue", "1000", "1200", "USD", null),
        };

        var effects = ScenarioService.DeriveProjectedEffects(scenario, assumptions);

        Assert.Single(effects);
        Assert.Equal("Increase", effects[0].Direction);
        Assert.Equal(1000.0, effects[0].BaselineValue);
        Assert.Equal(1200.0, effects[0].ProjectedValue);
    }

    [Fact]
    public void DeriveEffects_NonNumericAssumption_ProducesQualitativeEffect()
    {
        var scenario = MakeScenario();
        var assumptions = new List<ScenarioAssumption>
        {
            new("Strategy", "Conservative", "Aggressive", null, null),
        };

        var effects = ScenarioService.DeriveProjectedEffects(scenario, assumptions);

        Assert.Single(effects);
        Assert.Equal("Changed", effects[0].Direction);
        Assert.Null(effects[0].BaselineValue);
        Assert.Equal("Low", effects[0].Confidence);
    }

    [Fact]
    public void DeriveEffects_SmallDelta_HighConfidence()
    {
        var scenario = MakeScenario();
        var assumptions = new List<ScenarioAssumption>
        {
            new("Budget", "1000", "1050", "USD", null), // 5% change < 10% threshold
        };

        var effects = ScenarioService.DeriveProjectedEffects(scenario, assumptions);

        Assert.Equal("High", effects[0].Confidence);
    }

    [Fact]
    public void DeriveEffects_LargeDelta_MediumConfidence()
    {
        var scenario = MakeScenario();
        var assumptions = new List<ScenarioAssumption>
        {
            new("Budget", "1000", "1500", "USD", null), // 50% change > 10% threshold
        };

        var effects = ScenarioService.DeriveProjectedEffects(scenario, assumptions);

        Assert.Equal("Medium", effects[0].Confidence);
    }

    [Fact]
    public void DeriveEffects_DecreaseDirection()
    {
        var scenario = MakeScenario();
        var assumptions = new List<ScenarioAssumption>
        {
            new("Headcount", "100", "80", "people", null),
        };

        var effects = ScenarioService.DeriveProjectedEffects(scenario, assumptions);

        Assert.Equal("Decrease", effects[0].Direction);
    }
}
