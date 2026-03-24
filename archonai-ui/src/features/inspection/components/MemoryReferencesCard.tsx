import type { MemoryContextReference } from '../types';

interface Props {
  references: MemoryContextReference[];
}

export function MemoryReferencesCard({ references }: Props) {
  if (references.length === 0) return null;

  return (
    <section className="ins-card">
      <span className="ins-section-label">Memory / Context Sources</span>
      <p className="ins-section-desc">
        Context retrieved from organizational memory that influenced this decision or action.
      </p>
      <div className="ins-memory-list">
        {references.map((ref, i) => (
          <div key={i} className="ins-memory-row">
            <div className="ins-memory-header">
              <span className="ins-memory-type">{ref.memoryType}</span>
              <span className="ins-memory-source">{ref.source}</span>
              <span className="ins-memory-score">
                {(ref.relevanceScore * 100).toFixed(0)}% relevance
              </span>
            </div>
            <p className="ins-memory-summary">{ref.contentSummary}</p>
            <span className="ins-memory-context">{ref.usageContext}</span>
          </div>
        ))}
      </div>
    </section>
  );
}
