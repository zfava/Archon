using ArchonAI.Core.Models.Telemetry;

namespace ArchonAI.Core.Interfaces;

public interface ITaskTelemetryStore
{
    global::System.Threading.Tasks.Task RecordAsync(TaskExecutionTelemetry telemetry, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<TaskExecutionTelemetry>> QueryByObjectiveAsync(
        Guid objectiveId,
        int limit = 200,
        CancellationToken cancellationToken = default);
}
