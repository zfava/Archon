import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useInspection } from './hooks/useInspection';
import { RationaleInspectionCard } from './components/RationaleInspectionCard';
import { PolicyInspectionCard } from './components/PolicyInspectionCard';
import { MemoryReferencesCard } from './components/MemoryReferencesCard';
import { WorkflowDiagnosticsCard } from './components/WorkflowDiagnosticsCard';
import './inspection.css';

type Tab = 'overview' | 'rationale' | 'policy' | 'memory' | 'diagnostics';

export function OperatorInspectionView() {
  const {
    loading,
    error,
    summaries,
    rationaleBundle,
    policyEvaluation,
    memoryReferences,
    workflowDiagnostics,
    loadSummaries,
    inspectDecisionRationale,
    inspectPolicyEvaluation,
    inspectMemoryReferences,
    inspectWorkflowDiagnostics,
  } = useInspection();

  const [tab, setTab] = useState<Tab>('overview');
  const [subjectId, setSubjectId] = useState('');
  const [subjectType, setSubjectType] = useState('decision');
  const [filterDomain, setFilterDomain] = useState('');

  useEffect(() => {
    loadSummaries();
  }, [loadSummaries]);

  const handleInspect = async () => {
    if (!subjectId) return;

    switch (tab) {
      case 'rationale':
        await inspectDecisionRationale(subjectId);
        break;
      case 'policy':
        await inspectPolicyEvaluation(subjectType, subjectId);
        break;
      case 'memory':
        await inspectMemoryReferences(subjectType, subjectId);
        break;
      case 'diagnostics':
        await inspectWorkflowDiagnostics(subjectId);
        break;
      case 'overview':
        await loadSummaries(subjectType || undefined, filterDomain || undefined);
        break;
    }
  };

  const handleRowClick = async (summary: { subjectId: string; subjectType: string }) => {
    setSubjectId(summary.subjectId);
    setSubjectType(summary.subjectType);

    if (summary.subjectType === 'decision') {
      setTab('rationale');
      await inspectDecisionRationale(summary.subjectId);
    } else if (summary.subjectType === 'workflow') {
      setTab('diagnostics');
      await inspectWorkflowDiagnostics(summary.subjectId);
    }
  };

  return (
    <div className="ins-container">
      <header className="ins-header">
        <div className="ins-header-left">
          <h1 className="ins-page-title">Operator Inspection</h1>
          <p className="ins-subtitle">Deep introspection into decisions, policies, memory context, and workflow diagnostics</p>
        </div>
      </header>

      {error && (
        <div className="ins-error-banner">
          <span>{error}</span>
        </div>
      )}

      <div className="ins-content">
        <div className="ins-main">
          {/* Navigation tabs */}
          <div className="ins-tabs">
            {(['overview', 'rationale', 'policy', 'memory', 'diagnostics'] as Tab[]).map((t) => (
              <button
                key={t}
                className={`ins-tab ${tab === t ? 'ins-tab--active' : ''}`}
                onClick={() => setTab(t)}
              >
                {t === 'overview' ? 'Overview' :
                 t === 'rationale' ? 'Decision Rationale' :
                 t === 'policy' ? 'Policy Inspection' :
                 t === 'memory' ? 'Memory/Context' : 'Workflow Diagnostics'}
              </button>
            ))}
          </div>

          {/* Query controls */}
          {tab !== 'overview' && (
            <section className="ins-card">
              <span className="ins-section-label">Inspect Subject</span>
              <div className="ins-form">
                <div className="ins-form-row">
                  {tab !== 'rationale' && tab !== 'diagnostics' && (
                    <div className="ins-form-field">
                      <label className="ins-field-label">Subject Type</label>
                      <select
                        className="ins-input"
                        value={subjectType}
                        onChange={(e) => setSubjectType(e.target.value)}
                      >
                        <option value="decision">Decision</option>
                        <option value="workflow">Workflow</option>
                        <option value="action">Action</option>
                      </select>
                    </div>
                  )}
                  <div className="ins-form-field">
                    <label className="ins-field-label">
                      {tab === 'rationale' ? 'Decision ID' :
                       tab === 'diagnostics' ? 'Workflow ID' : 'Subject ID'}
                    </label>
                    <input
                      className="ins-input"
                      type="text"
                      placeholder="e.g. 3fa85f64-5717-4562-b3fc-2c963f66afa6"
                      value={subjectId}
                      onChange={(e) => setSubjectId(e.target.value)}
                    />
                  </div>
                </div>
                <div className="ins-form-actions">
                  <button className="ins-btn-primary" onClick={handleInspect} disabled={loading || !subjectId}>
                    {loading ? (
                      <>
                        <div className="phase-spinner" />
                        Inspecting...
                      </>
                    ) : (
                      'Inspect'
                    )}
                  </button>
                </div>
              </div>
            </section>
          )}

          {/* Overview tab: summary list */}
          {tab === 'overview' && (
            <section className="ins-card">
              <div className="ins-card-header">
                <span className="ins-section-label">Inspection Queue</span>
                <div className="ins-filter-row">
                  <select
                    className="ins-input ins-input--sm"
                    value={subjectType}
                    onChange={(e) => setSubjectType(e.target.value)}
                  >
                    <option value="">All Types</option>
                    <option value="decision">Decisions</option>
                    <option value="workflow">Workflows</option>
                  </select>
                  <input
                    className="ins-input ins-input--sm"
                    type="text"
                    placeholder="Filter domain..."
                    value={filterDomain}
                    onChange={(e) => setFilterDomain(e.target.value)}
                  />
                  <button
                    className="ins-btn-secondary"
                    onClick={() => loadSummaries(subjectType || undefined, filterDomain || undefined)}
                    disabled={loading}
                  >
                    Refresh
                  </button>
                </div>
              </div>

              {summaries.length === 0 && !loading && (
                <p className="ins-empty">No inspection subjects found. Create decisions or workflows to inspect them.</p>
              )}

              <div className="ins-summary-list">
                {summaries.map((s) => (
                  <div
                    key={`${s.subjectType}-${s.subjectId}`}
                    className="ins-summary-row"
                    onClick={() => handleRowClick(s)}
                  >
                    <div className="ins-summary-main">
                      <span className={`ins-badge ins-badge--${s.subjectType}`}>{s.subjectType}</span>
                      <span className="ins-summary-title">{s.title}</span>
                      <span className={`ins-badge ins-badge--status-${s.status.toLowerCase()}`}>{s.status}</span>
                    </div>
                    <div className="ins-summary-meta">
                      <span>Domain: {s.domain}</span>
                      {s.confidence != null && <span>Confidence: {(s.confidence * 100).toFixed(0)}%</span>}
                      {s.riskScore != null && <span>Risk: {s.riskScore.toFixed(1)}</span>}
                      {s.hasPolicyViolations && <span className="ins-violation-tag">Policy Violations</span>}
                      {s.hasFailures && <span className="ins-violation-tag">Failures</span>}
                      <span>{new Date(s.createdAtUtc).toLocaleDateString()}</span>
                    </div>
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* Rationale tab */}
          {tab === 'rationale' && rationaleBundle && (
            <RationaleInspectionCard bundle={rationaleBundle} />
          )}

          {/* Policy tab */}
          {tab === 'policy' && policyEvaluation && (
            <PolicyInspectionCard evaluation={policyEvaluation} />
          )}

          {/* Memory tab */}
          {tab === 'memory' && (
            <MemoryReferencesCard references={memoryReferences} />
          )}

          {/* Diagnostics tab */}
          {tab === 'diagnostics' && workflowDiagnostics && (
            <WorkflowDiagnosticsCard diagnostics={workflowDiagnostics} />
          )}
        </div>
      </div>
    </div>
  );
}
