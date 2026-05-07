import type { KnowledgeGraphLink } from '../types';

interface Props {
  links: KnowledgeGraphLink[];
}

function nodeTypeColor(type: string): string {
  switch (type) {
    case 'goal': return '#6366f1';
    case 'strategy': return '#8b5cf6';
    case 'task-graph': return '#06b6d4';
    case 'task': return '#4ade80';
    case 'agent': return '#f97316';
    case 'evaluation': return '#ec4899';
    default: return '#71717a';
  }
}

function relLabel(rel: string): string {
  return rel.replace(/_/g, ' ');
}

export function KnowledgeGraph({ links }: Props) {
  if (links.length === 0) return null;

  // Collect unique nodes
  const nodeMap = new Map<string, string>();
  for (const link of links) {
    nodeMap.set(link.from, link.fromType);
    nodeMap.set(link.to, link.toType);
  }

  const nodes = Array.from(nodeMap.entries());

  return (
    <div className="at-section">
      <h3 className="at-section-title">Knowledge Graph Connections</h3>

      {/* Node legend */}
      <div className="at-kg-legend">
        {['goal', 'strategy', 'task-graph', 'agent', 'task', 'evaluation'].map((type) => (
          <span key={type} className="at-kg-legend-item">
            <span className="at-kg-legend-dot" style={{ background: nodeTypeColor(type) }} />
            {type}
          </span>
        ))}
      </div>

      {/* Nodes */}
      <div className="at-kg-nodes">
        {nodes.map(([name, type]) => (
          <span
            key={name}
            className="at-kg-node"
            style={{ borderColor: nodeTypeColor(type), color: nodeTypeColor(type) }}
          >
            {name}
          </span>
        ))}
      </div>

      {/* Relationships */}
      <div className="at-kg-edges">
        {links.map((link, i) => (
          <div key={i} className="at-kg-edge">
            <span className="at-kg-from" style={{ color: nodeTypeColor(link.fromType) }}>
              {link.from}
            </span>
            <span className="at-kg-rel">
              <svg width="16" height="8" viewBox="0 0 16 8">
                <path d="M0 4h12M10 1l3 3-3 3" fill="none" stroke="#3f3f46" strokeWidth="1.5" />
              </svg>
              {relLabel(link.relationship)}
            </span>
            <span className="at-kg-to" style={{ color: nodeTypeColor(link.toType) }}>
              {link.to}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}
