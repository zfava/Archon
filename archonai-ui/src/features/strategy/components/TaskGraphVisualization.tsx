import { useMemo } from 'react';
import type { TaskGraph, TaskGraphNode } from '../../command/types';

interface Props {
  graph: TaskGraph;
  selectedNodeId: string | null;
  onSelectNode: (nodeId: string | null) => void;
}

interface LayoutNode {
  node: TaskGraphNode;
  x: number;
  y: number;
  layer: number;
}

const NODE_W = 200;
const NODE_H = 72;
const LAYER_GAP = 120;
const NODE_GAP = 32;
const PAD_X = 40;
const PAD_Y = 40;

function computeLayout(graph: TaskGraph): LayoutNode[] {
  const { nodes, edges } = graph;
  if (nodes.length === 0) return [];

  // Build adjacency maps
  const inDeg = new Map<string, number>();
  const children = new Map<string, string[]>();
  for (const n of nodes) {
    inDeg.set(n.nodeId, 0);
    children.set(n.nodeId, []);
  }
  for (const e of edges) {
    inDeg.set(e.targetNodeId, (inDeg.get(e.targetNodeId) ?? 0) + 1);
    children.get(e.sourceNodeId)?.push(e.targetNodeId);
  }

  // BFS topological layering
  const layers: string[][] = [];
  const layerOf = new Map<string, number>();
  let frontier = nodes.filter((n) => (inDeg.get(n.nodeId) ?? 0) === 0).map((n) => n.nodeId);
  if (frontier.length === 0) frontier = [nodes[0].nodeId]; // fallback for cycles

  while (frontier.length > 0) {
    layers.push(frontier);
    frontier.forEach((id) => layerOf.set(id, layers.length - 1));
    const next: string[] = [];
    for (const id of frontier) {
      for (const child of children.get(id) ?? []) {
        if (!layerOf.has(child)) {
          const deg = (inDeg.get(child) ?? 1) - 1;
          inDeg.set(child, deg);
          if (deg <= 0) next.push(child);
        }
      }
    }
    frontier = next;
  }

  // Assign any unvisited nodes to last layer
  for (const n of nodes) {
    if (!layerOf.has(n.nodeId)) {
      const last = layers.length - 1;
      layers[last].push(n.nodeId);
      layerOf.set(n.nodeId, last);
    }
  }

  const nodeMap = new Map(nodes.map((n) => [n.nodeId, n]));
  const result: LayoutNode[] = [];

  for (let li = 0; li < layers.length; li++) {
    const layer = layers[li];
    const totalW = layer.length * NODE_W + (layer.length - 1) * NODE_GAP;
    const startX = -totalW / 2 + NODE_W / 2;

    for (let ni = 0; ni < layer.length; ni++) {
      const node = nodeMap.get(layer[ni]);
      if (!node) continue;
      result.push({
        node,
        x: startX + ni * (NODE_W + NODE_GAP),
        y: li * (NODE_H + LAYER_GAP),
        layer: li,
      });
    }
  }

  return result;
}

const AGENT_COLORS: Record<string, string> = {
  finance: '#22c55e',
  sales: '#6366f1',
  marketing: '#f59e0b',
  operations: '#06b6d4',
  support: '#ec4899',
  analysis: '#8b5cf6',
  planning: '#14b8a6',
};

function agentColor(agentType: string): string {
  const key = agentType.toLowerCase();
  for (const [k, v] of Object.entries(AGENT_COLORS)) {
    if (key.includes(k)) return v;
  }
  return '#6366f1';
}

function statusColor(status: string): string {
  switch (status.toLowerCase()) {
    case 'completed': return '#4ade80';
    case 'running': case 'inprogress': return '#6366f1';
    case 'failed': return '#ef4444';
    case 'cancelled': return '#71717a';
    default: return '#52525b';
  }
}

export function TaskGraphVisualization({ graph, selectedNodeId, onSelectNode }: Props) {
  const layout = useMemo(() => computeLayout(graph), [graph]);
  const posMap = useMemo(
    () => new Map(layout.map((l) => [l.node.nodeId, l])),
    [layout],
  );

  if (layout.length === 0) {
    return <div className="sg-empty">No tasks in this graph.</div>;
  }

  // Compute SVG viewBox
  const xs = layout.map((l) => l.x);
  const ys = layout.map((l) => l.y);
  const minX = Math.min(...xs) - NODE_W / 2 - PAD_X;
  const maxX = Math.max(...xs) + NODE_W / 2 + PAD_X;
  const minY = Math.min(...ys) - PAD_Y;
  const maxY = Math.max(...ys) + NODE_H + PAD_Y;
  const vw = maxX - minX;
  const vh = maxY - minY;

  return (
    <div className="sg-graph-wrap">
      <svg
        className="sg-graph-svg"
        viewBox={`${minX} ${minY} ${vw} ${vh}`}
        preserveAspectRatio="xMidYMin meet"
      >
        <defs>
          <marker
            id="arrowhead"
            markerWidth="8"
            markerHeight="6"
            refX="8"
            refY="3"
            orient="auto"
          >
            <polygon points="0 0, 8 3, 0 6" fill="#3f3f46" />
          </marker>
        </defs>

        {/* Edges */}
        {graph.edges.map((edge) => {
          const src = posMap.get(edge.sourceNodeId);
          const tgt = posMap.get(edge.targetNodeId);
          if (!src || !tgt) return null;

          const x1 = src.x;
          const y1 = src.y + NODE_H;
          const x2 = tgt.x;
          const y2 = tgt.y;
          const midY = (y1 + y2) / 2;

          return (
            <path
              key={edge.edgeId}
              d={`M ${x1} ${y1} C ${x1} ${midY}, ${x2} ${midY}, ${x2} ${y2}`}
              fill="none"
              stroke="#3f3f46"
              strokeWidth="2"
              markerEnd="url(#arrowhead)"
              className="sg-edge"
            />
          );
        })}

        {/* Nodes */}
        {layout.map(({ node, x, y }) => {
          const isSelected = node.nodeId === selectedNodeId;
          const color = agentColor(node.agentType);

          return (
            <g
              key={node.nodeId}
              className={`sg-node ${isSelected ? 'sg-node--selected' : ''}`}
              onClick={() => onSelectNode(isSelected ? null : node.nodeId)}
              style={{ cursor: 'pointer' }}
            >
              {/* Card background */}
              <rect
                x={x - NODE_W / 2}
                y={y}
                width={NODE_W}
                height={NODE_H}
                rx={8}
                fill={isSelected ? '#1a1a2e' : '#18181b'}
                stroke={isSelected ? color : '#27272a'}
                strokeWidth={isSelected ? 2 : 1}
              />

              {/* Agent color bar */}
              <rect
                x={x - NODE_W / 2}
                y={y}
                width={4}
                height={NODE_H}
                rx={2}
                fill={color}
              />

              {/* Node name */}
              <text
                x={x - NODE_W / 2 + 14}
                y={y + 24}
                fill="#e4e4e7"
                fontSize="12"
                fontWeight="600"
                fontFamily="Inter, sans-serif"
              >
                {node.name.length > 22 ? node.name.slice(0, 22) + '...' : node.name}
              </text>

              {/* Agent type */}
              <text
                x={x - NODE_W / 2 + 14}
                y={y + 42}
                fill={color}
                fontSize="10"
                fontWeight="500"
                fontFamily="Inter, sans-serif"
              >
                {node.agentType}
              </text>

              {/* Status dot */}
              <circle
                cx={x + NODE_W / 2 - 14}
                cy={y + 14}
                r={4}
                fill={statusColor(node.status)}
              />

              {/* Duration */}
              <text
                x={x - NODE_W / 2 + 14}
                y={y + 60}
                fill="#71717a"
                fontSize="10"
                fontFamily="Inter, sans-serif"
              >
                {node.estimatedDurationHours}h est.
              </text>

              {/* Expand indicator */}
              <text
                x={x + NODE_W / 2 - 16}
                y={y + NODE_H - 10}
                fill="#52525b"
                fontSize="12"
                fontFamily="Inter, sans-serif"
                textAnchor="end"
              >
                {isSelected ? '−' : '+'}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}
