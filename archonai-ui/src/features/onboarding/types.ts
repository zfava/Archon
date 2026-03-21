export type OnboardingStep = 'template' | 'connect' | 'business' | 'automation' | 'review';

export interface SystemConnection {
  id: string;
  name: string;
  category: 'crm' | 'erp' | 'messaging' | 'finance' | 'productivity' | 'support';
  description: string;
  connected: boolean;
  configuring: boolean;
}

export type BusinessType =
  | 'saas'
  | 'ecommerce'
  | 'healthcare'
  | 'financial-services'
  | 'manufacturing'
  | 'professional-services'
  | 'energy'
  | 'defense'
  | 'pool-service'
  | 'pest-control'
  | 'landscaping'
  | 'other';

export interface BusinessTypeOption {
  id: BusinessType;
  label: string;
  description: string;
  suggestedAgents: string[];
}

export type AutomationLevel = 'approval' | 'assisted' | 'autonomous';

export interface AutomationConfig {
  level: AutomationLevel;
  departments: DepartmentAutomation[];
}

export interface DepartmentAutomation {
  name: string;
  enabled: boolean;
  level: AutomationLevel;
}

export interface ComplianceNote {
  standard: string;
  status: 'enabled' | 'available' | 'roadmap';
  description: string;
}

export interface TrustTierDefault {
  actionScope: string;
  maxTier: string;
  rationale: string;
}

export interface ShadowScenario {
  title: string;
  problem: string;
  detection: string;
  action: string;
  savings: string;
}

export interface OnboardingTemplate {
  id: string;
  name: string;
  industry: string;
  description: string;
  agents: TemplateAgent[];
  workflows: TemplateWorkflow[];
  strategies: string[];
  systems: string[];
  automationLevel: AutomationLevel;
  departments: DepartmentAutomation[];
  estimatedMinutes: number;
  complianceNotes?: ComplianceNote[];
  trustTierDefaults?: TrustTierDefault[];
  shadowScenarios?: ShadowScenario[];
}

export interface TemplateAgent {
  name: string;
  role: string;
}

export interface TemplateWorkflow {
  name: string;
  description: string;
  steps: string[];
}

export interface OnboardingState {
  step: OnboardingStep;
  systems: SystemConnection[];
  businessType: BusinessType | null;
  automation: AutomationConfig;
  deploying: boolean;
  deployed: boolean;
  deployError: string | null;
  estimatedMinutes: number;
  selectedTemplate: OnboardingTemplate | null;
}

export interface DeploymentResult {
  success: boolean;
  agentsConfigured: string[];
  strategiesApplied: string[];
  integrationsActive: string[];
  workflowsCreated: string[];
  estimatedReadyMinutes: number;
}
