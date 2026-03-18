import type { DecisionRationaleBundle } from '../types';
import { PolicyInspectionCard } from './PolicyInspectionCard';
import { MemoryReferencesCard } from './MemoryReferencesCard';

interface Props {
  bundle: DecisionRationaleBundle;
}

export function RationaleInspectionCard({ bundle }: Props) {
  return (
    <div className="ins-rationale-bundle">
      <section className="ins-card">
        <span className="ins-section-label">Decision Rationale</span>
        <h2 className="ins-decision-title">{bundle.title}</h2>
        <div className="ins-rationale">{bundle.recommendationRationale}</div>

        <div className="ins-meta-row">
          <span className="ins-meta-item">
            Domain: <strong>{bundle.domain}</strong>
          </span>
          <span className="ins-meta-item">
            Confidence: <strong>{(bundle.confidence * 100).toFixed(0)}%</strong>
          </span>
          <span className="ins-meta-item">
            Risk: <strong>{bundle.riskLevel}</strong>
          </span>
          <span className="ins-meta-item">
            Reversibility: <strong>{bundle.reversibility}</strong>
          </span>
        </div>

        {bundle.objective && (
          <>
            <span className="ins-sub-label">Objective</span>
            <p className="ins-text">{bundle.objective}</p>
          </>
        )}

        {bundle.assumptions.length > 0 && (
          <>
            <span className="ins-sub-label">Assumptions</span>
            <ul className="ins-list">
              {bundle.assumptions.map((a, i) => (
                <li key={i}>{a}</li>
              ))}
            </ul>
          </>
        )}

        {bundle.constraints.length > 0 && (
          <>
            <span className="ins-sub-label">Constraints</span>
            <ul className="ins-list">
              {bundle.constraints.map((c, i) => (
                <li key={i}>{c}</li>
              ))}
            </ul>
          </>
        )}
      </section>

      {bundle.alternatives.length > 0 && (
        <section className="ins-card">
          <span className="ins-section-label">Alternatives Considered</span>
          <div className="ins-alt-list">
            {bundle.alternatives.map((alt) => (
              <div key={alt.id} className={`ins-alt-row ${alt.isRecommended ? 'ins-alt-row--recommended' : ''}`}>
                <div className="ins-alt-header">
                  <span className="ins-alt-name">
                    {alt.title}
                    {alt.isRecommended && <span className="ins-badge ins-badge--recommended">Recommended</span>}
                  </span>
                  {alt.estimatedConfidence != null && (
                    <span className="ins-alt-score">{(alt.estimatedConfidence * 100).toFixed(0)}%</span>
                  )}
                </div>
                <p className="ins-alt-reason">{alt.rationale}</p>
                {(alt.pros.length > 0 || alt.cons.length > 0) && (
                  <div className="ins-alt-proscons">
                    {alt.pros.length > 0 && (
                      <div className="ins-pros">
                        {alt.pros.map((p, i) => (
                          <span key={i} className="ins-pro-tag">+ {p}</span>
                        ))}
                      </div>
                    )}
                    {alt.cons.length > 0 && (
                      <div className="ins-cons">
                        {alt.cons.map((c, i) => (
                          <span key={i} className="ins-con-tag">- {c}</span>
                        ))}
                      </div>
                    )}
                  </div>
                )}
              </div>
            ))}
          </div>
        </section>
      )}

      {bundle.policyEvaluation && <PolicyInspectionCard evaluation={bundle.policyEvaluation} />}

      {bundle.memoryReferences.length > 0 && <MemoryReferencesCard references={bundle.memoryReferences} />}

      {bundle.changeHistory.length > 0 && (
        <section className="ins-card">
          <span className="ins-section-label">Change History</span>
          <div className="ins-timeline">
            {bundle.changeHistory.map((evt, i) => (
              <div key={i} className="ins-timeline-item">
                <span className="ins-timeline-type">{evt.changeType}</span>
                <span className="ins-timeline-detail">{evt.reason}</span>
                <span className="ins-timeline-actor">{evt.actor}</span>
                <span className="ins-timeline-time">
                  {new Date(evt.occurredAtUtc).toLocaleString()}
                </span>
              </div>
            ))}
          </div>
        </section>
      )}

      {bundle.linkedArtifacts.length > 0 && (
        <section className="ins-card">
          <span className="ins-section-label">Linked Artifacts</span>
          <div className="ins-artifacts">
            {bundle.linkedArtifacts.map((a, i) => (
              <div key={i} className="ins-artifact-row">
                <span className="ins-artifact-type">{a.artifactType}</span>
                <span className="ins-artifact-id">{a.artifactId}</span>
                <span className="ins-artifact-desc">{a.description}</span>
              </div>
            ))}
          </div>
        </section>
      )}
    </div>
  );
}
