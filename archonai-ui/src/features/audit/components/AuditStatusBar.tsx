import type { AuditIntegrityResult, AuditLogStatus } from '../types';

interface Props {
  status: AuditLogStatus | null;
  integrity: AuditIntegrityResult | null;
  verifying: boolean;
  onVerify: () => void;
}

export function AuditStatusBar({ status, integrity, verifying, onVerify }: Props) {
  if (!status) return null;

  return (
    <div className="al-status-bar">
      <div className="al-status-metrics">
        <div className="al-metric">
          <span className="al-metric-value">{status.totalEntries}</span>
          <span className="al-metric-label">Total</span>
        </div>
        <div className="al-metric">
          <span className="al-metric-value al-metric--agent">{status.agentActionEntries}</span>
          <span className="al-metric-label">Agent</span>
        </div>
        <div className="al-metric">
          <span className="al-metric-value al-metric--workflow">{status.workflowChangeEntries}</span>
          <span className="al-metric-label">Workflow</span>
        </div>
        <div className="al-metric">
          <span className="al-metric-value al-metric--user">{status.userActivityEntries}</span>
          <span className="al-metric-label">User</span>
        </div>
      </div>

      <div className="al-integrity">
        {integrity && (
          <span
            className={`al-integrity-badge ${integrity.integrityValid ? 'al-integrity--valid' : 'al-integrity--invalid'}`}
          >
            {integrity.integrityValid ? 'Chain Valid' : 'Chain Broken'}
          </span>
        )}
        <button
          className="al-verify-btn"
          onClick={onVerify}
          disabled={verifying}
        >
          {verifying ? 'Verifying...' : 'Verify Integrity'}
        </button>
      </div>
    </div>
  );
}
