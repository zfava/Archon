import type { OnboardingTemplate } from './types';

export const TEMPLATES: OnboardingTemplate[] = [
  // ── MANUFACTURING ──────────────────────────────────────────────────
  {
    id: 'manufacturing',
    name: 'Manufacturing',
    industry: 'Manufacturing',
    description:
      'Production scheduling, supply chain visibility, quality management, and predictive maintenance for discrete and process manufacturers.',
    agents: [
      { name: 'Production Scheduler', role: 'Optimizes production sequencing based on demand forecasts, material availability, and machine capacity' },
      { name: 'Supply Chain Monitor', role: 'Tracks inbound materials, flags lead time deviations, triggers reorder points, monitors supplier SLAs' },
      { name: 'Quality Inspector', role: 'Analyzes SPC data from production lines, flags out-of-spec trends, routes nonconformance reports' },
      { name: 'Predictive Maintenance Agent', role: 'Monitors equipment telemetry to predict failures and schedule preventive work orders' },
      { name: 'Demand Planner', role: 'Reconciles sales forecasts with production capacity and inventory positions' },
      { name: 'Logistics Coordinator', role: 'Manages outbound shipments, carrier selection, and delivery scheduling' },
      { name: 'Compliance Agent', role: 'Monitors ISO 9001, OSHA, and EPA requirements, flags upcoming audits' },
      { name: 'Cost Analyst', role: 'Tracks production costs against standard, identifies variance drivers' },
    ],
    workflows: [
      {
        name: 'Daily Production Standup',
        description: 'Coordinate daily production priorities across demand, materials, and throughput.',
        steps: ['Pull demand', 'Check material availability', 'Sequence orders', 'Assign to lines', 'Monitor throughput', 'End-of-shift report'],
      },
      {
        name: 'Quality Escape Response',
        description: 'Rapid containment and root cause resolution for quality escapes.',
        steps: ['Detect anomaly', 'Quarantine lot', 'Root cause analysis', 'Corrective action', 'Verify effectiveness', 'Close NCR'],
      },
      {
        name: 'Predictive Maintenance Trigger',
        description: 'Proactive maintenance scheduling driven by equipment telemetry.',
        steps: ['Detect degradation', 'Assess failure probability', 'Schedule work order', 'Coordinate with production', 'Execute', 'Validate'],
      },
      {
        name: 'Supply Disruption Response',
        description: 'Mitigate inbound supply disruptions with alternative sourcing and schedule adjustments.',
        steps: ['Detect delay', 'Assess impact', 'Identify alternatives', 'Escalate if critical', 'Adjust schedule', 'Notify customers'],
      },
    ],
    strategies: ['throughput-optimized', 'quality-first', 'cost-optimized', 'balanced', 'safe-mode'],
    systems: ['salesforce', 'microsoft365', 'slack'],
    automationLevel: 'assisted',
    departments: [
      { name: 'Production', enabled: true, level: 'autonomous' },
      { name: 'Quality', enabled: true, level: 'assisted' },
      { name: 'Supply Chain', enabled: true, level: 'assisted' },
      { name: 'Maintenance', enabled: true, level: 'autonomous' },
      { name: 'Finance', enabled: true, level: 'approval' },
      { name: 'Compliance', enabled: true, level: 'approval' },
      { name: 'Logistics', enabled: true, level: 'assisted' },
    ],
    estimatedMinutes: 30,
  },

  // ── HEALTHCARE ─────────────────────────────────────────────────────
  {
    id: 'healthcare',
    name: 'Healthcare',
    industry: 'Healthcare',
    description:
      'Patient care coordination, clinical workflow optimization, compliance management, and revenue cycle operations for health systems and practices.',
    agents: [
      { name: 'Care Coordinator', role: 'Manages patient handoffs, tracks care plan adherence, flags follow-up gaps' },
      { name: 'Scheduling Optimizer', role: 'Balances provider schedules against demand, appointment types, and room availability' },
      { name: 'Compliance Monitor', role: 'Tracks HIPAA, Joint Commission, and CMS requirements, flags documentation gaps' },
      { name: 'Revenue Cycle Agent', role: 'Monitors claims, denial patterns, and payment posting, identifies undercoding' },
      { name: 'Prior Authorization Agent', role: 'Tracks authorization requirements by payer and procedure, initiates and follows up requests' },
      { name: 'Patient Communications Agent', role: 'Manages reminders, post-visit instructions, surveys, and recall campaigns' },
      { name: 'Clinical Documentation Agent', role: 'Reviews encounter notes for completeness, flags missing diagnoses for coding accuracy' },
      { name: 'Resource Utilization Agent', role: 'Monitors bed occupancy, OR utilization, and equipment availability' },
    ],
    workflows: [
      {
        name: 'Patient Visit Lifecycle',
        description: 'End-to-end patient visit from pre-visit prep through payment posting.',
        steps: ['Pre-visit prep', 'Check-in', 'Encounter', 'Documentation review', 'Coding', 'Claim submission', 'Payment posting'],
      },
      {
        name: 'Denial Management',
        description: 'Systematic denial detection, categorization, and appeal workflow.',
        steps: ['Detect denial', 'Categorize', 'Route for appeal', 'Draft letter', 'Submit', 'Track', 'Report trends'],
      },
      {
        name: 'Prior Authorization',
        description: 'Identify and fulfill payer authorization requirements before procedures.',
        steps: ['Identify requirement', 'Gather documentation', 'Submit', 'Track status', 'Escalate', 'Notify provider'],
      },
      {
        name: 'Compliance Audit Prep',
        description: 'Prepare for regulatory audits with gap analysis and remediation tracking.',
        steps: ['Identify audit', 'Gather docs', 'Gap analysis', 'Assign remediation', 'Verify completion', 'Generate packet'],
      },
    ],
    strategies: ['patient-safety-first', 'compliance-first', 'efficiency-balanced', 'balanced', 'safe-mode'],
    systems: ['microsoft365', 'slack', 'google-workspace'],
    automationLevel: 'approval',
    departments: [
      { name: 'Clinical Operations', enabled: true, level: 'assisted' },
      { name: 'Scheduling', enabled: true, level: 'assisted' },
      { name: 'Revenue Cycle', enabled: true, level: 'assisted' },
      { name: 'Compliance', enabled: true, level: 'approval' },
      { name: 'Patient Services', enabled: true, level: 'assisted' },
      { name: 'Administration', enabled: true, level: 'approval' },
    ],
    estimatedMinutes: 35,
  },

  // ── FINANCIAL SERVICES ─────────────────────────────────────────────
  {
    id: 'financial-services',
    name: 'Financial Services',
    industry: 'Financial Services',
    description:
      'Risk monitoring, regulatory compliance, portfolio operations, and client advisory support for banks, asset managers, and insurance carriers.',
    agents: [
      { name: 'Risk Monitor', role: 'Continuous surveillance of VaR, concentration limits, counterparty exposure, alerts on threshold breaches' },
      { name: 'Compliance Agent', role: 'Tracks SEC, FINRA, OCC requirements, monitors filing deadlines and examination schedules' },
      { name: 'Trade Operations Agent', role: 'Monitors settlement, confirms allocations, flags exceptions, tracks T+1 compliance' },
      { name: 'Client Reporting Agent', role: 'Generates performance reports, fee calculations, compliance attestations by SLA deadlines' },
      { name: 'AML/KYC Monitor', role: 'Screens transactions against sanctions lists and suspicious activity patterns, flags for SAR review' },
      { name: 'Credit Underwriting Agent', role: 'Gathers financial data, calculates ratios, generates preliminary assessments, routes for human review' },
      { name: 'Regulatory Filing Agent', role: 'Tracks filing calendars for 13F, Form ADV, Call Reports, pre-populates forms' },
      { name: 'Market Data Agent', role: 'Monitors real-time feeds for positions, flags material moves and corporate actions' },
    ],
    workflows: [
      {
        name: 'Daily Risk Review',
        description: 'Daily portfolio risk assessment with limit monitoring and escalation.',
        steps: ['Pull positions', 'Calculate metrics', 'Compare to limits', 'Flag breaches', 'Escalate', 'Generate report'],
      },
      {
        name: 'Regulatory Examination Prep',
        description: 'Prepare for regulatory examinations with document gathering and mock reviews.',
        steps: ['Identify scope', 'Gather documents', 'Gap analysis', 'Prepare binder', 'Mock review', 'Submit'],
      },
      {
        name: 'Client Onboarding',
        description: 'KYC-compliant client onboarding from documentation through account configuration.',
        steps: ['KYC documentation', 'Sanctions screening', 'Verify ownership', 'Risk-rate', 'Open accounts', 'Configure reporting'],
      },
      {
        name: 'Incident Response',
        description: 'Detect, investigate, and report suspicious activity or operational incidents.',
        steps: ['Detect anomaly', 'Classify severity', 'Notify compliance', 'Investigate', 'File SAR if required', 'Document', 'Report'],
      },
    ],
    strategies: ['risk-averse', 'compliance-first', 'balanced', 'safe-mode'],
    systems: ['salesforce', 'microsoft365', 'slack'],
    automationLevel: 'approval',
    departments: [
      { name: 'Risk Management', enabled: true, level: 'assisted' },
      { name: 'Compliance', enabled: true, level: 'approval' },
      { name: 'Trading Operations', enabled: true, level: 'assisted' },
      { name: 'Client Services', enabled: true, level: 'assisted' },
      { name: 'Finance', enabled: true, level: 'approval' },
      { name: 'Legal', enabled: true, level: 'approval' },
    ],
    estimatedMinutes: 35,
  },

  // ── ENERGY & UTILITIES ─────────────────────────────────────────────
  {
    id: 'energy',
    name: 'Energy & Utilities',
    industry: 'Energy & Utilities',
    description:
      'Asset performance management, grid operations, regulatory compliance, and field workforce optimization for utilities and energy producers.',
    agents: [
      { name: 'Asset Performance Agent', role: 'Monitors SCADA data, tracks heat rates, availability factors, and degradation curves' },
      { name: 'Outage Management Agent', role: 'Coordinates storm response, tracks crew dispatch, ETAs, and customer notifications' },
      { name: 'Regulatory Compliance Agent', role: 'Monitors NERC, FERC, state PUC requirements, tracks filing deadlines and rate case milestones' },
      { name: 'Field Workforce Optimizer', role: 'Schedules crews by priority, qualifications, travel time, and safety requirements' },
      { name: 'Energy Trading Agent', role: 'Monitors market prices, generation forecasts, transmission constraints, flags hedging needs' },
      { name: 'Environmental Monitor', role: 'Tracks emissions, permit requirements, flags exceedances and reporting deadlines' },
      { name: 'Demand Forecast Agent', role: 'Generates load forecasts from weather, historical patterns, and economic indicators' },
      { name: 'Safety Compliance Agent', role: 'Monitors OSHA requirements, tracks incidents, near-misses, and training completion' },
    ],
    workflows: [
      {
        name: 'Storm Response',
        description: 'End-to-end storm response from detection through post-storm review.',
        steps: ['Detect weather', 'Pre-position crews', 'Monitor outages', 'Prioritize restoration', 'Dispatch', 'Track', 'Notify customers', 'Post-storm review'],
      },
      {
        name: 'Planned Outage Coordination',
        description: 'Coordinate planned outages with customer notification and crew management.',
        steps: ['Schedule window', 'Notify customers', 'Coordinate crews', 'Execute', 'Test and restore', 'Verify notification', 'Close'],
      },
      {
        name: 'Rate Case Filing',
        description: 'Prepare and manage rate case filings with the public utilities commission.',
        steps: ['Gather cost data', 'Calculate revenue requirement', 'Prepare testimony', 'File with PUC', 'Track discovery', 'Prepare rebuttal', 'Attend hearings'],
      },
      {
        name: 'Environmental Compliance',
        description: 'Monitor emissions and environmental compliance with corrective action tracking.',
        steps: ['Monitor emissions', 'Compare to limits', 'Flag exceedances', 'File reports', 'Track corrective actions', 'Prepare for audit'],
      },
    ],
    strategies: ['reliability-first', 'cost-optimized', 'compliance-first', 'balanced', 'safe-mode'],
    systems: ['microsoft365', 'slack', 'salesforce'],
    automationLevel: 'assisted',
    departments: [
      { name: 'Generation Operations', enabled: true, level: 'assisted' },
      { name: 'Transmission and Distribution', enabled: true, level: 'assisted' },
      { name: 'Field Services', enabled: true, level: 'autonomous' },
      { name: 'Regulatory Affairs', enabled: true, level: 'approval' },
      { name: 'Environmental Compliance', enabled: true, level: 'approval' },
      { name: 'Trading', enabled: true, level: 'approval' },
      { name: 'Safety', enabled: true, level: 'approval' },
    ],
    estimatedMinutes: 35,
  },

  // ── DEFENSE & GOVERNMENT ───────────────────────────────────────────
  {
    id: 'defense',
    name: 'Defense & Government',
    industry: 'Defense & Government',
    description:
      'Mission readiness assessment, logistics optimization, personnel management, and compliance tracking for defense organizations and government agencies.',
    agents: [
      { name: 'Readiness Assessment Agent', role: 'Evaluates unit readiness across personnel, equipment, supply, and training, flags deployment blockers' },
      { name: 'Logistics Optimizer', role: 'Tracks supply chain status, consumption rates, reorder points, predicts shortages from operational tempo' },
      { name: 'Personnel Status Agent', role: 'Monitors individual readiness including medical, dental, training, and security clearance expirations' },
      { name: 'Maintenance Scheduler', role: 'Tracks equipment cycles, parts availability, depot repair timelines, optimizes windows against operations' },
      { name: 'Compliance Monitor', role: 'Tracks NIST 800-171, CMMC, DFARS, ITAR requirements, ATO expirations, STIG compliance' },
      { name: 'Budget Execution Agent', role: 'Tracks obligation and expenditure rates against appropriation timelines, flags underspend and overspend risk' },
      { name: 'Cyber Posture Agent', role: 'Monitors network security metrics, vulnerability scans, incident response, POA&M remediation timelines' },
      { name: 'Training Coordinator', role: 'Tracks individual and unit training requirements, completion rates, identifies gaps impacting readiness' },
    ],
    workflows: [
      {
        name: 'Readiness Assessment',
        description: 'Comprehensive unit readiness evaluation across all readiness pillars.',
        steps: ['Pull C-status data', 'Evaluate personnel', 'Assess equipment', 'Check supply', 'Calculate score', 'Identify gaps', 'Brief commander'],
      },
      {
        name: 'Deployment Preparation',
        description: 'Systematic preparation for deployment with gap remediation tracking.',
        steps: ['Receive order', 'Assess gaps', 'Prioritize remediation', 'Track completion', 'Verify requirements', 'Generate checklist'],
      },
      {
        name: 'ATO Renewal',
        description: 'Authority to Operate renewal with vulnerability assessment and compliance packaging.',
        steps: ['Inventory systems', 'Vulnerability scans', 'STIG compliance', 'Remediate', 'Compile package', 'Submit', 'Track approval'],
      },
      {
        name: 'Budget Execution Review',
        description: 'Track obligation rates against spend plans and project year-end positions.',
        steps: ['Pull obligation data', 'Compare to spend plan', 'Identify variances', 'Project year-end', 'Recommend adjustments', 'Route for approval'],
      },
    ],
    strategies: ['mission-readiness-first', 'compliance-first', 'balanced', 'safe-mode'],
    systems: ['microsoft365', 'slack'],
    automationLevel: 'approval',
    departments: [
      { name: 'Operations', enabled: true, level: 'assisted' },
      { name: 'Logistics', enabled: true, level: 'assisted' },
      { name: 'Personnel', enabled: true, level: 'assisted' },
      { name: 'Maintenance', enabled: true, level: 'autonomous' },
      { name: 'Compliance', enabled: true, level: 'approval' },
      { name: 'Finance', enabled: true, level: 'approval' },
      { name: 'Cyber Security', enabled: true, level: 'approval' },
      { name: 'Training', enabled: true, level: 'assisted' },
    ],
    estimatedMinutes: 35,
  },

  // ── PROFESSIONAL SERVICES ──────────────────────────────────────────
  {
    id: 'professional-services',
    name: 'Professional Services',
    industry: 'Professional Services',
    description:
      'Resource optimization, project profitability tracking, and client engagement management for consulting firms, law firms, and agencies.',
    agents: [
      { name: 'Resource Allocation Optimizer', role: 'Matches consultant skills and availability to projects, flags anyone above 90% utilization for 3+ weeks' },
      { name: 'Project Profitability Monitor', role: 'Tracks actual hours vs budget in real-time, detects margin erosion from scope creep or rate leakage' },
      { name: 'Client Engagement Coordinator', role: 'Prepares meeting briefs, tracks deliverables, monitors satisfaction, flags at-risk accounts' },
      { name: 'Time and Expense Processor', role: 'Validates entries against project codes, flags missing entries, routes expenses for approval' },
      { name: 'Business Development Agent', role: 'Tracks pipeline stages, coordinates proposals, monitors win/loss patterns' },
      { name: 'Knowledge Manager', role: 'Captures lessons learned, indexes methodologies, surfaces prior work for new engagements' },
      { name: 'Bench Management Agent', role: 'Identifies underutilized consultants, matches bench to training, alerts when availability exceeds threshold' },
      { name: 'Contract Monitor', role: 'Tracks terms, billable hour limits, SOW expirations, alerts before overruns' },
    ],
    workflows: [
      {
        name: 'New Engagement Standup',
        description: 'Launch new client engagements from scoping through first milestone.',
        steps: ['Scope review', 'Staff allocation', 'Project plan', 'Kick-off prep', 'Client communication', 'First milestone'],
      },
      {
        name: 'Monthly Client Review',
        description: 'Monthly profitability and deliverable review for active engagements.',
        steps: ['Pull hours and budget', 'Profitability report', 'Deliverable status', 'Draft review deck', 'Schedule meeting'],
      },
      {
        name: 'Proposal Response',
        description: 'End-to-end RFP response from assessment through submission.',
        steps: ['Receive RFP', 'Assess fit', 'Assign team', 'Draft proposal', 'Internal review', 'Pricing approval', 'Submit'],
      },
      {
        name: 'Staffing Gap Resolution',
        description: 'Resolve project understaffing with skill-matched resource allocation.',
        steps: ['Detect understaffing', 'Search available', 'Check skill match', 'Confirm availability', 'Assign', 'Notify PM'],
      },
    ],
    strategies: ['utilization-optimized', 'margin-protected', 'client-satisfaction-first', 'balanced', 'safe-mode'],
    systems: ['salesforce', 'hubspot', 'quickbooks', 'slack', 'microsoft365'],
    automationLevel: 'assisted',
    departments: [
      { name: 'Delivery', enabled: true, level: 'assisted' },
      { name: 'Business Development', enabled: true, level: 'assisted' },
      { name: 'Staffing', enabled: true, level: 'autonomous' },
      { name: 'Finance', enabled: true, level: 'approval' },
      { name: 'Knowledge Management', enabled: true, level: 'autonomous' },
      { name: 'Client Services', enabled: true, level: 'assisted' },
    ],
    estimatedMinutes: 25,
  },
];
