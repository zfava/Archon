import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import './operational-twin.css';

interface TwinEntity {
  id: string;
  tenantId: string;
  entityType: string;
  name: string;
  description: string | null;
  status: string;
  properties: Record<string, string>;
  tags: string[];
  createdBy: string;
  updatedAtUtc: string;
}

interface TwinBottleneck {
  id: string;
  affectedEntityId: string;
  description: string;
  severity: string;
  rootCause: string | null;
  isResolved: boolean;
  detectedAtUtc: string;
}

interface TwinKpi {
  entityId: string;
  metricName: string;
  currentValue: number;
  targetValue: number | null;
  thresholdWarning: number | null;
  thresholdCritical: number | null;
  direction: string;
  unit: string;
}

interface TwinOverview {
  entityCounts: Record<string, number>;
  activeBottlenecks: TwinBottleneck[];
  warningKpis: TwinKpi[];
  totalDependencies: number;
}

const ENTITY_TYPES = ['Team', 'Function', 'System', 'Integration', 'Workflow', 'Kpi', 'Objective'] as const;

function severityClass(severity: string): string {
  return `sev-${severity.toLowerCase()}`;
}

function statusClass(status: string): string {
  return `twin-status-${status.toLowerCase()}`;
}

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

export function OperationalTwinView() {
  const [overview, setOverview] = useState<TwinOverview | null>(null);
  const [entities, setEntities] = useState<TwinEntity[]>([]);
  const [filterType, setFilterType] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const params: Record<string, string> = {};
      if (filterType) params.type = filterType;
      const [ov, ents] = await Promise.all([
        api.getTwinOverview() as Promise<TwinOverview>,
        api.listTwinEntities(params) as Promise<TwinEntity[]>,
      ]);
      setOverview(ov);
      setEntities(ents);
    } catch {
      setOverview(null);
      setEntities([]);
    } finally {
      setLoading(false);
    }
  }, [filterType]);

  useEffect(() => { load(); }, [load]);

  return (
    <div className="twin-view">
      <header className="twin-header">
        <div className="twin-header-left">
          <Link to="/" className="twin-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
          </Link>
          <div>
            <h1 className="twin-title">Operational Twin</h1>
            <p className="twin-subtitle">Living model of the business: teams, systems, workflows, KPIs, dependencies, and bottlenecks</p>
          </div>
        </div>
      </header>

      {/* Overview cards */}
      {overview && (
        <div className="twin-overview">
          {Object.entries(overview.entityCounts).map(([type, count]) => (
            <div key={type} className="twin-stat-card" onClick={() => setFilterType(filterType === type ? null : type)}>
              <div className="twin-stat-value">{count}</div>
              <div className="twin-stat-label">{type}s</div>
            </div>
          ))}
          <div className="twin-stat-card">
            <div className="twin-stat-value">{overview.totalDependencies}</div>
            <div className="twin-stat-label">Dependencies</div>
          </div>
          <div className={`twin-stat-card ${overview.activeBottlenecks.length > 0 ? 'twin-stat-warn' : ''}`}>
            <div className="twin-stat-value">{overview.activeBottlenecks.length}</div>
            <div className="twin-stat-label">Bottlenecks</div>
          </div>
          <div className={`twin-stat-card ${overview.warningKpis.length > 0 ? 'twin-stat-warn' : ''}`}>
            <div className="twin-stat-value">{overview.warningKpis.length}</div>
            <div className="twin-stat-label">KPI Warnings</div>
          </div>
        </div>
      )}

      {/* Bottlenecks */}
      {overview && overview.activeBottlenecks.length > 0 && (
        <div className="twin-section">
          <div className="twin-section-label">Active Bottlenecks</div>
          <div className="twin-bottleneck-list">
            {overview.activeBottlenecks.map((bn) => (
              <div key={bn.id} className={`twin-bottleneck ${severityClass(bn.severity)}`}>
                <span className={`twin-badge ${severityClass(bn.severity)}`}>{bn.severity}</span>
                <span className="twin-bn-desc">{bn.description}</span>
                {bn.rootCause && <span className="twin-bn-cause">{bn.rootCause}</span>}
                <span className="twin-bn-time">{fmtDate(bn.detectedAtUtc)}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* KPI Warnings */}
      {overview && overview.warningKpis.length > 0 && (
        <div className="twin-section">
          <div className="twin-section-label">KPI Warnings</div>
          <div className="twin-kpi-list">
            {overview.warningKpis.map((k, i) => (
              <div key={i} className="twin-kpi-row">
                <span className="twin-kpi-name">{k.metricName}</span>
                <span className="twin-kpi-value">{k.currentValue} {k.unit}</span>
                {k.targetValue != null && <span className="twin-kpi-target">target: {k.targetValue}</span>}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Entity type filter */}
      <div className="twin-type-bar">
        <button className={`twin-type-btn ${filterType === null ? 'active' : ''}`} onClick={() => setFilterType(null)}>All</button>
        {ENTITY_TYPES.map((t) => (
          <button key={t} className={`twin-type-btn ${filterType === t ? 'active' : ''}`} onClick={() => setFilterType(filterType === t ? null : t)}>{t}</button>
        ))}
      </div>

      {/* Entity list */}
      {loading ? (
        <div className="twin-loading">Loading twin...</div>
      ) : entities.length === 0 ? (
        <div className="twin-empty">No entities in the operational twin</div>
      ) : (
        <div className="twin-entity-list">
          {entities.map((e) => (
            <div key={e.id} className="twin-entity">
              <div className="twin-entity-top">
                <span className="twin-badge twin-type-badge">{e.entityType}</span>
                <span className={`twin-badge ${statusClass(e.status)}`}>{e.status}</span>
                <span className="twin-entity-name">{e.name}</span>
              </div>
              {e.description && <div className="twin-entity-desc">{e.description}</div>}
              {e.tags.length > 0 && (
                <div className="twin-entity-tags">
                  {e.tags.map((t, i) => <span key={i} className="twin-tag">{t}</span>)}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
