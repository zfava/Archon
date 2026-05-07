import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import './exception-intelligence.css';

interface ArtifactLink {
  artifactType: string;
  artifactId: string;
  label: string | null;
}

interface RecAction {
  actionType: string;
  description: string;
  targetArtifactType: string | null;
  targetArtifactId: string | null;
  confidence: string;
}

interface OpException {
  id: string;
  tenantId: string;
  category: string;
  severity: string;
  title: string;
  description: string;
  domain: string;
  status: string;
  urgency: number;
  economicImpactEstimate: number;
  confidence: number;
  escalationLevel: string;
  assignedTo: string | null;
  escalationPath: string | null;
  linkedArtifacts: ArtifactLink[];
  recommendedAction: RecAction | null;
  createdBy: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  acknowledgedAtUtc: string | null;
  resolvedAtUtc: string | null;
}

interface QueueSummary {
  totalOpen: number;
  critical: number;
  high: number;
  warning: number;
  totalEconomicExposure: number;
  byCategory: Record<string, number>;
  generatedAtUtc: string;
}

interface PriorityScore {
  exceptionId: string;
  score: number;
  breakdown: string;
}

const SEVERITY_FILTERS = ['Critical', 'High', 'Warning', 'Info'] as const;
const STATUS_FILTERS = ['Open', 'Acknowledged', 'InProgress', 'Resolved', 'Dismissed'] as const;

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

function fmtCurrency(n: number): string {
  if (n >= 1_000_000) return `$${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `$${(n / 1_000).toFixed(0)}K`;
  return `$${n.toFixed(0)}`;
}

function sevClass(s: string): string { return `exc-sev-${s.toLowerCase()}`; }
function catClass(s: string): string { return `exc-cat-${s.toLowerCase()}`; }
function statusClass(s: string): string { return `exc-status-${s.toLowerCase()}`; }

export function ExceptionIntelligenceView() {
  const [exceptions, setExceptions] = useState<OpException[]>([]);
  const [summary, setSummary] = useState<QueueSummary | null>(null);
  const [prioritized, setPrioritized] = useState<PriorityScore[]>([]);
  const [filterSev, setFilterSev] = useState<string | null>(null);
  const [filterStatus, setFilterStatus] = useState<string | null>(null);
  const [selected, setSelected] = useState<OpException | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const params: Record<string, string> = {};
      if (filterSev) params.severity = filterSev;
      if (filterStatus) params.status = filterStatus;
      const [list, sum, prio] = await Promise.all([
        api.listExceptions(params) as Promise<OpException[]>,
        api.getExceptionSummary() as Promise<QueueSummary>,
        api.getExceptionPrioritized(20) as Promise<PriorityScore[]>,
      ]);
      setExceptions(list);
      setSummary(sum);
      setPrioritized(prio);
    } catch {
      setExceptions([]);
    } finally {
      setLoading(false);
    }
  }, [filterSev, filterStatus]);

  useEffect(() => { load(); }, [load]);

  const scoreMap = new Map(prioritized.map(p => [p.exceptionId, p.score]));

  const selectException = async (id: string) => {
    try {
      const ex = await api.getException(id) as OpException;
      setSelected(ex);
    } catch { /* ignore */ }
  };

  const updateStatus = async (id: string, status: string) => {
    try {
      await api.updateExceptionStatus(id, { status });
      await load();
      if (selected?.id === id) {
        const ex = await api.getException(id) as OpException;
        setSelected(ex);
      }
    } catch { /* ignore */ }
  };

  return (
    <div className="exc-view">
      <header className="exc-header">
        <div className="exc-header-left">
          <Link to="/" className="exc-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
          </Link>
          <div>
            <h1 className="exc-title">Exception Intelligence</h1>
            <p className="exc-subtitle">AI-scored anomalies ranked by economic exposure and urgency</p>
          </div>
        </div>
      </header>

      {/* Summary cards */}
      {summary && (
        <div className="exc-summary">
          <div className="exc-stat-card">
            <div className="exc-stat-value">{summary.totalOpen}</div>
            <div className="exc-stat-label">Open</div>
          </div>
          <div className={`exc-stat-card ${summary.critical > 0 ? 'exc-stat-critical' : ''}`}>
            <div className="exc-stat-value">{summary.critical}</div>
            <div className="exc-stat-label">Critical</div>
          </div>
          <div className={`exc-stat-card ${summary.high > 0 ? 'exc-stat-high' : ''}`}>
            <div className="exc-stat-value">{summary.high}</div>
            <div className="exc-stat-label">High</div>
          </div>
          <div className={`exc-stat-card ${summary.warning > 0 ? 'exc-stat-warn' : ''}`}>
            <div className="exc-stat-value">{summary.warning}</div>
            <div className="exc-stat-label">Warning</div>
          </div>
          <div className="exc-stat-card exc-stat-exposure">
            <div className="exc-stat-value">{fmtCurrency(summary.totalEconomicExposure)}</div>
            <div className="exc-stat-label">Exposure</div>
          </div>
        </div>
      )}

      {/* Filter bars */}
      <div className="exc-filter-bar">
        <span style={{ fontFamily: 'var(--mono)', fontSize: 10, color: 'var(--text-3)', alignSelf: 'center', marginRight: 4 }}>SEVERITY:</span>
        <button className={`exc-filter-btn ${filterSev === null ? 'active' : ''}`} onClick={() => setFilterSev(null)}>All</button>
        {SEVERITY_FILTERS.map(s => (
          <button key={s} className={`exc-filter-btn ${filterSev === s ? 'active' : ''}`} onClick={() => setFilterSev(filterSev === s ? null : s)}>{s}</button>
        ))}
      </div>
      <div className="exc-filter-bar">
        <span style={{ fontFamily: 'var(--mono)', fontSize: 10, color: 'var(--text-3)', alignSelf: 'center', marginRight: 4 }}>STATUS:</span>
        <button className={`exc-filter-btn ${filterStatus === null ? 'active' : ''}`} onClick={() => setFilterStatus(null)}>All</button>
        {STATUS_FILTERS.map(s => (
          <button key={s} className={`exc-filter-btn ${filterStatus === s ? 'active' : ''}`} onClick={() => setFilterStatus(filterStatus === s ? null : s)}>{s}</button>
        ))}
      </div>

      {/* Detail panel */}
      {selected && (
        <div className="exc-detail">
          <div className="exc-detail-header">
            <span className={`exc-badge ${sevClass(selected.severity)}`}>{selected.severity}</span>
            <span className={`exc-badge ${catClass(selected.category)}`}>{selected.category}</span>
            <span className={`exc-badge ${statusClass(selected.status)}`}>{selected.status}</span>
            {selected.escalationLevel !== 'None' && (
              <span className="exc-badge exc-sev-high">{selected.escalationLevel}</span>
            )}
            <button className="exc-filter-btn" onClick={() => setSelected(null)} style={{ marginLeft: 'auto' }}>Close</button>
          </div>
          <div className="exc-detail-title">{selected.title}</div>
          <div className="exc-detail-desc">{selected.description}</div>

          {/* Scores */}
          <div className="exc-section">
            <div className="exc-section-label">Scoring</div>
            <div className="exc-scores">
              <div className="exc-score-item">
                <div className="exc-score-val">{selected.urgency.toFixed(1)}</div>
                <div className="exc-score-lbl">Urgency</div>
              </div>
              <div className="exc-score-item">
                <div className="exc-score-val">{fmtCurrency(selected.economicImpactEstimate)}</div>
                <div className="exc-score-lbl">Econ Impact</div>
              </div>
              <div className="exc-score-item">
                <div className="exc-score-val">{(selected.confidence * 100).toFixed(0)}%</div>
                <div className="exc-score-lbl">Confidence</div>
              </div>
              {scoreMap.has(selected.id) && (
                <div className="exc-score-item">
                  <div className="exc-score-val" style={{ color: 'var(--purple)' }}>{scoreMap.get(selected.id)!.toFixed(1)}</div>
                  <div className="exc-score-lbl">Priority</div>
                </div>
              )}
            </div>
          </div>

          {/* Recommended action */}
          {selected.recommendedAction && (
            <div className="exc-section">
              <div className="exc-section-label">Recommended Action</div>
              <div className="exc-action">
                <div className="exc-action-type">{selected.recommendedAction.actionType} — {selected.recommendedAction.confidence}</div>
                <div className="exc-action-desc">{selected.recommendedAction.description}</div>
                {selected.recommendedAction.targetArtifactType && (
                  <div className="exc-action-target">
                    Target: {selected.recommendedAction.targetArtifactType} {selected.recommendedAction.targetArtifactId?.slice(0, 8)}
                  </div>
                )}
              </div>
            </div>
          )}

          {/* Linked artifacts */}
          {selected.linkedArtifacts.length > 0 && (
            <div className="exc-section">
              <div className="exc-section-label">Linked Artifacts</div>
              <div className="exc-links">
                {selected.linkedArtifacts.map((l, i) => (
                  <span key={i} className="exc-link-chip">
                    {l.artifactType}: {l.label ?? l.artifactId.slice(0, 8)}
                  </span>
                ))}
              </div>
            </div>
          )}

          {/* Meta */}
          <div className="exc-card-meta" style={{ marginTop: 12 }}>
            {selected.assignedTo && <span>Assigned: {selected.assignedTo}</span>}
            {selected.escalationPath && <span>Escalation: {selected.escalationPath}</span>}
            <span>Domain: {selected.domain}</span>
            <span>Created: {fmtDate(selected.createdAtUtc)}</span>
            {selected.acknowledgedAtUtc && <span>Ack: {fmtDate(selected.acknowledgedAtUtc)}</span>}
            {selected.resolvedAtUtc && <span>Resolved: {fmtDate(selected.resolvedAtUtc)}</span>}
          </div>

          {/* Status actions */}
          {selected.status !== 'Resolved' && selected.status !== 'Dismissed' && (
            <div className="exc-status-actions">
              {selected.status === 'Open' && (
                <button className="exc-action-btn" onClick={() => updateStatus(selected.id, 'Acknowledged')}>Acknowledge</button>
              )}
              {(selected.status === 'Open' || selected.status === 'Acknowledged') && (
                <button className="exc-action-btn" onClick={() => updateStatus(selected.id, 'InProgress')}>Start Work</button>
              )}
              <button className="exc-action-btn" onClick={() => updateStatus(selected.id, 'Resolved')}>Resolve</button>
              <button className="exc-action-btn" onClick={() => updateStatus(selected.id, 'Dismissed')}>Dismiss</button>
            </div>
          )}
        </div>
      )}

      {/* Exception queue */}
      {loading ? (
        <div className="exc-loading">Loading exceptions...</div>
      ) : exceptions.length === 0 ? (
        <div className="exc-empty">No exceptions in the queue</div>
      ) : (
        <div className="exc-list">
          {exceptions.map((ex) => (
            <div key={ex.id} className="exc-card" onClick={() => selectException(ex.id)}>
              <div className="exc-card-top">
                <span className={`exc-badge ${sevClass(ex.severity)}`}>{ex.severity}</span>
                <span className={`exc-badge ${catClass(ex.category)}`}>{ex.category}</span>
                <span className={`exc-badge ${statusClass(ex.status)}`}>{ex.status}</span>
                <span className="exc-card-title">{ex.title}</span>
                {scoreMap.has(ex.id) && (
                  <span className="exc-card-score">{scoreMap.get(ex.id)!.toFixed(1)}</span>
                )}
              </div>
              {ex.description && <div className="exc-card-desc">{ex.description}</div>}
              <div className="exc-card-meta">
                <span>{ex.domain}</span>
                <span>urgency {ex.urgency.toFixed(1)}</span>
                {ex.economicImpactEstimate > 0 && <span>{fmtCurrency(ex.economicImpactEstimate)}</span>}
                {ex.assignedTo && <span>→ {ex.assignedTo}</span>}
                <span>{fmtDate(ex.updatedAtUtc)}</span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
