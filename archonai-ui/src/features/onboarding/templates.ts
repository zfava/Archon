import type { OnboardingTemplate } from './types';

export const TEMPLATES: OnboardingTemplate[] = [
  {
    id: 'pool-service',
    name: 'Pool Service',
    industry: 'Field Service',
    description: 'Automated scheduling, route optimization, chemical tracking, and customer communication for pool maintenance companies.',
    agents: [
      { name: 'Route Optimizer', role: 'Plans daily service routes for minimum drive time and maximum stops' },
      { name: 'Chemical Tracker', role: 'Monitors water chemistry readings and recommends treatments' },
      { name: 'Scheduling Agent', role: 'Handles recurring service schedules, cancellations, and rescheduling' },
      { name: 'Customer Comms Agent', role: 'Sends service reminders, completion reports, and follow-ups' },
      { name: 'Invoice Agent', role: 'Generates invoices after service completion and tracks payments' },
      { name: 'Equipment Monitor', role: 'Tracks pump, filter, and heater maintenance cycles' },
    ],
    workflows: [
      {
        name: 'Daily Service Dispatch',
        description: 'Morning route planning through service completion',
        steps: ['Pull schedule', 'Optimize routes', 'Dispatch techs', 'Log readings', 'Send reports'],
      },
      {
        name: 'New Customer Onboarding',
        description: 'Customer intake through first service',
        steps: ['Capture pool info', 'Set schedule', 'Initial assessment', 'Create billing'],
      },
      {
        name: 'Chemical Rebalance',
        description: 'Automated response to out-of-range readings',
        steps: ['Detect anomaly', 'Calculate treatment', 'Alert tech', 'Verify correction'],
      },
    ],
    strategies: ['route-optimized', 'cost-optimized', 'safe-mode'],
    systems: ['quickbooks', 'google-workspace', 'slack'],
    automationLevel: 'assisted',
    departments: [
      { name: 'Operations', enabled: true, level: 'autonomous' },
      { name: 'Scheduling', enabled: true, level: 'autonomous' },
      { name: 'Finance', enabled: true, level: 'assisted' },
      { name: 'Customer Service', enabled: true, level: 'assisted' },
    ],
    estimatedMinutes: 20,
  },
  {
    id: 'pest-control',
    name: 'Pest Control',
    industry: 'Field Service',
    description: 'Service scheduling, treatment tracking, regulatory compliance, and customer retention for pest management companies.',
    agents: [
      { name: 'Treatment Planner', role: 'Selects treatment protocols based on pest type, property, and season' },
      { name: 'Compliance Agent', role: 'Tracks pesticide usage, licensing, and regulatory documentation' },
      { name: 'Scheduling Agent', role: 'Manages recurring treatments, callbacks, and seasonal campaigns' },
      { name: 'Customer Retention Agent', role: 'Identifies at-risk accounts and triggers re-engagement workflows' },
      { name: 'Invoice Agent', role: 'Handles billing cycles, contract renewals, and payment follow-up' },
      { name: 'Inspection Reporter', role: 'Generates detailed inspection reports with photos and findings' },
    ],
    workflows: [
      {
        name: 'Service Call Workflow',
        description: 'From booking through treatment completion',
        steps: ['Receive request', 'Assess pest type', 'Assign tech', 'Apply treatment', 'Document results'],
      },
      {
        name: 'Quarterly Treatment Cycle',
        description: 'Recurring preventive treatment management',
        steps: ['Schedule reminders', 'Confirm appointments', 'Dispatch crew', 'Log chemicals', 'Invoice'],
      },
      {
        name: 'Compliance Audit',
        description: 'Regulatory documentation and reporting',
        steps: ['Gather usage logs', 'Verify licenses', 'Generate reports', 'Submit filings'],
      },
    ],
    strategies: ['compliance-first', 'balanced', 'safe-mode'],
    systems: ['quickbooks', 'google-workspace', 'slack'],
    automationLevel: 'assisted',
    departments: [
      { name: 'Operations', enabled: true, level: 'assisted' },
      { name: 'Scheduling', enabled: true, level: 'autonomous' },
      { name: 'Compliance', enabled: true, level: 'approval' },
      { name: 'Finance', enabled: true, level: 'assisted' },
      { name: 'Customer Service', enabled: true, level: 'assisted' },
    ],
    estimatedMinutes: 25,
  },
  {
    id: 'landscaping',
    name: 'Landscaping',
    industry: 'Field Service',
    description: 'Crew scheduling, job costing, seasonal planning, and property management for landscaping and lawn care businesses.',
    agents: [
      { name: 'Crew Scheduler', role: 'Assigns crews to jobs based on skills, equipment, and location' },
      { name: 'Job Estimator', role: 'Generates quotes from property size, service type, and material costs' },
      { name: 'Route Optimizer', role: 'Plans efficient daily routes across multiple job sites' },
      { name: 'Weather Monitor', role: 'Tracks forecasts and auto-reschedules weather-sensitive work' },
      { name: 'Invoice Agent', role: 'Calculates job costs, generates invoices, and tracks receivables' },
      { name: 'Seasonal Planner', role: 'Plans spring/fall cleanups, aeration, and seasonal service shifts' },
    ],
    workflows: [
      {
        name: 'Daily Crew Dispatch',
        description: 'Morning planning through end-of-day reporting',
        steps: ['Check weather', 'Assign crews', 'Optimize routes', 'Track progress', 'Close out jobs'],
      },
      {
        name: 'New Property Estimate',
        description: 'Lead intake through proposal delivery',
        steps: ['Site assessment', 'Measure property', 'Calculate materials', 'Generate quote', 'Send proposal'],
      },
      {
        name: 'Seasonal Transition',
        description: 'Service mix changes between seasons',
        steps: ['Analyze calendar', 'Update service offerings', 'Notify customers', 'Adjust schedules'],
      },
    ],
    strategies: ['route-optimized', 'cost-optimized', 'balanced'],
    systems: ['quickbooks', 'google-workspace', 'slack'],
    automationLevel: 'assisted',
    departments: [
      { name: 'Operations', enabled: true, level: 'autonomous' },
      { name: 'Scheduling', enabled: true, level: 'autonomous' },
      { name: 'Sales', enabled: true, level: 'assisted' },
      { name: 'Finance', enabled: true, level: 'assisted' },
    ],
    estimatedMinutes: 20,
  },
];
