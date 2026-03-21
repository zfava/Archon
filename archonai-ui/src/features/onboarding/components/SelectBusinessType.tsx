import type { BusinessType, BusinessTypeOption } from '../types';

const BUSINESS_TYPES: BusinessTypeOption[] = [
  {
    id: 'healthcare',
    label: 'Healthcare',
    description: 'Patient care coordination, compliance, scheduling, and revenue cycle management.',
    suggestedAgents: ['Compliance Agent', 'Scheduling Agent', 'Revenue Cycle Agent'],
  },
  {
    id: 'financial-services',
    label: 'Financial Services',
    description: 'Risk management, regulatory compliance, portfolio operations, and client advisory.',
    suggestedAgents: ['Risk Assessment Agent', 'Compliance Agent', 'Portfolio Ops Agent'],
  },
  {
    id: 'manufacturing',
    label: 'Manufacturing',
    description: 'Supply chain optimization, production scheduling, quality control, and logistics.',
    suggestedAgents: ['Supply Chain Agent', 'Quality Control Agent', 'Logistics Agent'],
  },
  {
    id: 'professional-services',
    label: 'Professional Services',
    description: 'Project management, resource allocation, billing, and client engagement.',
    suggestedAgents: ['Resource Planning Agent', 'Billing Agent', 'Client Engagement Agent'],
  },
  {
    id: 'energy',
    label: 'Energy & Utilities',
    description: 'Asset management, grid operations, safety-critical systems, and regulatory compliance.',
    suggestedAgents: ['Asset Monitor', 'Outage Coordinator', 'Compliance Tracker'],
  },
  {
    id: 'defense',
    label: 'Defense & Government',
    description: 'Mission readiness, supply chain visibility, personnel management, and secure operations.',
    suggestedAgents: ['Readiness Coordinator', 'Supply Chain Monitor', 'Cybersecurity Monitor'],
  },
];

interface Props {
  selected: BusinessType | null;
  onSelect: (type: BusinessType) => void;
}

export function SelectBusinessType({ selected, onSelect }: Props) {
  return (
    <div className="ob-step-content">
      <div className="ob-step-intro">
        <h2 className="ob-step-title">Select your business type</h2>
        <p className="ob-step-desc">
          ArchonAI will pre-configure agents and strategies optimized for your industry.
        </p>
      </div>

      <div className="ob-biz-grid">
        {BUSINESS_TYPES.map(bt => (
          <button
            key={bt.id}
            className={`ob-biz-card ${selected === bt.id ? 'ob-biz-card--active' : ''}`}
            onClick={() => onSelect(bt.id)}
          >
            <span className="ob-biz-name">{bt.label}</span>
            <p className="ob-biz-desc">{bt.description}</p>
            <div className="ob-biz-agents">
              {bt.suggestedAgents.map(a => (
                <span key={a} className="ob-agent-chip">{a}</span>
              ))}
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}
