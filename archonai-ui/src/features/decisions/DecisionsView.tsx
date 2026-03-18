import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import './decisions.css';

interface Alternative {
  id: string;
  title: string;
  rationale: string;
  pros: string[];
  cons: string[];
  estimatedConfidence?: number;
  estimatedValue?: number;
}

interface DecisionLink {
  artifactType: string;
  artifactId: string;
  description: string;
  linkedAtUtc: string;
}

interface LifecycleEvent {
  id: string;
  decisionId: string;
  eventType: string;
  actor: string;
  detail: string | null;
  occurredAtUtc: string;
}

interface Decision {
  id: string;
  tenantId: string;
  title: string;
  domain: string;
  objective: string;
  constraints: string[];
  assumptions: string[];
  alternatives: Alternative[];
  recommendedOptionId: string;
  confidence: number;
  reversibility: string;
  riskLevel: string;
  expectedValue: number | null;
  requiresApproval: boolean;
  linkedArtifacts: DecisionLink[];
  status: string;
  createdBy: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

const STATUS_FILTERS = ['All', 'Draft', 'Proposed', 'UnderReview', 'Approved', 'Rejected', 'Executing', 'Completed'];

function statusClass(status: string): string {
  return status.toLowerCase().replace(/\s/g, '');
}

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

function pct(n: number): string {
  return `${Math.round(n * 100)}%`;
}

export function DecisionsView() {
  const [decisions, setDecisions] = useState<Decision[]>([]);
  const [selected, setSelected] = useState<Decision | null>(null);
  const [history, setHistory] = useState<LifecycleEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState('All');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const params: Record<string, string | number> = {};
      if (filter !== 'All') params.status = filter;
      const data = await api.listDecisions(params) as Decision[];
      setDecisions(data);
    } catch {
      setDecisions([]);
    } finally {
      setLoading(false);
    }
  }, [filter]);

  useEffect(() => { load(); }, [load]);

  const openDetail = async (d: Decision) => {
    setSelected(d);
    try {
      const h = await api.getDecisionHistory(d.id) as LifecycleEvent[];
      setHistory(h);
    } catch {
      setHistory([]);
    }
  };

  if (selected) {
    return (
      <div className="dec-detail">
        <header className="dec-detail-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 12 }}>
            <button className="dec-back" onClick={() => setSelected(null)} style={{ background: 'none', border: 'none', cursor: 'pointer' }}>
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
            </button>
            <h1 className="dec-detail-title">{selected.title}</h1>
          </div>
          <div className="dec-detail-meta">
            <span className={`dec-badge ${statusClass(selected.status)}`}>{selected.status}</span>
            <span className={`dec-badge ${selected.riskLevel.toLowerCase()}`}>{selected.riskLevel} risk</span>
            <span className="dec-badge">{selected.reversibility}</span>
            <span className="dec-confidence">Confidence: {pct(selected.confidence)}</span>
            {selected.expectedValue != null && (
              <span className="dec-confidence">EV: ${selected.expectedValue.toLocaleString()}</span>
            )}
          </div>
        </header>

        {selected.objective && (
          <div className="dec-section">
            <div className="dec-section-label">Objective</div>
            <p className="dec-objective">{selected.objective}</p>
          </div>
        )}

        {selected.constraints.length > 0 && (
          <div className="dec-section">
            <div className="dec-section-label">Constraints</div>
            <div className="dec-tags">{selected.constraints.map((c, i) => <span key={i} className="dec-tag">{c}</span>)}</div>
          </div>
        )}

        {selected.assumptions.length > 0 && (
          <div className="dec-section">
            <div className="dec-section-label">Assumptions</div>
            <div className="dec-tags">{selected.assumptions.map((a, i) => <span key={i} className="dec-tag">{a}</span>)}</div>
          </div>
        )}

        {selected.alternatives.length > 0 && (
          <div className="dec-section">
            <div className="dec-section-label">Alternatives Considered</div>
            <div className="dec-alt-list">
              {selected.alternatives.map((alt) => (
                <div key={alt.id} className={`dec-alt ${alt.id === selected.recommendedOptionId ? 'recommended' : ''}`}>
                  <div className="dec-alt-header">
                    <span className="dec-alt-title">{alt.title}</span>
                    {alt.id === selected.recommendedOptionId && <span className="dec-alt-rec">Recommended</span>}
                  </div>
                  <p className="dec-alt-rationale">{alt.rationale}</p>
                  {(alt.pros.length > 0 || alt.cons.length > 0) && (
                    <div className="dec-alt-pros-cons">
                      <div>
                        <div className="dec-alt-col-label pro">Pros</div>
                        {alt.pros.map((p, i) => <div key={i} className="dec-alt-item">+ {p}</div>)}
                      </div>
                      <div>
                        <div className="dec-alt-col-label con">Cons</div>
                        {alt.cons.map((c, i) => <div key={i} className="dec-alt-item">- {c}</div>)}
                      </div>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {selected.linkedArtifacts.length > 0 && (
          <div className="dec-section">
            <div className="dec-section-label">Linked Artifacts</div>
            <div className="dec-links">
              {selected.linkedArtifacts.map((link, i) => (
                <div key={i} className="dec-link-row">
                  <span className="dec-link-type">{link.artifactType}</span>
                  <span>{link.artifactId}</span>
                  <span style={{ color: 'var(--text-3)' }}>{link.description}</span>
                </div>
              ))}
            </div>
          </div>
        )}

        {history.length > 0 && (
          <div className="dec-section">
            <div className="dec-section-label">Lifecycle History</div>
            <div className="dec-history">
              {history.map((evt) => (
                <div key={evt.id} className="dec-history-event">
                  <span className="dec-history-time">{fmtDate(evt.occurredAtUtc)}</span>
                  <span className="dec-history-type">{evt.eventType}</span>
                  <span className="dec-history-detail">{evt.detail}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    );
  }

  return (
    <div className="dec-view">
      <header className="dec-header">
        <div className="dec-header-left">
          <Link to="/" className="dec-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
          </Link>
          <div>
            <h1 className="dec-title">Decisions</h1>
            <p className="dec-subtitle">Structured business decisions with rationale, alternatives, and risk</p>
          </div>
        </div>
      </header>

      <div className="dec-filters">
        {STATUS_FILTERS.map((s) => (
          <button
            key={s}
            className={`dec-filter-btn ${filter === s ? 'active' : ''}`}
            onClick={() => setFilter(s)}
          >
            {s === 'UnderReview' ? 'Under Review' : s}
          </button>
        ))}
      </div>

      {loading ? (
        <div className="dec-loading">Loading decisions...</div>
      ) : decisions.length === 0 ? (
        <div className="dec-list"><div className="dec-empty">No decisions found</div></div>
      ) : (
        <div className="dec-list">
          {decisions.map((d) => (
            <div key={d.id} className="dec-row" onClick={() => openDetail(d)}>
              <div>
                <p className="dec-row-title">{d.title}</p>
                <p className="dec-row-domain">{d.domain}</p>
              </div>
              <span className={`dec-badge ${statusClass(d.status)}`}>{d.status}</span>
              <span className={`dec-badge ${d.riskLevel.toLowerCase()}`}>{d.riskLevel}</span>
              <span className="dec-confidence">{pct(d.confidence)}</span>
              <span className="dec-confidence">{d.requiresApproval ? 'Yes' : 'No'}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
