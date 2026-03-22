import type { BusinessType, BusinessTypeOption } from '../types';

const BUSINESS_TYPES: BusinessTypeOption[] = [
  {
    id: 'saas',
    label: 'SaaS',
    description: 'Customer lifecycle management, subscription billing, product analytics, and support operations.',
    suggestedAgents: ['Customer Success Agent', 'Churn Prediction Agent', 'Support Triage Agent'],
  },
  {
    id: 'ecommerce',
    label: 'E-Commerce',
    description: 'Order management, inventory optimization, fulfillment coordination, and customer experience.',
    suggestedAgents: ['Inventory Optimizer', 'Order Routing Agent', 'Customer Experience Agent'],
  },
  {
    id: 'healthcare',
    label: 'Healthcare',
    description: 'Patient care coordination, compliance, scheduling, and revenue cycle management.',
    suggestedAgents: ['Care Coordinator', 'Scheduling Optimizer', 'Revenue Cycle Agent'],
  },
  {
    id: 'financial-services',
    label: 'Financial Services',
    description: 'Risk management, regulatory compliance, portfolio operations, and client advisory.',
    suggestedAgents: ['Risk Monitor', 'Compliance Agent', 'Trade Operations Agent'],
  },
  {
    id: 'manufacturing',
    label: 'Manufacturing',
    description: 'Supply chain optimization, production scheduling, quality control, and logistics.',
    suggestedAgents: ['Production Scheduler', 'Quality Inspector', 'Supply Chain Monitor'],
  },
  {
    id: 'professional-services',
    label: 'Professional Services',
    description: 'Resource optimization, project profitability tracking, and client engagement management.',
    suggestedAgents: ['Resource Allocation Optimizer', 'Project Profitability Monitor', 'Client Engagement Coordinator'],
  },
  {
    id: 'energy',
    label: 'Energy & Utilities',
    description: 'Asset management, grid operations, regulatory compliance, and field workforce optimization.',
    suggestedAgents: ['Asset Performance Agent', 'Outage Management Agent', 'Regulatory Compliance Agent'],
  },
  {
    id: 'defense',
    label: 'Defense & Government',
    description: 'Mission readiness, logistics optimization, personnel management, and compliance tracking.',
    suggestedAgents: ['Readiness Assessment Agent', 'Logistics Optimizer', 'Compliance Monitor'],
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
