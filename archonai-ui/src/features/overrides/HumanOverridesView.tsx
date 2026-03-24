import { Link } from 'react-router-dom';
import { useHumanOverrides } from './hooks/useHumanOverrides';
import { OverrideActions } from './components/OverrideActions';
import { OverrideLogTable } from './components/OverrideLogTable';
import './overrides.css';

export function HumanOverridesView() {
  const {
    loading,
    acting,
    error,
    entries,
    lastResult,
    workflowFilter,
    pauseWorkflow,
    resumeWorkflow,
    cancelAction,
    modifyStrategy,
    rollback,
    setWorkflowFilter,
  } = useHumanOverrides();

  if (loading) {
    return (
      <div className="ho-container">
        <div className="ho-loading">
          <div className="phase-spinner" />
          <span>Loading override log...</span>
        </div>
      </div>
    );
  }

  return (
    <div className="ho-container">
      {/* Header */}
      <header className="ho-header">
        <div className="ho-header-left">
          <Link to="/" className="sg-back-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
            Console
          </Link>
          <h1 className="ho-page-title">Human Overrides</h1>
          <span className="ho-entry-count">{entries.length} entries</span>
        </div>
        <div className="ho-header-right">
          <div className="ho-filter">
            <label className="ho-field-label">Filter by Workflow</label>
            <input
              className="ho-input ho-input--sm"
              type="text"
              placeholder="Workflow ID..."
              value={workflowFilter}
              onChange={(e) => setWorkflowFilter(e.target.value)}
            />
          </div>
        </div>
      </header>

      {/* Result banner */}
      {lastResult && (
        <div className={`ho-result-banner ${lastResult.success ? 'ho-result--success' : 'ho-result--error'}`}>
          <span>{lastResult.message}</span>
          {lastResult.workflowState && (
            <span className="ho-result-state">State: {lastResult.workflowState}</span>
          )}
        </div>
      )}

      {/* Error banner */}
      {error && (
        <div className="ho-error-banner">
          <span>{error}</span>
        </div>
      )}

      {/* Content */}
      <div className="ho-content">
        <div className="ho-main">
          <OverrideActions
            acting={acting}
            onPause={pauseWorkflow}
            onResume={resumeWorkflow}
            onCancel={cancelAction}
            onModifyStrategy={modifyStrategy}
          />

          <OverrideLogTable
            entries={entries}
            acting={acting}
            onRollback={rollback}
          />
        </div>
      </div>
    </div>
  );
}
