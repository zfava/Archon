import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useExplanations } from './hooks/useExplanations';
import { StrategyExplanationCard } from './components/StrategyExplanationCard';
import { AgentExplanationCard } from './components/AgentExplanationCard';
import './explanations.css';

type Tab = 'strategy' | 'agent' | 'decision';

export function ExplanationView() {
  const {
    loading,
    error,
    strategyExplanation,
    agentExplanation,
    decisionExplanation,
    explainStrategy,
    explainAgent,
    explainDecision,
  } = useExplanations();

  const [tab, setTab] = useState<Tab>('decision');
  const [goalId, setGoalId] = useState('');
  const [goalTitle, setGoalTitle] = useState('');
  const [strategies, setStrategies] = useState('balanced, cost-optimized, throughput-optimized');
  const [capability, setCapability] = useState('');
  const [taskType, setTaskType] = useState('');

  const handleSubmit = async () => {
    const strategyList = strategies
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);

    switch (tab) {
      case 'strategy':
        if (goalId && goalTitle && strategyList.length > 0) {
          await explainStrategy(goalId, goalTitle, strategyList);
        }
        break;
      case 'agent':
        if (capability) {
          await explainAgent(capability, taskType || undefined);
        }
        break;
      case 'decision':
        if (goalId && goalTitle && strategyList.length > 0 && capability) {
          await explainDecision(goalId, goalTitle, strategyList, capability, taskType || undefined);
        }
        break;
    }
  };

  return (
    <div className="ex-container">
      {/* Header */}
      <header className="ex-header">
        <div className="ex-header-left">
          <Link to="/" className="sg-back-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
            Console
          </Link>
          <h1 className="ex-page-title">Decision Explainer</h1>
        </div>
      </header>

      {/* Error banner */}
      {error && (
        <div className="ex-error-banner">
          <span>{error}</span>
        </div>
      )}

      <div className="ex-content">
        <div className="ex-main">
          {/* Query card */}
          <section className="ex-card">
            <span className="ex-section-label">Generate Explanation</span>
            <p className="ex-section-desc">
              Ask the Reasoner why a strategy was chosen or why an agent was selected for a task.
            </p>

            {/* Tabs */}
            <div className="ex-tabs">
              {(['strategy', 'agent', 'decision'] as Tab[]).map((t) => (
                <button
                  key={t}
                  className={`ex-tab ${tab === t ? 'ex-tab--active' : ''}`}
                  onClick={() => setTab(t)}
                >
                  {t === 'strategy' ? 'Strategy Choice' : t === 'agent' ? 'Agent Selection' : 'Full Decision'}
                </button>
              ))}
            </div>

            <div className="ex-form">
              {(tab === 'strategy' || tab === 'decision') && (
                <>
                  <div className="ex-form-row">
                    <div className="ex-form-field">
                      <label className="ex-field-label">Goal ID</label>
                      <input
                        className="ex-input"
                        type="text"
                        placeholder="e.g. 3fa85f64-5717-4562-b3fc-2c963f66afa6"
                        value={goalId}
                        onChange={(e) => setGoalId(e.target.value)}
                      />
                    </div>
                    <div className="ex-form-field">
                      <label className="ex-field-label">Goal Title</label>
                      <input
                        className="ex-input"
                        type="text"
                        placeholder="e.g. Reduce customer churn by 15%"
                        value={goalTitle}
                        onChange={(e) => setGoalTitle(e.target.value)}
                      />
                    </div>
                  </div>
                  <div className="ex-form-field">
                    <label className="ex-field-label">Candidate Strategies (comma-separated)</label>
                    <input
                      className="ex-input"
                      type="text"
                      placeholder="e.g. balanced, cost-optimized, throughput-optimized"
                      value={strategies}
                      onChange={(e) => setStrategies(e.target.value)}
                    />
                  </div>
                </>
              )}

              {(tab === 'agent' || tab === 'decision') && (
                <div className="ex-form-row">
                  <div className="ex-form-field">
                    <label className="ex-field-label">Required Capability</label>
                    <input
                      className="ex-input"
                      type="text"
                      placeholder="e.g. workflow-orchestration"
                      value={capability}
                      onChange={(e) => setCapability(e.target.value)}
                    />
                  </div>
                  <div className="ex-form-field">
                    <label className="ex-field-label">Task Type (optional)</label>
                    <input
                      className="ex-input"
                      type="text"
                      placeholder="e.g. data-analysis"
                      value={taskType}
                      onChange={(e) => setTaskType(e.target.value)}
                    />
                  </div>
                </div>
              )}

              <div className="ex-form-actions">
                <button className="ex-btn-primary" onClick={handleSubmit} disabled={loading}>
                  {loading ? (
                    <>
                      <div className="phase-spinner" />
                      Generating...
                    </>
                  ) : (
                    'Explain'
                  )}
                </button>
              </div>
            </div>
          </section>

          {/* Decision summary */}
          {decisionExplanation && tab === 'decision' && (
            <section className="ex-card ex-summary-card">
              <span className="ex-section-label">Decision Summary</span>
              <p className="ex-summary-text">{decisionExplanation.summary}</p>
            </section>
          )}

          {/* Strategy explanation */}
          {strategyExplanation && (tab === 'strategy' || tab === 'decision') && (
            <StrategyExplanationCard explanation={strategyExplanation} />
          )}

          {/* Agent explanation */}
          {agentExplanation && (tab === 'agent' || tab === 'decision') && (
            <AgentExplanationCard explanation={agentExplanation} />
          )}
        </div>
      </div>
    </div>
  );
}
