import type { HumanOverrideEntry } from '../types';
import { ACTION_LABELS, STATUS_LABELS } from '../types';

interface Props {
  entries: HumanOverrideEntry[];
  acting: boolean;
  onRollback: (workflowId: string, overrideId: string, reason: string, performedBy: string) => Promise<unknown>;
}

function timeAgo(utc: string): string {
  const diff = Date.now() - new Date(utc).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  return `${Math.floor(hrs / 24)}d ago`;
}

function statusClass(status: string): string {
  switch (status) {
    case 'Applied': return 'ho-status--applied';
    case 'RolledBack': return 'ho-status--rolledback';
    case 'Failed': return 'ho-status--failed';
    default: return 'ho-status--pending';
  }
}

function actionClass(action: string): string {
  switch (action) {
    case 'PauseWorkflow': return 'ho-act--pause';
    case 'ResumeWorkflow': return 'ho-act--resume';
    case 'CancelAction': return 'ho-act--cancel';
    case 'ModifyStrategy': return 'ho-act--modify';
    case 'Rollback': return 'ho-act--rollback';
    default: return '';
  }
}

export function OverrideLogTable({ entries, acting, onRollback }: Props) {
  if (entries.length === 0) {
    return (
      <section className="ho-card">
        <span className="ho-section-label">Override Log</span>
        <p className="ho-empty">No overrides recorded yet. All interventions will appear here.</p>
      </section>
    );
  }

  return (
    <section className="ho-card">
      <span className="ho-section-label">Override Log</span>
      <p className="ho-section-desc">
        Complete audit trail of all human interventions. Each override is logged with reason, user, and timestamps.
      </p>

      <div className="ho-log-list">
        {entries.map((entry) => (
          <div key={entry.id} className="ho-log-row">
            <div className="ho-log-header">
              <span className={`ho-log-action ${actionClass(entry.action)}`}>
                {ACTION_LABELS[entry.action] ?? entry.action}
              </span>
              <span className={`ho-log-status ${statusClass(entry.status)}`}>
                {STATUS_LABELS[entry.status] ?? entry.status}
              </span>
              <span className="ho-log-time">{timeAgo(entry.createdAtUtc)}</span>
            </div>

            <div className="ho-log-body">
              <span className="ho-log-reason">{entry.reason}</span>
              <span className="ho-log-meta">
                by {entry.performedBy} &middot; workflow {entry.workflowId.slice(0, 8)}...
              </span>
            </div>

            {entry.previousValue && entry.newValue && (
              <div className="ho-log-diff">
                <span className="ho-diff-old">{entry.previousValue}</span>
                <span className="ho-diff-arrow">&rarr;</span>
                <span className="ho-diff-new">{entry.newValue}</span>
              </div>
            )}

            {entry.status === 'Applied' && entry.action !== 'Rollback' && entry.action !== 'CancelAction' && (
              <button
                className="ho-btn-rollback"
                disabled={acting}
                onClick={() =>
                  onRollback(entry.workflowId, entry.id, `Rollback of ${entry.action}`, entry.performedBy)
                }
              >
                Rollback
              </button>
            )}
          </div>
        ))}
      </div>
    </section>
  );
}
