export type OnboardingStep = 'connect' | 'business' | 'automation' | 'review';

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

export interface OnboardingState {
  step: OnboardingStep;
  systems: SystemConnection[];
  businessType: BusinessType | null;
  automation: AutomationConfig;
  deploying: boolean;
  deployed: boolean;
  deployError: string | null;
  estimatedMinutes: number;
}

export interface DeploymentResult {
  success: boolean;
  agentsConfigured: string[];
  strategiesApplied: string[];
  integrationsActive: string[];
  estimatedReadyMinutes: number;
}
