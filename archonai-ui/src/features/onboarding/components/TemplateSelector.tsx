import type { OnboardingTemplate } from '../types';
import { TEMPLATES } from '../templates';

interface Props {
  selected: OnboardingTemplate | null;
  onSelect: (template: OnboardingTemplate) => void;
  onSkip: () => void;
  onAutoDeploy: (template: OnboardingTemplate) => void;
  deploying: boolean;
}

export function TemplateSelector({ selected, onSelect, onSkip, onAutoDeploy, deploying }: Props) {
  return (
    <div className="ob-step-content">
      <div className="ob-step-intro">
        <h2 className="ob-step-title">Start with a template</h2>
        <p className="ob-step-desc">
          Choose a prebuilt setup for your industry. Templates come with agents, workflows,
          and strategies ready to deploy — or skip to configure manually.
        </p>
      </div>

      <div className="ob-tpl-grid">
        {TEMPLATES.map(tpl => (
          <button
            key={tpl.id}
            className={`ob-tpl-card ${selected?.id === tpl.id ? 'ob-tpl-card--active' : ''}`}
            onClick={() => onSelect(tpl)}
            disabled={deploying}
          >
            <div className="ob-tpl-header">
              <span className="ob-tpl-name">{tpl.name}</span>
              <span className="ob-tpl-industry">{tpl.industry}</span>
            </div>
            <p className="ob-tpl-desc">{tpl.description}</p>

            {/* Agents */}
            <div className="ob-tpl-section">
              <span className="ob-tpl-section-label">Agents</span>
              <div className="ob-tpl-chips">
                {tpl.agents.map(a => (
                  <span key={a.name} className="ob-agent-chip" title={a.role}>{a.name}</span>
                ))}
              </div>
            </div>

            {/* Workflows */}
            <div className="ob-tpl-section">
              <span className="ob-tpl-section-label">Workflows</span>
              <div className="ob-tpl-workflows">
                {tpl.workflows.map(w => (
                  <div key={w.name} className="ob-tpl-workflow">
                    <span className="ob-tpl-wf-name">{w.name}</span>
                    <div className="ob-tpl-wf-steps">
                      {w.steps.map((step, i) => (
                        <span key={i} className="ob-tpl-wf-step">
                          {i > 0 && <span className="ob-tpl-wf-arrow">&rarr;</span>}
                          {step}
                        </span>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Strategies */}
            <div className="ob-tpl-section">
              <span className="ob-tpl-section-label">Strategies</span>
              <div className="ob-tpl-chips">
                {tpl.strategies.map(s => (
                  <span key={s} className="ob-strategy-chip">{s}</span>
                ))}
              </div>
            </div>

            {/* Footer meta */}
            <div className="ob-tpl-meta">
              <span className="ob-tpl-meta-item">&lt; {tpl.estimatedMinutes} min setup</span>
              <span className="ob-tpl-meta-item">{tpl.departments.length} departments</span>
            </div>
          </button>
        ))}
      </div>

      {/* Actions */}
      <div className="ob-tpl-actions">
        {selected && (
          <button
            className="ob-deploy-btn"
            onClick={() => onAutoDeploy(selected)}
            disabled={deploying}
          >
            {deploying ? (
              <>
                <div className="phase-spinner" />
                Deploying {selected.name}...
              </>
            ) : (
              <>Deploy {selected.name}</>
            )}
          </button>
        )}
        <button
          className="ob-tpl-skip-btn"
          onClick={onSkip}
          disabled={deploying}
        >
          Configure manually instead
        </button>
      </div>
    </div>
  );
}
