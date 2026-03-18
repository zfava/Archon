import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useActionSafety } from './hooks/useActionSafety';
import type { ActionSafetyClassification, GovernedActionRecord, RollbackAttempt } from './types';
import './action-safety.css';

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

function revBadgeClass(rev: string): string {
  return rev.toLowerCase();
}

function statusBadgeClass(status: string): string {
  return status.toLowerCase().replace(/\s/g, '');
}

function canRollback(action: GovernedActionRecord): boolean {
  return (
    action.status === 'RollbackEligible' ||
    action.status === 'Executed'
  ) && action.safetyClassification.reversibility !== 'Irreversible';
}

function ActionDetail({ action, onRollback, onBack }: {
  action: GovernedActionRecord;
  onRollback: () => void;
  onBack: () => void;
}) {
  const sc = action.safetyClassification;

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 20 }}>
        <button className="as-refresh" onClick={onBack}>Back</button>
        <div>
          <h2 style={{ margin: 0, fontSize: '1.1rem', color: 'var(--text-1)' }}>{action.description}</h2>
          <span style={{ fontSize: '0.78rem', color: 'var(--text-3)' }}>{action.actionType}</span>
        </div>
      </div>

      <div className="as-kpi-row">
        <div className="as-kpi">
          <div className="as-kpi-label">Status</div>
          <div className="as-kpi-value" style={{ fontSize: '1rem' }}>
            <span className={`as-badge ${statusBadgeClass(action.status)}`}>{action.status}</span>
          </div>
        </div>
        <div className="as-kpi">
          <div className="as-kpi-label">Reversibility</div>
          <div className="as-kpi-value" style={{ fontSize: '1rem' }}>
            <span className={`as-badge ${revBadgeClass(sc.reversibility)}`}>{sc.reversibility}</span>
          </div>
        </div>
        <div className="as-kpi">
          <div className="as-kpi-label">Rollback</div>
          <div className="as-kpi-value" style={{ fontSize: '1rem' }}>
            {sc.rollbackSupported ? 'Supported' : 'Not supported'}
          </div>
        </div>
        <div className="as-kpi">
          <div className="as-kpi-label">Strategy</div>
          <div className="as-kpi-value" style={{ fontSize: '1rem' }}>{sc.rollbackStrategy}</div>
        </div>
      </div>

      <div className="as-detail">
        <div className="as-detail-label">Safety Summary</div>
        <div className="as-detail-value">{sc.safetySummary}</div>
      </div>

      {sc.operatorNotes && (
        <div className="as-detail">
          <div className="as-detail-label">Operator Notes</div>
          <div className="as-detail-value">{sc.operatorNotes}</div>
        </div>
      )}

      {sc.rollbackWindow && (
        <div className="as-detail">
          <div className="as-detail-label">Rollback Window</div>
          <div className="as-detail-value">{sc.rollbackWindow}</div>
        </div>
      )}

      {action.compensationOutcome && (
        <div className="as-detail">
          <div className="as-detail-label">Compensation Outcome</div>
          <div className="as-detail-value">{action.compensationOutcome}</div>
        </div>
      )}

      {canRollback(action) && (
        <div style={{ marginBottom: 20 }}>
          <button className="as-rollback-btn" onClick={onRollback} style={{ padding: '8px 16px', fontSize: '0.85rem' }}>
            Trigger Rollback
          </button>
        </div>
      )}

      {action.rollbackHistory.length > 0 && (
        <div className="as-section">
          <div className="as-section-title">Rollback History</div>
          <div className="as-rollback-history">
            {action.rollbackHistory.map((attempt: RollbackAttempt) => (
              <div key={attempt.id} className="as-rollback-entry">
                <span className="as-rollback-entry-time">{fmtDate(attempt.initiatedAtUtc)}</span>
                <span className={`as-badge ${attempt.status.toLowerCase()}`}>{attempt.status}</span>
                <span style={{ color: 'var(--text-2)' }}>{attempt.detail || attempt.error || ''}</span>
                <span style={{ color: 'var(--text-3)', fontSize: '0.72rem' }}>by {attempt.initiatedBy}</span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

export function ActionSafetyView() {
  const { classifications, actions, summary, loading, error, refresh, triggerRollback } = useActionSafety();
  const [selectedAction, setSelectedAction] = useState<GovernedActionRecord | null>(null);
  const [rollbackError, setRollbackError] = useState<string | null>(null);

  const handleRollback = async (actionId: string) => {
    setRollbackError(null);
    try {
      const result = await triggerRollback(actionId);
      setSelectedAction(result);
    } catch (e) {
      setRollbackError(e instanceof Error ? e.message : 'Rollback failed');
    }
  };

  if (selectedAction) {
    return (
      <div className="as-view">
        {rollbackError && <div className="as-error">{rollbackError}</div>}
        <ActionDetail
          action={selectedAction}
          onRollback={() => handleRollback(selectedAction.id)}
          onBack={() => { setSelectedAction(null); setRollbackError(null); }}
        />
      </div>
    );
  }

  return (
    <div className="as-view">
      <header className="as-header">
        <div className="as-header-left">
          <Link to="/" className="as-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
          </Link>
          <div>
            <h1 className="as-title">Action Safety</h1>
            <p className="as-subtitle">Reversibility classifications, rollback eligibility, and compensation tracking</p>
          </div>
        </div>
        <button className="as-refresh" onClick={refresh}>Refresh</button>
      </header>

      {loading ? (
        <div className="as-loading">Loading action safety data...</div>
      ) : error ? (
        <div className="as-error">{error}</div>
      ) : (
        <>
          {/* Summary KPIs */}
          {summary && (
            <div className="as-kpi-row">
              <div className="as-kpi">
                <div className="as-kpi-label">Total Actions</div>
                <div className="as-kpi-value">{summary.totalActions}</div>
              </div>
              <div className="as-kpi">
                <div className="as-kpi-label">Reversible</div>
                <div className="as-kpi-value positive">{summary.reversible}</div>
              </div>
              <div className="as-kpi">
                <div className="as-kpi-label">Compensatable</div>
                <div className="as-kpi-value warning">{summary.compensatable}</div>
              </div>
              <div className="as-kpi">
                <div className="as-kpi-label">Irreversible</div>
                <div className="as-kpi-value negative">{summary.irreversible}</div>
              </div>
              <div className="as-kpi">
                <div className="as-kpi-label">Rollbacks OK</div>
                <div className="as-kpi-value positive">{summary.rollbacksSucceeded}</div>
              </div>
              <div className="as-kpi">
                <div className="as-kpi-label">Rollbacks Failed</div>
                <div className="as-kpi-value negative">{summary.rollbacksFailed}</div>
              </div>
              <div className="as-kpi">
                <div className="as-kpi-label">In Window</div>
                <div className="as-kpi-value">{summary.withinRollbackWindow}</div>
                <div className="as-kpi-sub">still eligible</div>
              </div>
            </div>
          )}

          {/* Safety Classifications */}
          <div className="as-section">
            <div className="as-section-title">Safety Classifications</div>
            {classifications.length > 0 ? (
              <table className="as-table">
                <thead>
                  <tr>
                    <th>Action Type</th>
                    <th>Reversibility</th>
                    <th>Rollback</th>
                    <th>Strategy</th>
                    <th>Window</th>
                    <th>Notes</th>
                  </tr>
                </thead>
                <tbody>
                  {classifications.map((c: ActionSafetyClassification) => (
                    <tr key={c.id}>
                      <td style={{ fontWeight: 500 }}>{c.actionType}</td>
                      <td><span className={`as-badge ${revBadgeClass(c.reversibility)}`}>{c.reversibility}</span></td>
                      <td>{c.rollbackSupported ? 'Yes' : 'No'}</td>
                      <td>{c.rollbackStrategy}</td>
                      <td>{c.rollbackWindow ?? '—'}</td>
                      <td style={{ maxWidth: 300, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{c.operatorNotes ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : (
              <div className="as-empty">No classifications registered</div>
            )}
          </div>

          {/* Governed Actions */}
          <div className="as-section">
            <div className="as-section-title">Governed Actions</div>
            {actions.length > 0 ? (
              <table className="as-table">
                <thead>
                  <tr>
                    <th>Action</th>
                    <th>Type</th>
                    <th>Reversibility</th>
                    <th>Status</th>
                    <th>Rollback</th>
                    <th>Executed</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {actions.map((a: GovernedActionRecord) => (
                    <tr key={a.id} onClick={() => setSelectedAction(a)} style={{ cursor: 'pointer' }}>
                      <td>{a.description}</td>
                      <td>{a.actionType}</td>
                      <td><span className={`as-badge ${revBadgeClass(a.safetyClassification.reversibility)}`}>{a.safetyClassification.reversibility}</span></td>
                      <td><span className={`as-badge ${statusBadgeClass(a.status)}`}>{a.status}</span></td>
                      <td>{a.safetyClassification.rollbackSupported ? 'Supported' : '—'}</td>
                      <td>{fmtDate(a.executedAtUtc)}</td>
                      <td>
                        {canRollback(a) && (
                          <button
                            className="as-rollback-btn"
                            onClick={(e) => { e.stopPropagation(); handleRollback(a.id); }}
                          >
                            Rollback
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : (
              <div className="as-empty">No governed actions recorded</div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
