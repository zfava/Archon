import type { WorkflowFailureDiagnostics } from '../types';

interface Props {
  diagnostics: WorkflowFailureDiagnostics;
}

export function WorkflowDiagnosticsCard({ diagnostics }: Props) {
  return (
    <div className="ins-workflow-diagnostics">
      <section className="ins-card">
        <div className="ins-card-header">
          <span className="ins-section-label">Workflow Diagnostics</span>
          <span className={`ins-badge ins-badge--${diagnostics.failureCategory}`}>
            {diagnostics.failureCategory}
          </span>
        </div>

        <h2 className="ins-decision-title">{diagnostics.workflowName}</h2>
        <div className="ins-rationale">{diagnostics.failureReason}</div>

        <div className="ins-meta-row">
          <span className="ins-meta-item">
            State: <strong>{diagnostics.currentState}</strong>
          </span>
          <span className="ins-meta-item">
            Retryable: <strong>{diagnostics.isRetryable ? 'Yes' : 'No'}</strong>
          </span>
          {diagnostics.failedStepName && (
            <span className="ins-meta-item">
              Failed Step: <strong>{diagnostics.failedStepName}</strong>
            </span>
          )}
        </div>

        {diagnostics.suggestedRemediation && (
          <>
            <span className="ins-sub-label">Suggested Remediation</span>
            <div className="ins-remediation">{diagnostics.suggestedRemediation}</div>
          </>
        )}
      </section>

      <section className="ins-card">
        <span className="ins-section-label">Step Diagnostics</span>
        <div className="ins-steps">
          {diagnostics.stepDiagnostics.map((step) => (
            <div
              key={step.stepIndex}
              className={`ins-step-row ${
                step.status === 'Failed' ? 'ins-step-row--failed' :
                step.status === 'Completed' ? 'ins-step-row--completed' : ''
              }`}
            >
              <div className="ins-step-header">
                <span className="ins-step-index">#{step.stepIndex + 1}</span>
                <span className="ins-step-name">{step.stepName}</span>
                <span className={`ins-step-status ins-step-status--${step.status.toLowerCase()}`}>
                  {step.status}
                </span>
              </div>
              <div className="ins-step-meta">
                <span>Agent: {step.agentType}</span>
                {step.durationMs != null && <span>{step.durationMs.toFixed(0)}ms</span>}
              </div>
              {step.errorMessage && (
                <div className="ins-step-error">{step.errorMessage}</div>
              )}
            </div>
          ))}
        </div>
      </section>

      {diagnostics.relatedExceptions.length > 0 && (
        <section className="ins-card">
          <span className="ins-section-label">Related Exceptions</span>
          <div className="ins-artifacts">
            {diagnostics.relatedExceptions.map((ex, i) => (
              <div key={i} className="ins-artifact-row">
                <span className="ins-artifact-type">{ex.artifactType}</span>
                <span className="ins-artifact-id">{ex.artifactId}</span>
                <span className="ins-artifact-desc">{ex.description}</span>
              </div>
            ))}
          </div>
        </section>
      )}
    </div>
  );
}
