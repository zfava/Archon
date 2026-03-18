import { useCallback, useEffect, useState } from 'react';
import { api } from '../../../api/client';
import type {
  AttributionChain,
  KnowledgeGraphLink,
  OperationalGoal,
  OutcomeEvaluationResult,
  SimulationGuidedPlan,
  TaskGraph,
} from '../types';

interface AttributionState {
  loading: boolean;
  error: string | null;
  chain: AttributionChain | null;
}

/** Build knowledge graph links from the plan and evaluation data. */
function buildKnowledgeLinks(
  plan: SimulationGuidedPlan | null,
  graphs: TaskGraph[],
  evaluations: OutcomeEvaluationResult[],
): KnowledgeGraphLink[] {
  const links: KnowledgeGraphLink[] = [];
  const seen = new Set<string>();

  const add = (from: string, fromType: string, rel: string, to: string, toType: string) => {
    const key = `${from}|${rel}|${to}`;
    if (!seen.has(key)) {
      seen.add(key);
      links.push({ from, fromType, relationship: rel, to, toType });
    }
  };

  // Goal → Strategy
  if (plan) {
    add(plan.goal.title, 'goal', 'selected_strategy', plan.selectedStrategy, 'strategy');
    add(plan.selectedStrategy, 'strategy', 'produced', plan.selectedTaskGraph.graphId.slice(0, 8), 'task-graph');
  }

  // Task graph nodes → agent assignments
  for (const graph of graphs) {
    for (const node of graph.nodes) {
      add(graph.graphId.slice(0, 8), 'task-graph', 'contains', node.name, 'task');
      add(node.agentType, 'agent', 'executes', node.name, 'task');
    }
    for (const edge of graph.edges) {
      const srcNode = graph.nodes.find((n) => n.nodeId === edge.sourceNodeId);
      const tgtNode = graph.nodes.find((n) => n.nodeId === edge.targetNodeId);
      if (srcNode && tgtNode) {
        add(srcNode.name, 'task', 'feeds', tgtNode.name, 'task');
      }
    }
  }

  // Evaluations → outcome link
  for (const ev of evaluations) {
    add(ev.strategy, 'strategy', 'evaluated_by', ev.evaluationId.slice(0, 8), 'evaluation');
    for (const ne of ev.nodeEvaluations) {
      add(ne.agentType, 'agent', 'produced_result', ne.nodeName, 'task');
    }
  }

  return links;
}

export function useAttribution(goalId: string | undefined) {
  const [state, setState] = useState<AttributionState>({
    loading: false,
    error: null,
    chain: null,
  });

  const load = useCallback(async (id: string) => {
    setState({ loading: true, error: null, chain: null });

    try {
      // Fetch goal, plan, graphs, and evaluations in parallel
      const [goal, plan, graphs, evaluations] = await Promise.allSettled([
        api.getGoal(id) as Promise<OperationalGoal>,
        api.getGuidedPlan(id) as Promise<SimulationGuidedPlan>,
        api.getGraphsByGoal(id) as Promise<TaskGraph[]>,
        api.getOutcomeEvaluationsForGoal(id) as Promise<OutcomeEvaluationResult[]>,
      ]);

      const goalData = goal.status === 'fulfilled' ? goal.value : null;
      if (!goalData) {
        setState({ loading: false, error: 'Goal not found', chain: null });
        return;
      }

      const planData = plan.status === 'fulfilled' ? plan.value : null;
      const graphsData = graphs.status === 'fulfilled' ? graphs.value : [];
      const evalsData = evaluations.status === 'fulfilled' ? evaluations.value : [];

      const knowledgeLinks = buildKnowledgeLinks(planData, graphsData, evalsData);

      setState({
        loading: false,
        error: null,
        chain: {
          goal: goalData,
          plan: planData,
          graphs: graphsData,
          evaluations: evalsData,
          knowledgeLinks,
        },
      });
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setState({ loading: false, error: msg, chain: null });
    }
  }, []);

  useEffect(() => {
    if (goalId) {
      const id = goalId;
      queueMicrotask(() => load(id));
    }
  }, [goalId, load]);

  return state;
}
