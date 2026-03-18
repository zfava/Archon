import { useState, useEffect, useCallback } from 'react';
import { api } from '../../api/client';
import './hero-workflows.css';

interface StepDef {
  stepId: string;
  name: string;
  description: string;
  subsystem: string;
  requiresInput: boolean;
}
interface WorkflowDef {
  workflowType: string;
  displayName: string;
  description: string;
  domain: string;
  steps: StepDef[];
  category: string;
}
interface StepState {
  stepId: string;
  status: string;
  detail: string | null;
  completedAtUtc: string | null;
}
interface WorkflowInstance {
  id: string;
  tenantId: string;
  workflowType: string;
  title: string;
  status: string;
  steps: StepState[];
  artifacts: Record<string, string>;
  initiatedBy: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}
interface WorkflowSummary {
  id: string;
  workflowType: string;
  title: string;
  status: string;
  completedSteps: number;
  totalSteps: number;
  initiatedBy: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

function statusClass(s: string): string {
  return `hw-status hw-status--${s.toLowerCase().replace(/\s/g, '')}`;
}

function stepIndicatorClass(s: string): string {
  return `hw-step-indicator hw-step-indicator--${s.toLowerCase()}`;
}

function stepNumber(status: string): string {
  if (status === 'Completed') return '\u2713';
  if (status === 'Failed') return '\u2717';
  if (status === 'InProgress') return '\u25B6';
  if (status === 'Skipped') return '\u2014';
  return '\u00B7';
}

export function HeroWorkflowsView() {
  const [tab, setTab] = useState<'catalog' | 'instances'>('catalog');
  const [catalog, setCatalog] = useState<WorkflowDef[]>([]);
  const [instances, setInstances] = useState<WorkflowSummary[]>([]);
  const [selected, setSelected] = useState<WorkflowInstance | null>(null);
  const [loading, setLoading] = useState(true);

  const loadInstances = useCallback(async () => {
    try {
      const list = await api.listHeroWorkflows() as WorkflowSummary[];
      setInstances(list);
    } catch { /* ignore */ }
  }, []);

  useEffect(() => {
    (async () => {
      try {
        const [cat] = await Promise.all([
          api.getHeroWorkflowCatalog() as Promise<WorkflowDef[]>,
          loadInstances(),
        ]);
        setCatalog(cat);
      } catch { /* ignore */ }
      finally { setLoading(false); }
    })();
  }, [loadInstances]);

  const handleStart = async (def: WorkflowDef) => {
    try {
      const instance = await api.startHeroWorkflow({
        workflowType: def.workflowType,
        title: `${def.displayName} — ${new Date().toLocaleDateString()}`,
        inputs: { domain: def.domain },
      }) as WorkflowInstance;
      setSelected(instance);
      setTab('instances');
      await loadInstances();
    } catch { /* ignore */ }
  };

  const handleAdvance = async () => {
    if (!selected) return;
    try {
      const updated = await api.advanceHeroWorkflow(selected.id) as WorkflowInstance;
      setSelected(updated);
      await loadInstances();
    } catch { /* ignore */ }
  };

  const handleCancel = async () => {
    if (!selected) return;
    try {
      const updated = await api.cancelHeroWorkflow(selected.id) as WorkflowInstance;
      setSelected(updated);
      await loadInstances();
    } catch { /* ignore */ }
  };

  const handleSelect = async (id: string) => {
    try {
      const instance = await api.getHeroWorkflow(id) as WorkflowInstance;
      setSelected(instance);
    } catch { /* ignore */ }
  };

  if (loading) return <div className="hw-view"><div className="hw-loading">Loading hero workflows...</div></div>;

  return (
    <div className="hw-view">
      <header className="hw-header">
        <h1 className="hw-title">Hero Workflows</h1>
        <p className="hw-subtitle">End-to-end governed business processes across ArchonAI subsystems.</p>
      </header>

      <div className="hw-tabs">
        <button className={`hw-tab ${tab === 'catalog' ? 'hw-tab--active' : ''}`} onClick={() => { setTab('catalog'); setSelected(null); }}>
          Catalog
        </button>
        <button className={`hw-tab ${tab === 'instances' ? 'hw-tab--active' : ''}`} onClick={() => { setTab('instances'); setSelected(null); }}>
          Instances ({instances.length})
        </button>
      </div>

      {/* ── Detail View ────────────────────────────────────── */}
      {selected && (
        <div className="hw-detail">
          <div className="hw-detail-header">
            <button className="hw-detail-back" onClick={() => setSelected(null)}>Back</button>
            <h2 className="hw-detail-title">{selected.title}</h2>
            <span className={statusClass(selected.status)}>{selected.status}</span>
          </div>

          <div className="hw-steps">
            {selected.steps.map((step, i) => (
              <div key={step.stepId} className="hw-step-row">
                <div className={stepIndicatorClass(step.status)}>{stepNumber(step.status)}</div>
                <span className="hw-step-name">
                  {i + 1}. {catalog.find(c => c.workflowType === selected.workflowType)?.steps[i]?.name ?? step.stepId}
                </span>
                <span className="hw-step-subsystem">
                  {catalog.find(c => c.workflowType === selected.workflowType)?.steps[i]?.subsystem ?? ''}
                </span>
                {step.detail && <span className="hw-step-detail">{step.detail}</span>}
                {step.completedAtUtc && <span className="hw-instance-ts">{fmtDate(step.completedAtUtc)}</span>}
              </div>
            ))}
          </div>

          {Object.keys(selected.artifacts).length > 0 && (
            <div className="hw-artifacts">
              <div className="hw-artifacts-title">Artifacts</div>
              <div className="hw-artifact-grid">
                {Object.entries(selected.artifacts).map(([k, v]) => (
                  <div key={k} className="hw-artifact-item">
                    <div className="hw-artifact-key">{k}</div>
                    <div className="hw-artifact-val">{v}</div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {(selected.status === 'InProgress' || selected.status === 'AwaitingApproval') && (
            <div className="hw-actions">
              <button className="hw-btn hw-btn--primary" onClick={handleAdvance}>Advance</button>
              <button className="hw-btn hw-btn--danger" onClick={handleCancel}>Cancel</button>
            </div>
          )}
        </div>
      )}

      {/* ── Catalog Tab ────────────────────────────────────── */}
      {!selected && tab === 'catalog' && (
        <div className="hw-catalog">
          {catalog.map(def => (
            <div key={def.workflowType} className="hw-catalog-card">
              <div className="hw-catalog-domain">{def.domain} / {def.category}</div>
              <h3 className="hw-catalog-name">{def.displayName}</h3>
              <p className="hw-catalog-desc">{def.description}</p>
              <div className="hw-catalog-steps">
                {def.steps.map(s => (
                  <span key={s.stepId} className="hw-step-chip">{s.subsystem}</span>
                ))}
              </div>
              <button className="hw-catalog-start" onClick={() => handleStart(def)}>
                Start Workflow
              </button>
            </div>
          ))}
        </div>
      )}

      {/* ── Instances Tab ──────────────────────────────────── */}
      {!selected && tab === 'instances' && (
        instances.length === 0
          ? <div className="hw-empty">No workflow instances yet. Start one from the catalog.</div>
          : <div className="hw-instance-list">
              {instances.map(inst => (
                <div key={inst.id} className="hw-instance-row" onClick={() => handleSelect(inst.id)}>
                  <span className="hw-instance-type">{inst.workflowType}</span>
                  <span className="hw-instance-title">{inst.title}</span>
                  <span className={statusClass(inst.status)}>{inst.status}</span>
                  <span className="hw-instance-progress">{inst.completedSteps}/{inst.totalSteps}</span>
                  <span className="hw-instance-ts">{fmtDate(inst.updatedAtUtc)}</span>
                </div>
              ))}
            </div>
      )}
    </div>
  );
}
