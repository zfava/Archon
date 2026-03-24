import type { TaskPerformanceDashboard } from '../types';

interface Props {
  data: TaskPerformanceDashboard | null;
}

function trendArrow(direction: string): string {
  switch (direction.toLowerCase()) {
    case 'up': return '\u2191';
    case 'down': return '\u2193';
    default: return '\u2192';
  }
}

function trendClass(direction: string, metricName: string): string {
  // For failure rate, "up" is bad; for completion rate, "up" is good
  const isNegativeMetric = metricName.toLowerCase().includes('fail') ||
    metricName.toLowerCase().includes('cost') ||
    metricName.toLowerCase().includes('time');
  if (direction === 'up') return isNegativeMetric ? 'sa-trend--bad' : 'sa-trend--good';
  if (direction === 'down') return isNegativeMetric ? 'sa-trend--good' : 'sa-trend--bad';
  return 'sa-trend--flat';
}

export function TaskPanel({ data }: Props) {
  if (!data) {
    return (
      <section className="sa-panel">
        <h2 className="sa-panel-title">Running Tasks</h2>
        <div className="sa-panel-empty">Waiting for data...</div>
      </section>
    );
  }

  const runningTasks = data.totalTasks - data.completedTasks - data.failedTasks;

  return (
    <section className="sa-panel">
      <div className="sa-panel-header">
        <h2 className="sa-panel-title">Running Tasks</h2>
        <span className="sa-count sa-count--active">{runningTasks} running</span>
      </div>

      {/* Summary stats */}
      <div className="sa-task-summary">
        <div className="sa-summary-stat">
          <span className="sa-summary-val">{data.totalTasks.toLocaleString()}</span>
          <span className="sa-summary-lbl">Total</span>
        </div>
        <div className="sa-summary-stat">
          <span className="sa-summary-val sa-val--green">{data.completedTasks.toLocaleString()}</span>
          <span className="sa-summary-lbl">Completed</span>
        </div>
        <div className="sa-summary-stat">
          <span className="sa-summary-val sa-val--red">{data.failedTasks.toLocaleString()}</span>
          <span className="sa-summary-lbl">Failed</span>
        </div>
        <div className="sa-summary-stat">
          <span className="sa-summary-val">{Math.round(data.overallCompletionRate * 100)}%</span>
          <span className="sa-summary-lbl">Completion</span>
        </div>
      </div>

      {/* Task type breakdown */}
      {data.byTaskType.length > 0 && (
        <div className="sa-task-types">
          <span className="sa-section-label">By Task Type</span>
          {data.byTaskType.map((tt) => {
            const running = tt.total - tt.completed - tt.failed;
            return (
              <div key={tt.taskType} className="sa-task-type-row">
                <div className="sa-task-type-info">
                  <span className="sa-task-type-name">{tt.taskType}</span>
                  <span className="sa-task-type-count">
                    {running > 0 && <span className="sa-running-badge">{running} running</span>}
                    {tt.completed} done
                    {tt.failed > 0 && <span className="sa-failed-count"> / {tt.failed} failed</span>}
                  </span>
                </div>
                {/* Completion bar */}
                <div className="sa-task-bar-wrap">
                  <div
                    className="sa-task-bar sa-task-bar--done"
                    style={{ width: `${Math.round(tt.completionRate * 100)}%` }}
                  />
                  {tt.failed > 0 && (
                    <div
                      className="sa-task-bar sa-task-bar--failed"
                      style={{ width: `${Math.round((tt.failed / tt.total) * 100)}%` }}
                    />
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Trends */}
      {data.trends.length > 0 && (
        <div className="sa-trends">
          <span className="sa-section-label">Trends</span>
          <div className="sa-trend-list">
            {data.trends.slice(0, 6).map((t, i) => (
              <div key={i} className="sa-trend-item">
                <span className="sa-trend-name">{t.taskType} {t.metricName}</span>
                <span className={`sa-trend-change ${trendClass(t.trendDirection, t.metricName)}`}>
                  {trendArrow(t.trendDirection)} {Math.abs(t.changePercent).toFixed(1)}%
                </span>
              </div>
            ))}
          </div>
        </div>
      )}
    </section>
  );
}
