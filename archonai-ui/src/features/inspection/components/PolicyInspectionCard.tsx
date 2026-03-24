import type { PolicyEvaluationResult } from '../types';

interface Props {
  evaluation: PolicyEvaluationResult;
}

export function PolicyInspectionCard({ evaluation }: Props) {
  return (
    <section className="ins-card">
      <div className="ins-card-header">
        <span className="ins-section-label">Policy Evaluation</span>
        <span className={`ins-badge ${evaluation.isAllowed ? 'ins-badge--allowed' : 'ins-badge--denied'}`}>
          {evaluation.isAllowed ? 'Allowed' : 'Denied'}
        </span>
      </div>

      <div className="ins-rationale">{evaluation.reason}</div>

      <div className="ins-meta-row">
        <span className="ins-meta-item">
          Risk: <strong>{evaluation.riskScore.toFixed(1)}</strong>
        </span>
        <span className="ins-meta-item">
          Confidence: <strong>{(evaluation.confidenceScore * 100).toFixed(0)}%</strong>
        </span>
        <span className="ins-meta-item">
          Approval: <strong>{evaluation.approvalState}</strong>
        </span>
        {evaluation.manualOverrideState !== 'none' && (
          <span className="ins-meta-item">
            Override: <strong>{evaluation.manualOverrideState}</strong>
          </span>
        )}
      </div>

      {evaluation.guardrailViolations.length > 0 && (
        <>
          <span className="ins-sub-label">Guardrail Violations</span>
          <div className="ins-violations">
            {evaluation.guardrailViolations.map((v, i) => (
              <span key={i} className="ins-violation-tag">{v}</span>
            ))}
          </div>
        </>
      )}

      <span className="ins-sub-label">Rules Evaluated</span>
      <div className="ins-rules">
        {evaluation.rulesEvaluated.map((rule, i) => (
          <div key={i} className={`ins-rule-row ${rule.passed ? '' : 'ins-rule-row--failed'}`}>
            <div className="ins-rule-header">
              <span className="ins-rule-name">{rule.ruleName}</span>
              <span className={`ins-rule-badge ${rule.passed ? 'ins-rule-badge--pass' : 'ins-rule-badge--fail'}`}>
                {rule.passed ? 'Pass' : 'Fail'}
              </span>
            </div>
            <p className="ins-rule-detail">{rule.detail}</p>
            {rule.riskContribution > 0 && (
              <span className="ins-rule-risk">+{rule.riskContribution.toFixed(1)} risk</span>
            )}
          </div>
        ))}
      </div>
    </section>
  );
}
