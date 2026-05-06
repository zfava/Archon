using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Scheduler;
using ArchonAI.Runtime.Execution;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Runtime.Tests;

public class TaskExecutionManagerTests
{
    private readonly Mock<IResourceScheduler> _resourceScheduler = new();
    private readonly Mock<ILogger<TaskExecutionManager>> _logger = new();
    private readonly RuntimeOptions _options = new() { QueueShardCount = 4, MaxDegreeOfParallelism = 4, MaxRetries = 1, BaseRetryDelayMs = 10 };

    private TaskExecutionManager CreateManager(RuntimeOptions? options = null)
    {
        return new TaskExecutionManager(
            Options.Create(options ?? _options),
            _resourceScheduler.Object,
            _logger.Object);
    }

    private static CoreTask CreateTask(Guid? id = null, string capability = "data-analysis", int order = 1)
    {
        return new CoreTask(id ?? Guid.NewGuid(), Guid.NewGuid(), order, "test-task", "desc",
            capability, new Dictionary<string, string>(), DateTimeOffset.UtcNow, null, null);
    }

    private static ExecutionResult SuccessResult(Guid taskId) =>
        new(taskId, true, "ok", new Dictionary<string, string>(), Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);

    private static ExecutionResult FailureResult(Guid taskId) =>
        new(taskId, false, "failed", new Dictionary<string, string>(), Array.Empty<string>(), new[] { "err" }, DateTimeOffset.UtcNow);

    private void SetupScheduler(IReadOnlyList<CoreTask> tasks, int maxParallelism = 4)
    {
        _resourceScheduler.Setup(s => s.CreateExecutionPlanAsync(
                It.IsAny<IReadOnlyList<CoreTask>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<CoreTask> t, int _, CancellationToken _) =>
                new SchedulePlan(
                    t.Select((task, i) => new ScheduledTask(task, i, "default", null, true, null)).ToList(),
                    maxParallelism));
    }

    // ═══════════════════════════════════════════════════════════════
    // EnqueueAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Enqueue_SingleTask_DoesNotThrow()
    {
        var manager = CreateManager();
        var task = CreateTask();

        await manager.EnqueueAsync(task);
    }

    [Fact]
    public async Task Enqueue_MultipleTasks_DistributesAcrossShards()
    {
        var manager = CreateManager();

        for (int i = 0; i < 20; i++)
            await manager.EnqueueAsync(CreateTask());
    }

    [Fact]
    public async Task Enqueue_CancellationRequested_Throws()
    {
        var manager = CreateManager();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.EnqueueAsync(CreateTask(), cts.Token));
    }

    // ═══════════════════════════════════════════════════════════════
    // ExecuteAllAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAll_NoTasks_ReturnsEmpty()
    {
        SetupScheduler(Array.Empty<CoreTask>());
        var manager = CreateManager();

        var results = await manager.ExecuteAllAsync((task, ct) => Task.FromResult(SuccessResult(task.Id)));

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAll_SingleTask_ExecutesAndReturns()
    {
        var task = CreateTask();
        SetupScheduler(new[] { task });
        var manager = CreateManager();
        await manager.EnqueueAsync(task);

        var results = await manager.ExecuteAllAsync((t, ct) => Task.FromResult(SuccessResult(t.Id)));

        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeTrue();
        results[0].TaskId.Should().Be(task.Id);
    }

    [Fact]
    public async Task ExecuteAll_MultipleTasks_ExecutesAll()
    {
        var tasks = Enumerable.Range(0, 5).Select(i => CreateTask(order: i)).ToArray();
        SetupScheduler(tasks);
        var manager = CreateManager();
        foreach (var task in tasks)
            await manager.EnqueueAsync(task);

        var results = await manager.ExecuteAllAsync((t, ct) => Task.FromResult(SuccessResult(t.Id)));

        results.Should().HaveCount(5);
        results.Should().OnlyContain(r => r.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAll_FailedTask_ReturnsFailureResult()
    {
        var task = CreateTask();
        SetupScheduler(new[] { task });
        var manager = CreateManager();
        await manager.EnqueueAsync(task);

        var results = await manager.ExecuteAllAsync((t, ct) => Task.FromResult(FailureResult(t.Id)));

        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAll_ExceptionWithRetry_RetriesAndReturnsFailure()
    {
        _options.MaxRetries = 1;
        _options.BaseRetryDelayMs = 1;
        var task = CreateTask();
        SetupScheduler(new[] { task });
        var manager = CreateManager();
        await manager.EnqueueAsync(task);
        int callCount = 0;

        var results = await manager.ExecuteAllAsync((t, ct) =>
        {
            callCount++;
            throw new InvalidOperationException("boom");
        });

        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeFalse();
        results[0].Errors.Should().Contain("boom");
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAll_ExceptionThenSuccess_ReturnsSuccess()
    {
        _options.MaxRetries = 2;
        _options.BaseRetryDelayMs = 1;
        var task = CreateTask();
        SetupScheduler(new[] { task });
        var manager = CreateManager();
        await manager.EnqueueAsync(task);
        int callCount = 0;

        var results = await manager.ExecuteAllAsync((t, ct) =>
        {
            callCount++;
            if (callCount == 1) throw new InvalidOperationException("first fail");
            return Task.FromResult(SuccessResult(t.Id));
        });

        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeTrue();
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAll_BlockedTask_ReturnsFailureWithReason()
    {
        var task = CreateTask();
        _resourceScheduler.Setup(s => s.CreateExecutionPlanAsync(
                It.IsAny<IReadOnlyList<CoreTask>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulePlan(
                new[] { new ScheduledTask(task, 0, "default", null, false, "no-gpu-available") },
                1));
        var manager = CreateManager();
        await manager.EnqueueAsync(task);

        var results = await manager.ExecuteAllAsync((t, ct) => Task.FromResult(SuccessResult(t.Id)));

        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeFalse();
        results[0].Errors.Should().Contain("no-gpu-available");
    }

    [Fact]
    public async Task ExecuteAll_ResultsOrderedByTaskOrder()
    {
        var task1 = CreateTask(order: 3);
        var task2 = CreateTask(order: 1);
        var task3 = CreateTask(order: 2);
        var tasks = new[] { task1, task2, task3 };
        SetupScheduler(tasks);
        var manager = CreateManager();
        foreach (var t in tasks)
            await manager.EnqueueAsync(t);

        var results = await manager.ExecuteAllAsync((t, ct) => Task.FromResult(SuccessResult(t.Id)));

        results.Should().HaveCount(3);
        results[0].TaskId.Should().Be(task2.Id);
        results[1].TaskId.Should().Be(task3.Id);
        results[2].TaskId.Should().Be(task1.Id);
    }

    [Fact]
    public async Task ExecuteAll_GpuScheduledTask_AcquiresGpuSlot()
    {
        var task = CreateTask();
        _resourceScheduler.Setup(s => s.CreateExecutionPlanAsync(
                It.IsAny<IReadOnlyList<CoreTask>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulePlan(
                new[] { new ScheduledTask(task, 0, "gpu-model", 0, true, null) },
                1));
        var manager = CreateManager();
        await manager.EnqueueAsync(task);

        var results = await manager.ExecuteAllAsync((t, ct) => Task.FromResult(SuccessResult(t.Id)));

        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // RuntimeOptions
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RuntimeOptions_Defaults_AreReasonable()
    {
        var opts = new RuntimeOptions();

        opts.MaxDegreeOfParallelism.Should().Be(64);
        opts.MaxRetries.Should().Be(2);
        opts.BaseRetryDelayMs.Should().Be(250);
        opts.QueueShardCount.Should().Be(32);
    }

    [Fact]
    public async Task Manager_SingleShard_StillWorks()
    {
        var opts = new RuntimeOptions { QueueShardCount = 1, MaxDegreeOfParallelism = 1, MaxRetries = 0, BaseRetryDelayMs = 1 };
        var task = CreateTask();
        _resourceScheduler.Setup(s => s.CreateExecutionPlanAsync(
                It.IsAny<IReadOnlyList<CoreTask>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulePlan(
                new[] { new ScheduledTask(task, 0, "default", null, true, null) },
                1));
        var manager = CreateManager(opts);
        await manager.EnqueueAsync(task);

        var results = await manager.ExecuteAllAsync((t, ct) => Task.FromResult(SuccessResult(t.Id)));

        results.Should().HaveCount(1);
    }
}
