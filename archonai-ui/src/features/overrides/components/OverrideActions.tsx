import { useCallback, useState } from 'react';
import { usePermissions } from '../../../auth/usePermissions';

interface Props {
  acting: boolean;
  onPause: (workflowId: string, reason: string, performedBy: string) => Promise<unknown>;
  onResume: (workflowId: string, reason: string, performedBy: string) => Promise<unknown>;
  onCancel: (workflowId: string, reason: string, performedBy: string, taskId?: string) => Promise<unknown>;
  onModifyStrategy: (
    workflowId: string,
    previousStrategy: string,
    newStrategy: string,
    reason: string,
    performedBy: string,
  ) => Promise<unknown>;
}

type ActionType = 'pause' | 'resume' | 'cancel' | 'modify-strategy';

const ACTIONS: { type: ActionType; label: string; icon: string; className: string }[] = [
  { type: 'pause', label: 'Pause Workflow', icon: '\u23F8', className: 'ho-action-btn--pause' },
  { type: 'resume', label: 'Resume Workflow', icon: '\u25B6', className: 'ho-action-btn--resume' },
  { type: 'cancel', label: 'Cancel Action', icon: '\u2715', className: 'ho-action-btn--cancel' },
  { type: 'modify-strategy', label: 'Modify Strategy', icon: '\u270E', className: 'ho-action-btn--modify' },
];

export function OverrideActions({ acting, onPause, onResume, onCancel, onModifyStrategy }: Props) {
  const { hasPermission } = usePermissions();
  const canExecuteWorkflows = hasPermission('workflows:execute');
  const canWriteWorkflows = hasPermission('workflows:write');
  const [activeAction, setActiveAction] = useState<ActionType | null>(null);
  const [workflowId, setWorkflowId] = useState('');
  const [reason, setReason] = useState('');
  const [performedBy, setPerformedBy] = useState('operator');
  const [taskId, setTaskId] = useState('');
  const [previousStrategy, setPreviousStrategy] = useState('');
  const [newStrategy, setNewStrategy] = useState('');

  const resetForm = useCallback(() => {
    setActiveAction(null);
    setWorkflowId('');
    setReason('');
    setTaskId('');
    setPreviousStrategy('');
    setNewStrategy('');
  }, []);

  const handleSubmit = useCallback(async () => {
    if (!workflowId || !reason) return;

    switch (activeAction) {
      case 'pause':
        await onPause(workflowId, reason, performedBy);
        break;
      case 'resume':
        await onResume(workflowId, reason, performedBy);
        break;
      case 'cancel':
        await onCancel(workflowId, reason, performedBy, taskId || undefined);
        break;
      case 'modify-strategy':
        if (!previousStrategy || !newStrategy) return;
        await onModifyStrategy(workflowId, previousStrategy, newStrategy, reason, performedBy);
        break;
    }
    resetForm();
  }, [
    activeAction, workflowId, reason, performedBy, taskId,
    previousStrategy, newStrategy,
    onPause, onResume, onCancel, onModifyStrategy, resetForm,
  ]);

  return (
    <section className="ho-card">
      <span className="ho-section-label">Intervention Actions</span>
      <p className="ho-section-desc">
        Take control of running workflows. Pause, resume, cancel, or modify strategies in real time.
      </p>

      <div className="ho-action-grid">
        {ACTIONS.map(({ type, label, icon, className }) => {
          const needsWrite = type === 'cancel' || type === 'modify-strategy';
          const allowed = needsWrite ? canWriteWorkflows : canExecuteWorkflows;
          return (
            <button
              key={type}
              className={`ho-action-btn ${className} ${activeAction === type ? 'ho-action-btn--selected' : ''}`}
              onClick={() => setActiveAction(activeAction === type ? null : type)}
              disabled={acting || !allowed}
              title={!allowed ? 'Insufficient permissions' : undefined}
            >
              <span className="ho-action-icon">{icon}</span>
              <span className="ho-action-label">{label}</span>
            </button>
          );
        })}
      </div>

      {activeAction && (
        <div className="ho-action-form">
          <div className="ho-form-row">
            <div className="ho-form-field">
              <label className="ho-field-label">Workflow ID</label>
              <input
                className="ho-input"
                type="text"
                placeholder="e.g. 3fa85f64-5717-4562-b3fc-2c963f66afa6"
                value={workflowId}
                onChange={(e) => setWorkflowId(e.target.value)}
              />
            </div>
            <div className="ho-form-field">
              <label className="ho-field-label">Performed By</label>
              <input
                className="ho-input"
                type="text"
                value={performedBy}
                onChange={(e) => setPerformedBy(e.target.value)}
              />
            </div>
          </div>

          {activeAction === 'cancel' && (
            <div className="ho-form-field">
              <label className="ho-field-label">Task ID (optional)</label>
              <input
                className="ho-input"
                type="text"
                placeholder="Leave empty to cancel entire workflow"
                value={taskId}
                onChange={(e) => setTaskId(e.target.value)}
              />
            </div>
          )}

          {activeAction === 'modify-strategy' && (
            <div className="ho-form-row">
              <div className="ho-form-field">
                <label className="ho-field-label">Current Strategy</label>
                <input
                  className="ho-input"
                  type="text"
                  placeholder="e.g. balanced"
                  value={previousStrategy}
                  onChange={(e) => setPreviousStrategy(e.target.value)}
                />
              </div>
              <div className="ho-form-field">
                <label className="ho-field-label">New Strategy</label>
                <input
                  className="ho-input"
                  type="text"
                  placeholder="e.g. cost-optimized"
                  value={newStrategy}
                  onChange={(e) => setNewStrategy(e.target.value)}
                />
              </div>
            </div>
          )}

          <div className="ho-form-field">
            <label className="ho-field-label">Reason</label>
            <textarea
              className="ho-textarea"
              rows={2}
              placeholder="Describe why this intervention is needed..."
              value={reason}
              onChange={(e) => setReason(e.target.value)}
            />
          </div>

          <div className="ho-form-actions">
            <button className="ho-btn-secondary" onClick={resetForm}>
              Cancel
            </button>
            <button
              className="ho-btn-primary"
              onClick={handleSubmit}
              disabled={acting || !workflowId || !reason}
            >
              {acting ? (
                <>
                  <div className="phase-spinner" />
                  Applying...
                </>
              ) : (
                'Apply Override'
              )}
            </button>
          </div>
        </div>
      )}
    </section>
  );
}
