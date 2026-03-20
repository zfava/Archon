using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ActionSafety;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

public sealed class ActionSafetyAutoClassificationTests
{
    private readonly ActionSafetyService _service;

    public ActionSafetyAutoClassificationTests()
    {
        _service = new ActionSafetyService(
            Substitute.For<IProofAnalyticsService>(),
            Substitute.For<IEventBus>(),
            NullLogger<ActionSafetyService>.Instance);
    }

    [Theory]
    [InlineData("data.delete", ReversibilityLevel.Irreversible)]
    [InlineData("record.remove", ReversibilityLevel.Irreversible)]
    [InlineData("workflow.terminate", ReversibilityLevel.Irreversible)]
    [InlineData("session.cancel", ReversibilityLevel.Irreversible)]
    public async Task DeleteKeywords_InferIrreversible(string actionType, ReversibilityLevel expected)
    {
        var result = await _service.GetOrInferClassificationAsync(actionType, "tenant-1");
        Assert.Equal(expected, result.Reversibility);
        Assert.False(result.RollbackSupported);
        Assert.Equal("auto-inference", result.ClassifiedBy);
    }

    [Theory]
    [InlineData("alert.send", ReversibilityLevel.Irreversible)]
    [InlineData("user.notify", ReversibilityLevel.Irreversible)]
    [InlineData("report.email", ReversibilityLevel.Irreversible)]
    [InlineData("event.publish", ReversibilityLevel.Irreversible)]
    public async Task SendKeywords_InferIrreversible(string actionType, ReversibilityLevel expected)
    {
        var result = await _service.GetOrInferClassificationAsync(actionType, "tenant-1");
        Assert.Equal(expected, result.Reversibility);
        Assert.Equal("auto-inference", result.ClassifiedBy);
    }

    [Theory]
    [InlineData("profile.update", ReversibilityLevel.Reversible)]
    [InlineData("config.modify", ReversibilityLevel.Reversible)]
    [InlineData("setting.edit", ReversibilityLevel.Reversible)]
    [InlineData("field.change", ReversibilityLevel.Reversible)]
    public async Task UpdateKeywords_InferReversible(string actionType, ReversibilityLevel expected)
    {
        var result = await _service.GetOrInferClassificationAsync(actionType, "tenant-1");
        Assert.Equal(expected, result.Reversibility);
        Assert.True(result.RollbackSupported);
        Assert.Equal(RollbackStrategy.Automatic, result.RollbackStrategy);
        Assert.Equal("auto-inference", result.ClassifiedBy);
    }

    [Theory]
    [InlineData("record.create", ReversibilityLevel.Reversible)]
    [InlineData("agent.add", ReversibilityLevel.Reversible)]
    [InlineData("user.register", ReversibilityLevel.Reversible)]
    public async Task CreateKeywords_InferReversible(string actionType, ReversibilityLevel expected)
    {
        var result = await _service.GetOrInferClassificationAsync(actionType, "tenant-1");
        Assert.Equal(expected, result.Reversibility);
        Assert.True(result.RollbackSupported);
        Assert.Equal("auto-inference", result.ClassifiedBy);
    }

    [Theory]
    [InlineData("request.approve", ReversibilityLevel.Compensatable)]
    [InlineData("submission.deny", ReversibilityLevel.Compensatable)]
    [InlineData("proposal.review", ReversibilityLevel.Compensatable)]
    public async Task ApprovalKeywords_InferCompensatable(string actionType, ReversibilityLevel expected)
    {
        var result = await _service.GetOrInferClassificationAsync(actionType, "tenant-1");
        Assert.Equal(expected, result.Reversibility);
        Assert.Equal(RollbackStrategy.Compensation, result.RollbackStrategy);
        Assert.Equal("auto-inference", result.ClassifiedBy);
    }

    [Fact]
    public async Task UnknownActionType_InferCompensatableDefault()
    {
        var result = await _service.GetOrInferClassificationAsync("xyz.unknown.action", "tenant-1");
        Assert.Equal(ReversibilityLevel.Compensatable, result.Reversibility);
        Assert.Equal("auto-inference", result.ClassifiedBy);
    }

    [Fact]
    public async Task ExplicitClassification_OverridesInference()
    {
        // "data.read" is explicitly seeded as Reversible in ActionSafetyService
        var result = await _service.GetOrInferClassificationAsync("data.read", "tenant-1");
        Assert.Equal(ReversibilityLevel.Reversible, result.Reversibility);
        // Explicitly seeded classifications have "system:seed" as ClassifiedBy
        Assert.Equal("system:seed", result.ClassifiedBy);
    }

    [Fact]
    public async Task ExplicitSetClassification_OverridesInference()
    {
        // Set an explicit classification for a new action type
        await _service.SetClassificationAsync(new ActionSafetyClassification(
            Guid.NewGuid(), "custom.action",
            ReversibilityLevel.Irreversible, false, RollbackStrategy.None,
            null, null, "Manually classified", "operator", DateTimeOffset.UtcNow));

        var result = await _service.GetOrInferClassificationAsync("custom.action", "tenant-1");
        Assert.Equal(ReversibilityLevel.Irreversible, result.Reversibility);
        Assert.Equal("operator", result.ClassifiedBy); // Not "auto-inference"
    }
}
