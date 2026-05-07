import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import './memory.css';

interface MemoryEntityLink {
  entityType: string;
  entityId: string;
  relationship: string;
}

interface EnterpriseMemoryRecord {
  id: string;
  tenantId: string;
  layer: string;
  category: string;
  subject: string;
  content: string;
  metadata: Record<string, string>;
  linkedEntities: MemoryEntityLink[];
  tags: string[];
  importance: number;
  createdBy: string;
  createdAtUtc: string;
  expiresAtUtc: string | null;
}

interface MemoryQueryResult {
  records: EnterpriseMemoryRecord[];
  totalCount: number;
  layerCounts: Record<string, number>;
}

const LAYERS = ['Session', 'Operational', 'Organizational', 'Strategic', 'Relational', 'Financial'] as const;

const LAYER_DESCRIPTIONS: Record<string, string> = {
  Session: 'Ephemeral session context',
  Operational: 'Active workflows & task state',
  Organizational: 'Institutional knowledge & processes',
  Strategic: 'Goals, strategy & market intelligence',
  Relational: 'Customer, vendor & team relationships',
  Financial: 'Budgets, forecasts & consequences',
};

function layerClass(layer: string): string {
  return `mem-layer-${layer.toLowerCase()}`;
}

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

export function MemoryExplorerView() {
  const [result, setResult] = useState<MemoryQueryResult | null>(null);
  const [selectedLayer, setSelectedLayer] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const params: Record<string, string | number> = {};
      if (selectedLayer) params.layer = selectedLayer;
      const data = await api.queryMemory(params) as MemoryQueryResult;
      setResult(data);
    } catch {
      setResult(null);
    } finally {
      setLoading(false);
    }
  }, [selectedLayer]);

  useEffect(() => { load(); }, [load]);

  return (
    <div className="mem-view">
      <header className="mem-header">
        <div className="mem-header-left">
          <Link to="/" className="mem-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
          </Link>
          <div>
            <h1 className="mem-title">Enterprise Memory</h1>
            <p className="mem-subtitle">Layered knowledge hierarchy across session, operational, organizational, strategic, relational, and financial domains</p>
          </div>
        </div>
      </header>

      {/* Layer facets */}
      <div className="mem-layer-bar">
        <button
          className={`mem-layer-btn ${selectedLayer === null ? 'active' : ''}`}
          onClick={() => setSelectedLayer(null)}
        >
          All{result ? ` (${result.totalCount})` : ''}
        </button>
        {LAYERS.map((l) => (
          <button
            key={l}
            className={`mem-layer-btn ${layerClass(l)} ${selectedLayer === l ? 'active' : ''}`}
            onClick={() => setSelectedLayer(selectedLayer === l ? null : l)}
          >
            <span className="mem-layer-dot" />
            {l}
            {result?.layerCounts[l] != null && <span className="mem-layer-count">{result.layerCounts[l]}</span>}
          </button>
        ))}
      </div>

      {/* Layer descriptions */}
      {selectedLayer && (
        <div className={`mem-layer-desc ${layerClass(selectedLayer)}`}>
          {LAYER_DESCRIPTIONS[selectedLayer]}
        </div>
      )}

      {loading ? (
        <div className="mem-loading">Loading memory...</div>
      ) : !result || result.records.length === 0 ? (
        <div className="mem-empty">No memory records found</div>
      ) : (
        <div className="mem-record-list">
          {result.records.map((r) => (
            <div key={r.id} className="mem-record">
              <div className="mem-record-top">
                <span className={`mem-badge ${layerClass(r.layer)}`}>{r.layer}</span>
                {r.category && <span className="mem-category">{r.category}</span>}
                <span className="mem-time">{fmtDate(r.createdAtUtc)}</span>
                {r.expiresAtUtc && <span className="mem-expires">expires {fmtDate(r.expiresAtUtc)}</span>}
              </div>
              <div className="mem-subject">{r.subject}</div>
              <div className="mem-content">{r.content}</div>
              {r.tags.length > 0 && (
                <div className="mem-tags">
                  {r.tags.map((t, i) => <span key={i} className="mem-tag">{t}</span>)}
                </div>
              )}
              {r.linkedEntities.length > 0 && (
                <div className="mem-links">
                  {r.linkedEntities.map((e, i) => (
                    <span key={i} className="mem-link">
                      {e.relationship}: {e.entityType}/{e.entityId}
                    </span>
                  ))}
                </div>
              )}
              <div className="mem-record-footer">
                <span className="mem-importance">importance: {(r.importance * 100).toFixed(0)}%</span>
                <span className="mem-author">by {r.createdBy}</span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
