import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import './trust-tiers.css';

interface TrustTierPolicy {
  id: string;
  tenantId: string;
  actionScope: string;
  maxTier: string;
  confidenceThreshold: number | null;
  valueCeiling: number | null;
  requireReversible: boolean;
  description: string | null;
  isEnabled: boolean;
  createdBy: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

const TIER_LABELS: Record<string, string> = {
  ObserveOnly: 'T0 — Observe Only',
  RecommendOnly: 'T1 — Recommend Only',
  DraftApprovalRequired: 'T2 — Draft + Approval',
  AutoExecuteReversible: 'T3 — Auto (Reversible)',
  AutoExecuteHighConfidence: 'T4 — Auto (High Conf.)',
  PolicyEnvelope: 'T5 — Policy Envelope',
};

const TIER_ORDER = [
  'ObserveOnly', 'RecommendOnly', 'DraftApprovalRequired',
  'AutoExecuteReversible', 'AutoExecuteHighConfidence', 'PolicyEnvelope',
];

function tierClass(tier: string): string {
  return `tier-${tier.toLowerCase().replace(/[^a-z]/g, '')}`;
}

function tierLevel(tier: string): number {
  return TIER_ORDER.indexOf(tier);
}

export function TrustTiersView() {
  const [policies, setPolicies] = useState<TrustTierPolicy[]>([]);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await api.listTrustTierPolicies() as TrustTierPolicy[];
      setPolicies(data);
    } catch {
      setPolicies([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  return (
    <div className="tt-view">
      <header className="tt-header">
        <div className="tt-header-left">
          <Link to="/" className="tt-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
          </Link>
          <div>
            <h1 className="tt-title">Trust-Tiered Autonomy</h1>
            <p className="tt-subtitle">Execution boundaries defining what the system may observe, recommend, draft, or execute</p>
          </div>
        </div>
      </header>

      <div className="tt-tier-legend">
        {TIER_ORDER.map((t) => (
          <div key={t} className={`tt-tier-chip ${tierClass(t)}`}>
            <span className="tt-tier-dot" />
            {TIER_LABELS[t]}
          </div>
        ))}
      </div>

      {loading ? (
        <div className="tt-loading">Loading policies...</div>
      ) : policies.length === 0 ? (
        <div className="tt-empty">No trust tier policies configured</div>
      ) : (
        <div className="tt-policy-list">
          <div className="tt-policy-header-row">
            <span>Action Scope</span>
            <span>Max Tier</span>
            <span>Confidence Gate</span>
            <span>Value Ceiling</span>
            <span>Reversible Only</span>
            <span>Source</span>
          </div>
          {policies.map((p) => (
            <div key={p.id} className="tt-policy-row">
              <div className="tt-scope">
                <span className="tt-scope-name">{p.actionScope}</span>
                {p.description && <span className="tt-scope-desc">{p.description}</span>}
              </div>
              <div>
                <span className={`tt-badge ${tierClass(p.maxTier)}`}>
                  T{tierLevel(p.maxTier)} {p.maxTier.replace(/([A-Z])/g, ' $1').trim()}
                </span>
              </div>
              <span className="tt-mono">{p.confidenceThreshold != null ? `${Math.round(p.confidenceThreshold * 100)}%` : '—'}</span>
              <span className="tt-mono">{p.valueCeiling != null ? `$${p.valueCeiling.toLocaleString()}` : '—'}</span>
              <span className="tt-mono">{p.requireReversible ? 'Yes' : 'No'}</span>
              <span className="tt-source">{p.tenantId === '__default__' ? 'System' : 'Tenant'}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
