using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Perception;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Perception;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace ArchonAI.Perception.Tests;

public sealed class PerceptionTests
{
    // ──────────────────────────────────────────────
    //  Shared helpers
    // ──────────────────────────────────────────────

    private static BusinessSignal CreateValidSignal(
        SourceSystem source = SourceSystem.CRM,
        Dictionary<string, string>? payload = null,
        Guid? signalId = null,
        string entityId = "entity-1")
    {
        return new BusinessSignal(
            SignalId: signalId ?? Guid.NewGuid(),
            SignalType: SignalType.ApiCall,
            SourceSystem: source,
            EntityId: entityId,
            Timestamp: DateTimeOffset.UtcNow,
            Payload: payload ?? new Dictionary<string, string> { ["key"] = "value" });
    }

    private static Objective CreateObjective(
        string title = "Test Objective",
        string description = "A valid description",
        Dictionary<string, string>? constraints = null)
    {
        return new Objective(
            Id: Guid.NewGuid(),
            Title: title,
            Description: description,
            Constraints: constraints ?? new Dictionary<string, string> { ["objectiveType"] = "test" },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            DueAtUtc: DateTimeOffset.UtcNow.AddDays(7));
    }

    // ──────────────────────────────────────────────
    //  BusinessPerceptionEngine helpers
    // ──────────────────────────────────────────────

    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<IStrategicPlanner> _strategicPlanner = new();
    private readonly Mock<ILogger<BusinessPerceptionEngine>> _bpeLogger = new();

    private BusinessPerceptionEngine CreateBusinessPerceptionEngine()
    {
        _strategicPlanner
            .Setup(s => s.BuildWorkflowAsync(It.IsAny<Objective>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition(
                Guid.NewGuid(), "auto", "workflow", Array.Empty<WorkflowStepDefinition>(), DateTimeOffset.UtcNow));

        return new BusinessPerceptionEngine(
            _eventBus.Object,
            _strategicPlanner.Object,
            _bpeLogger.Object);
    }

    // ──────────────────────────────────────────────
    //  PerceptionEngine helpers
    // ──────────────────────────────────────────────

    private static PerceptionEngine CreatePerceptionEngine(PerceptionOptions? options = null)
    {
        var opts = options ?? new PerceptionOptions();
        return new PerceptionEngine(Options.Create(opts));
    }

    // ──────────────────────────────────────────────
    //  SignalIngestionService helpers
    // ──────────────────────────────────────────────

    private readonly Mock<IBusinessPerceptionEngine> _mockPerceptionEngine = new();
    private readonly Mock<ILogger<SignalIngestionService>> _sisLogger = new();

    private SignalIngestionService CreateSignalIngestionService(PerceptionOptions? options = null)
    {
        var opts = options ?? new PerceptionOptions();

        _mockPerceptionEngine
            .Setup(p => p.ProcessSignalAsync(It.IsAny<BusinessSignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BusinessSignal s, CancellationToken _) => new OperationalObservation(
                Guid.NewGuid(), s.SignalId, s.SourceSystem,
                ObservationCategory.CustomerActivity, ObservationSeverity.Info,
                s.EntityId, "summary",
                new Dictionary<string, string>(),
                s.Timestamp, DateTimeOffset.UtcNow));

        return new SignalIngestionService(
            _mockPerceptionEngine.Object,
            Options.Create(opts),
            _sisLogger.Object);
    }

    // ══════════════════════════════════════════════
    //  BusinessPerceptionEngine Tests
    // ══════════════════════════════════════════════

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_ValidCrmSignal_ReturnsObservationWithCustomerActivityCategory()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(source: SourceSystem.CRM);

        var observation = await engine.ProcessSignalAsync(signal);

        observation.Should().NotBeNull();
        observation.SourceSignalId.Should().Be(signal.SignalId);
        observation.SourceSystem.Should().Be(SourceSystem.CRM);
        observation.Category.Should().Be(ObservationCategory.CustomerActivity);
        observation.EntityId.Should().Be(signal.EntityId);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_PayloadWithEventTypeOrder_ReturnsOrderLifecycleCategory()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            payload: new Dictionary<string, string> { ["eventType"] = "orderPlaced" });

        var observation = await engine.ProcessSignalAsync(signal);

        observation.Category.Should().Be(ObservationCategory.OrderLifecycle);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_PayloadWithPaymentEventType_ReturnsRevenueCategory()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            payload: new Dictionary<string, string> { ["eventType"] = "paymentReceived" });

        var observation = await engine.ProcessSignalAsync(signal);

        observation.Category.Should().Be(ObservationCategory.Revenue);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_PayloadWithSeverityCritical_ReturnsCriticalSeverity()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            payload: new Dictionary<string, string> { ["severity"] = "critical" });

        var observation = await engine.ProcessSignalAsync(signal);

        observation.Severity.Should().Be(ObservationSeverity.Critical);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_HighSeverity_NotifiesStrategicPlanner()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            payload: new Dictionary<string, string> { ["severity"] = "high" });

        await engine.ProcessSignalAsync(signal);

        _strategicPlanner.Verify(
            s => s.BuildWorkflowAsync(It.IsAny<Objective>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_InfoSeverity_DoesNotNotifyStrategicPlanner()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            payload: new Dictionary<string, string> { ["key"] = "value" });

        await engine.ProcessSignalAsync(signal);

        _strategicPlanner.Verify(
            s => s.BuildWorkflowAsync(It.IsAny<Objective>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_HighAmountPayload_ReturnsHighSeverity()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            payload: new Dictionary<string, string> { ["amount"] = "200000" });

        var observation = await engine.ProcessSignalAsync(signal);

        observation.Severity.Should().Be(ObservationSeverity.High);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_ErrorInPayload_ReturnsHighSeverity()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            payload: new Dictionary<string, string> { ["error"] = "something failed" });

        var observation = await engine.ProcessSignalAsync(signal);

        observation.Severity.Should().Be(ObservationSeverity.High);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_PublishesEventToEventBus()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal();

        await engine.ProcessSignalAsync(signal);

        _eventBus.Verify(
            e => e.PublishAsync(It.IsAny<SystemEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessBatchAsync_MultipleSignals_ReturnsMatchingCount()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signals = new List<BusinessSignal>
        {
            CreateValidSignal(entityId: "e1"),
            CreateValidSignal(entityId: "e2"),
            CreateValidSignal(entityId: "e3")
        };

        var results = await engine.ProcessBatchAsync(signals);

        results.Should().HaveCount(3);
        results[0].EntityId.Should().Be("e1");
        results[1].EntityId.Should().Be("e2");
        results[2].EntityId.Should().Be("e3");
    }

    [Fact]
    public async Task BusinessPerceptionEngine_GetDashboardAsync_AfterProcessingSignals_ReflectsCorrectCounts()
    {
        var engine = CreateBusinessPerceptionEngine();
        await engine.ProcessSignalAsync(CreateValidSignal(source: SourceSystem.CRM));
        await engine.ProcessSignalAsync(CreateValidSignal(source: SourceSystem.ERP,
            payload: new Dictionary<string, string> { ["key"] = "val" }));

        var dashboard = await engine.GetDashboardAsync();

        dashboard.TotalSignalsIngested.Should().Be(2);
        dashboard.TotalObservationsProduced.Should().Be(2);
        dashboard.SignalsBySource.Should().ContainKey(SourceSystem.CRM).WhoseValue.Should().Be(1);
        dashboard.SignalsBySource.Should().ContainKey(SourceSystem.ERP).WhoseValue.Should().Be(1);
        dashboard.RecentObservations.Should().HaveCount(2);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_StructuredDataContainsPayloadKeys()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            payload: new Dictionary<string, string>
            {
                ["eventType"] = "orderPlaced",
                ["customField"] = "customValue"
            });

        var observation = await engine.ProcessSignalAsync(signal);

        observation.StructuredData.Should().ContainKey("payload.eventType");
        observation.StructuredData["payload.eventType"].Should().Be("orderPlaced");
        observation.StructuredData.Should().ContainKey("payload.customField");
        observation.StructuredData["payload.customField"].Should().Be("customValue");
        observation.StructuredData.Should().ContainKey("entityId");
        observation.StructuredData["entityId"].Should().Be(signal.EntityId);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_SummaryContainsSourceAndEntity()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            source: SourceSystem.Finance,
            entityId: "invoice-42",
            payload: new Dictionary<string, string> { ["eventType"] = "invoiceCreated" });

        var observation = await engine.ProcessSignalAsync(signal);

        observation.Summary.Should().Contain("Finance");
        observation.Summary.Should().Contain("invoice-42");
        observation.Summary.Should().Contain("invoiceCreated");
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_CampaignEventType_ReturnsCampaignPerformanceCategory()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            payload: new Dictionary<string, string> { ["eventType"] = "campaignStarted" });

        var observation = await engine.ProcessSignalAsync(signal);

        observation.Category.Should().Be(ObservationCategory.CampaignPerformance);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_ShipmentEventType_ReturnsSupplyChainCategory()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            payload: new Dictionary<string, string> { ["eventType"] = "shipmentDispatched" });

        var observation = await engine.ProcessSignalAsync(signal);

        observation.Category.Should().Be(ObservationCategory.SupplyChain);
    }

    [Fact]
    public async Task BusinessPerceptionEngine_ProcessSignalAsync_MediumAmountPayload_ReturnsMediumSeverity()
    {
        var engine = CreateBusinessPerceptionEngine();
        var signal = CreateValidSignal(
            payload: new Dictionary<string, string> { ["amount"] = "50000" });

        var observation = await engine.ProcessSignalAsync(signal);

        observation.Severity.Should().Be(ObservationSeverity.Medium);
    }

    // ══════════════════════════════════════════════
    //  PerceptionEngine Tests
    // ══════════════════════════════════════════════

    [Fact]
    public async Task PerceptionEngine_ProcessObjectiveAsync_ValidObjective_ReturnsIsValidTrue()
    {
        var engine = CreatePerceptionEngine();
        var objective = CreateObjective();

        var result = await engine.ProcessObjectiveAsync(objective);

        result.IsValid.Should().BeTrue();
        result.ValidationErrors.Should().BeEmpty();
        result.NormalizedObjective.Title.Should().Be("Test Objective");
    }

    [Fact]
    public async Task PerceptionEngine_ProcessObjectiveAsync_EmptyTitle_ReturnsValidationError()
    {
        var engine = CreatePerceptionEngine();
        var objective = CreateObjective(title: "   ");

        var result = await engine.ProcessObjectiveAsync(objective);

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.Contains("title is required"));
    }

    [Fact]
    public async Task PerceptionEngine_ProcessObjectiveAsync_TitleExceedsMaxLength_ReturnsValidationError()
    {
        var options = new PerceptionOptions { MaxTitleLength = 10 };
        var engine = CreatePerceptionEngine(options);
        var objective = CreateObjective(title: "This title is way too long for the limit");

        var result = await engine.ProcessObjectiveAsync(objective);

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.Contains("exceeds max length"));
    }

    [Fact]
    public async Task PerceptionEngine_ProcessObjectiveAsync_MissingRequiredConstraint_ReturnsValidationError()
    {
        var engine = CreatePerceptionEngine();
        var objective = CreateObjective(
            constraints: new Dictionary<string, string> { ["unrelated"] = "value" });

        var result = await engine.ProcessObjectiveAsync(objective);

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.Contains("objectiveType") && e.Contains("missing"));
    }

    [Fact]
    public async Task PerceptionEngine_ProcessObjectiveAsync_NoiseTokensInDescription_AreRemoved()
    {
        var engine = CreatePerceptionEngine();
        var objective = CreateObjective(
            description: "We basically need to um improve throughput like now");

        var result = await engine.ProcessObjectiveAsync(objective);

        result.NormalizedObjective.Description.Should().NotContain("basically");
        result.NormalizedObjective.Description.Should().NotContain(" um ");
        result.RemovedNoiseTokens.Should().Contain("basically");
        result.RemovedNoiseTokens.Should().Contain("um");
    }

    [Fact]
    public async Task PerceptionEngine_ProcessObjectiveAsync_ComplianceInDescription_ExtractsComplianceSignal()
    {
        var engine = CreatePerceptionEngine();
        var objective = CreateObjective(
            description: "Ensure compliance with new regulations");

        var result = await engine.ProcessObjectiveAsync(objective);

        result.ExtractedContext.Should().ContainKey("complianceSignal");
        result.ExtractedContext["complianceSignal"].Should().Be("true");
    }

    [Fact]
    public async Task PerceptionEngine_ProcessObjectiveAsync_LatencyInDescription_ExtractsPerformanceSignal()
    {
        var engine = CreatePerceptionEngine();
        var objective = CreateObjective(
            description: "Reduce latency across all endpoints");

        var result = await engine.ProcessObjectiveAsync(objective);

        result.ExtractedContext.Should().ContainKey("performanceSignal");
        result.ExtractedContext["performanceSignal"].Should().Be("true");
    }

    [Fact]
    public async Task PerceptionEngine_ProcessObjectiveAsync_NormalizesWhitespace()
    {
        var engine = CreatePerceptionEngine();
        var objective = CreateObjective(
            title: "  Multiple   spaces   here  ",
            description: "  Also   many    spaces   ");

        var result = await engine.ProcessObjectiveAsync(objective);

        result.NormalizedObjective.Title.Should().Be("Multiple spaces here");
        result.NormalizedObjective.Description.Should().Be("Also many spaces");
    }

    [Fact]
    public async Task PerceptionEngine_ProcessObjectiveAsync_ConstraintKeysAreNormalizedAndLowercased()
    {
        var engine = CreatePerceptionEngine();
        var objective = CreateObjective(
            constraints: new Dictionary<string, string>
            {
                ["Objective Type"] = "test",
                [" Priority "] = "high"
            });

        var result = await engine.ProcessObjectiveAsync(objective);

        result.NormalizedObjective.Constraints.Should().ContainKey("objectivetype");
        result.NormalizedObjective.Constraints.Should().ContainKey("priority");
    }

    [Fact]
    public async Task PerceptionEngine_ProcessObjectiveAsync_ExtractedContextDefaultValues()
    {
        var engine = CreatePerceptionEngine();
        var objective = CreateObjective(
            description: "Something neutral",
            constraints: new Dictionary<string, string> { ["objectiveType"] = "test" });

        var result = await engine.ProcessObjectiveAsync(objective);

        result.ExtractedContext.Should().ContainKey("objectiveType");
        result.ExtractedContext["objectiveType"].Should().Be("test");
        result.ExtractedContext.Should().ContainKey("source");
        result.ExtractedContext["source"].Should().Be("external");
        result.ExtractedContext.Should().ContainKey("priority");
        result.ExtractedContext["priority"].Should().Be("balanced");
    }

    // ══════════════════════════════════════════════
    //  SignalIngestionService Tests
    // ══════════════════════════════════════════════

    [Fact]
    public async Task SignalIngestionService_IngestApiSignalAsync_ValidSignal_ReturnsAccepted()
    {
        var svc = CreateSignalIngestionService();
        var signal = CreateValidSignal();

        var result = await svc.IngestApiSignalAsync(signal);

        result.Accepted.Should().BeTrue();
        result.RejectionReason.Should().BeNull();
        result.SignalId.Should().Be(signal.SignalId);
    }

    [Fact]
    public async Task SignalIngestionService_IngestApiSignalAsync_EmptySignalId_ReturnsRejected()
    {
        var svc = CreateSignalIngestionService();
        var signal = CreateValidSignal(signalId: Guid.Empty);

        var result = await svc.IngestApiSignalAsync(signal);

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Contain("SignalId is required");
    }

    [Fact]
    public async Task SignalIngestionService_IngestApiSignalAsync_EmptyEntityId_ReturnsRejected()
    {
        var svc = CreateSignalIngestionService();
        var signal = CreateValidSignal(entityId: "  ");

        var result = await svc.IngestApiSignalAsync(signal);

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Contain("EntityId is required");
    }

    [Fact]
    public async Task SignalIngestionService_IngestApiSignalAsync_EmptyPayload_ReturnsRejected()
    {
        var svc = CreateSignalIngestionService();
        var signal = new BusinessSignal(
            Guid.NewGuid(), SignalType.ApiCall, SourceSystem.CRM, "entity-1",
            DateTimeOffset.UtcNow, new Dictionary<string, string>());

        var result = await svc.IngestApiSignalAsync(signal);

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Contain("Payload must contain at least one entry");
    }

    [Fact]
    public async Task SignalIngestionService_IngestApiSignalAsync_DisabledSource_ReturnsRejected()
    {
        var options = new PerceptionOptions { EnabledSources = new HashSet<SourceSystem> { SourceSystem.CRM } };
        var svc = CreateSignalIngestionService(options);
        var signal = CreateValidSignal(source: SourceSystem.Finance);

        var result = await svc.IngestApiSignalAsync(signal);

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Contain("not enabled");
    }

    [Fact]
    public async Task SignalIngestionService_IngestEventBusSignalAsync_ValidSignal_ReturnsAccepted()
    {
        var svc = CreateSignalIngestionService();
        var signal = CreateValidSignal();

        var result = await svc.IngestEventBusSignalAsync(signal);

        result.Accepted.Should().BeTrue();
        result.RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task SignalIngestionService_IngestEventBusSignalAsync_InvalidSignal_ReturnsRejected()
    {
        var svc = CreateSignalIngestionService();
        var signal = CreateValidSignal(signalId: Guid.Empty);

        var result = await svc.IngestEventBusSignalAsync(signal);

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SignalIngestionService_IngestScheduledPollSignalsAsync_MixedSignals_ReturnsPerSignalResults()
    {
        var svc = CreateSignalIngestionService();
        var validSignal = CreateValidSignal(entityId: "good-entity");
        var invalidSignal = CreateValidSignal(signalId: Guid.Empty, entityId: "bad-entity");

        var results = await svc.IngestScheduledPollSignalsAsync(
            SourceSystem.CRM,
            new List<BusinessSignal> { validSignal, invalidSignal });

        results.Should().HaveCount(2);
        results[0].Accepted.Should().BeTrue();
        results[1].Accepted.Should().BeFalse();
    }

    [Fact]
    public async Task SignalIngestionService_IngestApiSignalAsync_ProcessingException_ReturnsFailedResult()
    {
        var svc = CreateSignalIngestionService();

        _mockPerceptionEngine
            .Setup(p => p.ProcessSignalAsync(It.IsAny<BusinessSignal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var signal = CreateValidSignal();
        var result = await svc.IngestApiSignalAsync(signal);

        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Contain("Processing failed");
        result.RejectionReason.Should().Contain("boom");
    }

    [Fact]
    public async Task SignalIngestionService_StartPollingAsync_ThenStopPollingAsync_CompletesSuccessfully()
    {
        var svc = CreateSignalIngestionService();

        var startTask = svc.StartPollingAsync();
        await startTask;
        startTask.IsCompletedSuccessfully.Should().BeTrue();

        var stopTask = svc.StopPollingAsync();
        await stopTask;
        stopTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task SignalIngestionService_StartPollingAsync_CalledTwice_DoesNotThrow()
    {
        var svc = CreateSignalIngestionService();

        await svc.StartPollingAsync();
        var act = () => svc.StartPollingAsync();

        await act.Should().NotThrowAsync();
    }

    // ══════════════════════════════════════════════
    //  PerceptionOptions Tests
    // ══════════════════════════════════════════════

    [Fact]
    public void PerceptionOptions_Defaults_HaveExpectedValues()
    {
        var options = new PerceptionOptions();

        options.MaxTitleLength.Should().Be(200);
        options.MaxDescriptionLength.Should().Be(4000);
        options.PollIntervalSeconds.Should().Be(60);
        options.AutoTriggerStrategicPlanner.Should().BeTrue();
        options.RequiredConstraintKeys.Should().Contain("objectiveType");
        options.NoiseTokens.Should().HaveCountGreaterThanOrEqualTo(3);
        options.EnabledSources.Should().Contain(SourceSystem.CRM);
        options.EnabledSources.Should().Contain(SourceSystem.ERP);
        options.EnabledSources.Should().Contain(SourceSystem.Finance);
        options.EnabledSources.Should().Contain(SourceSystem.Marketing);
        options.EnabledSources.Should().Contain(SourceSystem.Logistics);
    }
}
