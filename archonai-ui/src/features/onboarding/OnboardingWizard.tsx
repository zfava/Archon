import { Link } from 'react-router-dom';
import { useOnboardingWizard } from './hooks/useOnboardingWizard';
import { ConnectSystems } from './components/ConnectSystems';
import { SelectBusinessType } from './components/SelectBusinessType';
import { ConfigureAutomation } from './components/ConfigureAutomation';
import { ReviewDeploy } from './components/ReviewDeploy';
import type { OnboardingStep } from './types';
import './onboarding.css';

const STEP_META: { id: OnboardingStep; label: string }[] = [
  { id: 'connect', label: 'Connect' },
  { id: 'business', label: 'Business' },
  { id: 'automation', label: 'Automation' },
  { id: 'review', label: 'Deploy' },
];

export function OnboardingWizard() {
  const {
    state,
    stepIndex,
    totalSteps,
    canGoNext,
    canGoBack,
    connectedCount,
    deployResult,
    goNext,
    goBack,
    goToStep,
    toggleSystem,
    setBusinessType,
    setAutomationLevel,
    setDepartmentLevel,
    toggleDepartment,
    deploy,
  } = useOnboardingWizard();

  return (
    <div className="ob-container">
      {/* Header */}
      <header className="ob-header">
        <div className="ob-header-left">
          <Link to="/" className="sg-back-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
            Console
          </Link>
          <h1 className="ob-page-title">Setup Wizard</h1>
        </div>
        <div className="ob-header-right">
          {connectedCount > 0 && (
            <span className="ob-connected-badge">
              {connectedCount} system{connectedCount !== 1 ? 's' : ''} connected
            </span>
          )}
        </div>
      </header>

      {/* Step indicators */}
      <nav className="ob-steps-bar">
        {STEP_META.map((s, i) => (
          <button
            key={s.id}
            className={`ob-step-indicator ${
              s.id === state.step ? 'ob-step-indicator--active' : ''
            } ${i < stepIndex ? 'ob-step-indicator--done' : ''}`}
            onClick={() => goToStep(s.id)}
            disabled={state.deploying || state.deployed}
          >
            <span className="ob-step-num">
              {i < stepIndex ? (
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                  <path d="M20 6L9 17l-5-5" />
                </svg>
              ) : (
                i + 1
              )}
            </span>
            <span className="ob-step-label">{s.label}</span>
          </button>
        ))}
        <div className="ob-step-progress" style={{ width: `${(stepIndex / (totalSteps - 1)) * 100}%` }} />
      </nav>

      {/* Content */}
      <div className="ob-content">
        {state.step === 'connect' && (
          <ConnectSystems systems={state.systems} onToggle={toggleSystem} />
        )}
        {state.step === 'business' && (
          <SelectBusinessType selected={state.businessType} onSelect={setBusinessType} />
        )}
        {state.step === 'automation' && (
          <ConfigureAutomation
            config={state.automation}
            onSetLevel={setAutomationLevel}
            onSetDeptLevel={setDepartmentLevel}
            onToggleDept={toggleDepartment}
          />
        )}
        {state.step === 'review' && (
          <ReviewDeploy state={state} deployResult={deployResult} onDeploy={deploy} />
        )}
      </div>

      {/* Footer navigation */}
      {!state.deployed && (
        <footer className="ob-footer">
          <button
            className="exec-btn exec-btn--secondary"
            onClick={goBack}
            disabled={!canGoBack}
          >
            Back
          </button>
          <span className="ob-step-counter">
            Step {stepIndex + 1} of {totalSteps}
          </span>
          {state.step !== 'review' ? (
            <button
              className="exec-btn exec-btn--primary"
              onClick={goNext}
              disabled={!canGoNext}
            >
              Continue
            </button>
          ) : (
            <span />
          )}
        </footer>
      )}

      {/* Post-deploy footer */}
      {state.deployed && (
        <footer className="ob-footer">
          <span />
          <Link to="/" className="exec-btn exec-btn--primary">
            Go to Console
          </Link>
          <span />
        </footer>
      )}
    </div>
  );
}
