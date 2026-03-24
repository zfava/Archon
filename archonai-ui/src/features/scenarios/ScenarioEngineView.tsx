import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import './scenario-engine.css';

interface Assumption {
  name: string;
  currentValue: string;
  proposedValue: string;
  unit: string | null;
  rationale: string | null;
}

interface ProjectedEffect {
  area: string;
  metric: string;
  baselineValue: number | null;
  projectedValue: number | null;
  unit: string | null;
  direction: string;
  confidence: string;
}

interface ScenarioLink {
  artifactType: string;
  artifactId: string;
  label: string | null;
}

interface Scenario {
  id: string;
  tenantId: string;
  title: string;
  description: string | null;
  type: string;
  status: string;
  assumptions: Assumption[];
  projectedEffects: ProjectedEffect[];
  linkedKpis: ScenarioLink[];
  linkedDecisions: ScenarioLink[];
  linkedEntities: ScenarioLink[];
  createdBy: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

interface ComparisonAxis {
  metric: string;
  unit: string | null;
  valuesByScenarioId: Record<string, number | null>;
}

interface Comparison {
  scenarioIds: string[];
  axes: ComparisonAxis[];
  generatedAtUtc: string;
}

const SCENARIO_TYPES = ['WhatIf', 'CostReduction', 'GrowthPlanning', 'RiskMitigation', 'ResourceReallocation', 'ProcessChange', 'StrategicPivot'] as const;

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

function typeClass(type: string): string {
  return `scenario-type-${type.toLowerCase()}`;
}

function statusClass(status: string): string {
  return `scenario-status-${status.toLowerCase()}`;
}

function dirClass(dir: string): string {
  return `dir-${dir.toLowerCase()}`;
}

export function ScenarioEngineView() {
  const [scenarios, setScenarios] = useState<Scenario[]>([]);
  const [filterType, setFilterType] = useState<string | null>(null);
  const [selected, setSelected] = useState<Scenario | null>(null);
  const [compareIds, setCompareIds] = useState<Set<string>>(new Set());
  const [comparison, setComparison] = useState<Comparison | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const params: Record<string, string> = {};
      if (filterType) params.type = filterType;
      const list = await api.listScenarios(params) as Scenario[];
      setScenarios(list);
    } catch {
      setScenarios([]);
    } finally {
      setLoading(false);
    }
  }, [filterType]);

  useEffect(() => { load(); }, [load]);

  const toggleCompare = (id: string) => {
    setCompareIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
    setComparison(null);
  };

  const runCompare = async () => {
    if (compareIds.size < 2) return;
    try {
      const result = await api.compareScenarios(Array.from(compareIds)) as Comparison;
      setComparison(result);
    } catch { /* ignore */ }
  };

  const selectScenario = async (id: string) => {
    try {
      const s = await api.getScenario(id) as Scenario;
      setSelected(s);
      setComparison(null);
    } catch { /* ignore */ }
  };

  return (
    <div className="scenario-view">
      <header className="scenario-header">
        <div className="scenario-header-left">
          <Link to="/" className="scenario-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
          </Link>
          <div>
            <h1 className="scenario-title">Scenario Engine</h1>
            <p className="scenario-subtitle">Explore what-if planning paths with structured assumptions and projected effects</p>
          </div>
        </div>
        <button className="scenario-create-btn" onClick={() => setShowForm(true)}>+ New Scenario</button>
      </header>

      {/* Type filter */}
      <div className="scenario-type-bar">
        <button className={`scenario-type-btn ${filterType === null ? 'active' : ''}`} onClick={() => setFilterType(null)}>All</button>
        {SCENARIO_TYPES.map((t) => (
          <button key={t} className={`scenario-type-btn ${filterType === t ? 'active' : ''}`} onClick={() => setFilterType(filterType === t ? null : t)}>{t}</button>
        ))}
      </div>

      {/* Compare bar */}
      {compareIds.size > 0 && (
        <div className="scenario-compare-bar">
          <span className="scenario-compare-count">{compareIds.size} selected</span>
          <button className="scenario-compare-btn" disabled={compareIds.size < 2} onClick={runCompare}>Compare Selected</button>
          <button className="scenario-type-btn" onClick={() => { setCompareIds(new Set()); setComparison(null); }}>Clear</button>
        </div>
      )}

      {/* Comparison result */}
      {comparison && (
        <div className="scenario-comparison">
          <div className="scenario-section-label">Comparison</div>
          <table className="scenario-comparison-table">
            <thead>
              <tr>
                <th>Metric</th>
                {comparison.scenarioIds.map((id) => {
                  const s = scenarios.find((sc) => sc.id === id);
                  return <th key={id}>{s?.title ?? id.slice(0, 8)}</th>;
                })}
              </tr>
            </thead>
            <tbody>
              {comparison.axes.map((axis, i) => (
                <tr key={i}>
                  <td>{axis.metric}{axis.unit ? ` (${axis.unit})` : ''}</td>
                  {comparison.scenarioIds.map((id) => (
                    <td key={id}>{axis.valuesByScenarioId[id] != null ? axis.valuesByScenarioId[id] : '—'}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Detail panel */}
      {selected && (
        <div className="scenario-detail">
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 8 }}>
            <span className={`scenario-badge ${typeClass(selected.type)}`}>{selected.type}</span>
            <span className={`scenario-badge ${statusClass(selected.status)}`}>{selected.status}</span>
            <button className="scenario-type-btn" onClick={() => setSelected(null)} style={{ marginLeft: 'auto' }}>Close</button>
          </div>
          <div className="scenario-detail-title">{selected.title}</div>
          {selected.description && <div className="scenario-detail-desc">{selected.description}</div>}

          {/* Assumptions */}
          {selected.assumptions.length > 0 && (
            <div className="scenario-section">
              <div className="scenario-section-label">Assumptions</div>
              <table className="scenario-assumptions">
                <thead>
                  <tr><th>Name</th><th>Current</th><th>Proposed</th><th>Rationale</th></tr>
                </thead>
                <tbody>
                  {selected.assumptions.map((a, i) => (
                    <tr key={i}>
                      <td>{a.name}{a.unit ? ` (${a.unit})` : ''}</td>
                      <td className="val-current">{a.currentValue}</td>
                      <td className="val-proposed">{a.proposedValue}</td>
                      <td style={{ color: 'var(--text-3)', fontSize: 11 }}>{a.rationale ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Projected Effects */}
          {selected.projectedEffects.length > 0 && (
            <div className="scenario-section">
              <div className="scenario-section-label">Projected Effects</div>
              <div className="scenario-effects">
                {selected.projectedEffects.map((e, i) => (
                  <div key={i} className="scenario-effect-row">
                    <span className="scenario-effect-metric">{e.metric}{e.unit ? ` (${e.unit})` : ''}</span>
                    {e.baselineValue != null && e.projectedValue != null && (
                      <span className="scenario-effect-values">{e.baselineValue} → {e.projectedValue}</span>
                    )}
                    <span className={`scenario-effect-dir ${dirClass(e.direction)}`}>{e.direction}</span>
                    <span className="scenario-effect-conf">{e.confidence}</span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Links */}
          {(selected.linkedKpis.length > 0 || selected.linkedDecisions.length > 0 || selected.linkedEntities.length > 0) && (
            <div className="scenario-section">
              <div className="scenario-section-label">Linked Artifacts</div>
              <div className="scenario-links">
                {selected.linkedKpis.map((l, i) => <span key={`k${i}`} className="scenario-link-chip">KPI: {l.artifactId.slice(0, 8)}</span>)}
                {selected.linkedDecisions.map((l, i) => <span key={`d${i}`} className="scenario-link-chip">Decision: {l.artifactId.slice(0, 8)}</span>)}
                {selected.linkedEntities.map((l, i) => <span key={`e${i}`} className="scenario-link-chip">Entity: {l.artifactId.slice(0, 8)}</span>)}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Scenario list */}
      {loading ? (
        <div className="scenario-loading">Loading scenarios...</div>
      ) : scenarios.length === 0 ? (
        <div className="scenario-empty">No scenarios created yet</div>
      ) : (
        <div className="scenario-list">
          {scenarios.map((s) => (
            <div
              key={s.id}
              className={`scenario-card ${compareIds.has(s.id) ? 'selected' : ''}`}
              onClick={() => selectScenario(s.id)}
            >
              <div className="scenario-card-top">
                <input
                  type="checkbox"
                  checked={compareIds.has(s.id)}
                  onChange={(e) => { e.stopPropagation(); toggleCompare(s.id); }}
                  onClick={(e) => e.stopPropagation()}
                />
                <span className={`scenario-badge ${typeClass(s.type)}`}>{s.type}</span>
                <span className={`scenario-badge ${statusClass(s.status)}`}>{s.status}</span>
                <span className="scenario-card-title">{s.title}</span>
              </div>
              {s.description && <div className="scenario-card-desc">{s.description}</div>}
              <div className="scenario-card-meta">
                <span>{s.assumptions.length} assumptions</span>
                <span>{s.projectedEffects.length} effects</span>
                <span>{fmtDate(s.updatedAtUtc)}</span>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Create form */}
      {showForm && <CreateScenarioForm onClose={() => setShowForm(false)} onCreated={() => { setShowForm(false); load(); }} />}
    </div>
  );
}

function CreateScenarioForm({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [type, setType] = useState('WhatIf');
  const [assumptions, setAssumptions] = useState([{ name: '', currentValue: '', proposedValue: '', unit: '', rationale: '' }]);
  const [submitting, setSubmitting] = useState(false);

  const addAssumption = () => setAssumptions([...assumptions, { name: '', currentValue: '', proposedValue: '', unit: '', rationale: '' }]);

  const updateAssumption = (idx: number, field: string, value: string) => {
    setAssumptions(assumptions.map((a, i) => i === idx ? { ...a, [field]: value } : a));
  };

  const submit = async () => {
    if (!title.trim()) return;
    setSubmitting(true);
    try {
      const validAssumptions = assumptions.filter((a) => a.name.trim());
      await api.createScenario({
        title: title.trim(),
        description: description.trim() || undefined,
        type,
        assumptions: validAssumptions.length > 0 ? validAssumptions : undefined,
      });
      onCreated();
    } catch {
      /* ignore */
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="scenario-form-overlay" onClick={onClose}>
      <div className="scenario-form" onClick={(e) => e.stopPropagation()}>
        <h2>New Scenario</h2>

        <div className="scenario-form-group">
          <label className="scenario-form-label">Title</label>
          <input className="scenario-form-input" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g., Expand engineering team by 30%" />
        </div>

        <div className="scenario-form-group">
          <label className="scenario-form-label">Description</label>
          <textarea className="scenario-form-textarea" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="What change are you exploring?" />
        </div>

        <div className="scenario-form-group">
          <label className="scenario-form-label">Type</label>
          <select className="scenario-form-select" value={type} onChange={(e) => setType(e.target.value)}>
            {SCENARIO_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </div>

        <div className="scenario-form-group">
          <label className="scenario-form-label">Assumptions</label>
          {assumptions.map((a, i) => (
            <div key={i} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 6, marginBottom: 6 }}>
              <input className="scenario-form-input" placeholder="Name" value={a.name} onChange={(e) => updateAssumption(i, 'name', e.target.value)} />
              <input className="scenario-form-input" placeholder="Current" value={a.currentValue} onChange={(e) => updateAssumption(i, 'currentValue', e.target.value)} />
              <input className="scenario-form-input" placeholder="Proposed" value={a.proposedValue} onChange={(e) => updateAssumption(i, 'proposedValue', e.target.value)} />
            </div>
          ))}
          <button className="scenario-type-btn" onClick={addAssumption}>+ Add Assumption</button>
        </div>

        <div className="scenario-form-actions">
          <button className="scenario-form-cancel" onClick={onClose}>Cancel</button>
          <button className="scenario-form-submit" disabled={submitting || !title.trim()} onClick={submit}>
            {submitting ? 'Creating...' : 'Create Scenario'}
          </button>
        </div>
      </div>
    </div>
  );
}
