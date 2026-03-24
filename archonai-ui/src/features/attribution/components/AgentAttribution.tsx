import type { NodeEvaluationResult, TaskGraph } from '../types';

interface Props {
  graphs: TaskGraph[];
  nodeEvaluations: NodeEvaluationResult[];
}

function agentColor(agentType: string): string {
  const hash = agentType.split('').reduce((h, c) => ((h << 5) - h + c.charCodeAt(0)) | 0, 0);
  const hues = [210, 260, 330, 160, 30, 180, 290, 50];
  const hue = hues[Math.abs(hash) % hues.length];
  return `hsl(${hue}, 70%, 65%)`;
}

export function AgentAttribution({ graphs, nodeEvaluations }: Props) {
  // Build agent → task mapping
  const agentMap = new Map<string, { tasks: string[]; nodeIds: string[] }>();

  for (const graph of graphs) {
    for (const node of graph.nodes) {
      const entry = agentMap.get(node.agentType) ?? { tasks: [], nodeIds: [] };
      entry.tasks.push(node.name);
      entry.nodeIds.push(node.nodeId);
      agentMap.set(node.agentType, entry);
    }
  }

  // Build evaluation lookup
  const evalMap = new Map<string, NodeEvaluationResult>();
  for (const ne of nodeEvaluations) {
    evalMap.set(ne.nodeId, ne);
  }

  const agents = Array.from(agentMap.entries());

  return (
    <div className="at-section">
      <h3 className="at-section-title">Which Agents Executed</h3>

      <div className="at-agents-grid">
        {agents.map(([agentType, { tasks, nodeIds }]) => {
          const color = agentColor(agentType);
          // Get evaluations for this agent's tasks
          const evals = nodeIds
            .map((id) => evalMap.get(id))
            .filter((e): e is NodeEvaluationResult => !!e);
          const succeeded = evals.filter((e) => e.actualSuccess).length;
          const failed = evals.filter((e) => !e.actualSuccess).length;
          const hasEvals = evals.length > 0;

          return (
            <div key={agentType} className="at-agent-card">
              <div className="at-agent-header">
                <span className="at-agent-dot" style={{ background: color }} />
                <span className="at-agent-name">{agentType}</span>
                <span className="at-agent-count">{tasks.length} task{tasks.length > 1 ? 's' : ''}</span>
              </div>

              {hasEvals && (
                <div className="at-agent-results">
                  {succeeded > 0 && (
                    <span className="at-result-badge at-result--ok">{succeeded} succeeded</span>
                  )}
                  {failed > 0 && (
                    <span className="at-result-badge at-result--fail">{failed} failed</span>
                  )}
                </div>
              )}

              <div className="at-agent-tasks">
                {tasks.map((task, i) => {
                  const ev = evalMap.get(nodeIds[i]);
                  return (
                    <div key={i} className="at-task-row">
                      {ev ? (
                        <span className={`at-task-dot ${ev.actualSuccess ? 'at-task-dot--ok' : 'at-task-dot--fail'}`} />
                      ) : (
                        <span className="at-task-dot at-task-dot--pending" />
                      )}
                      <span className="at-task-name">{task}</span>
                      {ev && (
                        <span className="at-task-meta">
                          {ev.actualDurationHours.toFixed(1)}h / ${Number(ev.actualCost).toFixed(2)}
                        </span>
                      )}
                    </div>
                  );
                })}
              </div>

              {evals.length > 0 && evals.some((e) => e.wasOnCriticalPath) && (
                <span className="at-critical-path-tag">Critical Path</span>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
