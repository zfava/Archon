import { useParams, Link } from 'react-router-dom';
import { useAttribution } from './hooks/useAttribution';
import { StrategyAttribution } from './components/StrategyAttribution';
import { AgentAttribution } from './components/AgentAttribution';
import { DecisionAttribution } from './components/DecisionAttribution';
import { KnowledgeGraph } from './components/KnowledgeGraph';
import './attribution.css';

export function AttributionView() {
  const { goalId } = useParams<{ goalId: string }>();
  const { loading, error, chain } = useAttribution(goalId);

  if (loading) {
    return (
      <div className="at-view">
        <header className="at-header">
          <Link to="/" className="at-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
          </Link>
          <h1 className="at-title">Attribution</h1>
        </header>
        <div className="at-loading">
          <div className="at-loading-bar" />
          <div className="at-loading-bar" />
          <div className="at-loading-bar" />
        </div>
      </div>
    );
  }

  if (error || !chain) {
    return (
      <div className="at-view">
        <header className="at-header">
          <Link to="/" className="at-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
          </Link>
          <h1 className="at-title">Attribution</h1>
        </header>
        <div className="at-error">{error ?? 'Goal not found'}</div>
      </div>
    );
  }

  const { goal, plan, graphs, evaluations, knowledgeLinks } = chain;

  // Collect all node evaluations across evaluations
  const allNodeEvals = evaluations.flatMap((e) => e.nodeEvaluations);

  return (
    <div className="at-view">
      {/* Header */}
      <header className="at-header">
        <div className="at-header-left">
          <Link to="/" className="at-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
          </Link>
          <div>
            <h1 className="at-title">Outcome Attribution</h1>
            <p className="at-subtitle">Explaining why this outcome happened</p>
          </div>
        </div>
        <div className="at-header-right">
          <Link to={`/strategy/${goal.goalId}`} className="at-link-btn">
            View Strategy
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M5 12h14M12 5l7 7-7 7" />
            </svg>
          </Link>
        </div>
      </header>

      <div className="at-scroll">
        {/* Goal context */}
        <div className="at-goal-banner">
          <div className="at-goal-info">
            <h2 className="at-goal-title">{goal.title}</h2>
            <p className="at-goal-desc">{goal.description}</p>
          </div>
          <div className="at-goal-meta">
            <span className={`at-priority at-priority--${goal.priority.toLowerCase()}`}>
              {goal.priority}
            </span>
            <span className="at-meta-chip">{goal.department}</span>
            <span className="at-meta-chip">{goal.status}</span>
          </div>
        </div>

        {/* 1. Strategy attribution */}
        {plan && <StrategyAttribution plan={plan} />}

        {/* 2. Agent attribution */}
        {graphs.length > 0 && (
          <AgentAttribution graphs={graphs} nodeEvaluations={allNodeEvals} />
        )}

        {/* 3. Decision impact */}
        <DecisionAttribution evaluations={evaluations} />

        {/* 4. Knowledge graph connections */}
        <KnowledgeGraph links={knowledgeLinks} />
      </div>
    </div>
  );
}
