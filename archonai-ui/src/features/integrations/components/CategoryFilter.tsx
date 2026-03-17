import type { ConnectorCategory } from '../types';

const CATEGORIES: { id: ConnectorCategory | 'all'; label: string }[] = [
  { id: 'all', label: 'All' },
  { id: 'crm', label: 'CRM' },
  { id: 'erp', label: 'ERP' },
  { id: 'finance', label: 'Finance' },
  { id: 'marketing', label: 'Marketing' },
  { id: 'messaging', label: 'Messaging' },
  { id: 'productivity', label: 'Productivity' },
  { id: 'support', label: 'Support' },
];

interface Props {
  active: ConnectorCategory | 'all';
  onChange: (cat: ConnectorCategory | 'all') => void;
  counts: Record<string, number>;
}

export function CategoryFilter({ active, onChange, counts }: Props) {
  return (
    <div className="im-categories">
      {CATEGORIES.map(cat => {
        const count = cat.id === 'all'
          ? Object.values(counts).reduce((a, b) => a + b, 0)
          : (counts[cat.id] ?? 0);
        return (
          <button
            key={cat.id}
            className={`im-cat-btn ${active === cat.id ? 'im-cat-btn--active' : ''}`}
            onClick={() => onChange(cat.id)}
          >
            {cat.label}
            <span className="im-cat-count">{count}</span>
          </button>
        );
      })}
    </div>
  );
}
