import { useState, useCallback, useMemo } from 'react';
import { api } from '../../../api/client';
import type {
  OnboardingStep,
  OnboardingState,
  SystemConnection,
  BusinessType,
  AutomationLevel,
  DeploymentResult,
  OnboardingTemplate,
} from '../types';

const STEPS: OnboardingStep[] = ['template', 'connect', 'business', 'automation', 'review'];

const DEFAULT_SYSTEMS: SystemConnection[] = [
  { id: 'salesforce', name: 'Salesforce', category: 'crm', description: 'CRM & sales pipeline', connected: false, configuring: false },
  { id: 'hubspot', name: 'HubSpot', category: 'crm', description: 'Marketing & CRM platform', connected: false, configuring: false },
  { id: 'slack', name: 'Slack', category: 'messaging', description: 'Team messaging & alerts', connected: false, configuring: false },
  { id: 'microsoft365', name: 'Microsoft 365', category: 'productivity', description: 'Email, calendar & docs', connected: false, configuring: false },
  { id: 'google-workspace', name: 'Google Workspace', category: 'productivity', description: 'Gmail, Drive & Calendar', connected: false, configuring: false },
  { id: 'quickbooks', name: 'QuickBooks', category: 'finance', description: 'Accounting & invoicing', connected: false, configuring: false },
  { id: 'erp', name: 'ERP System', category: 'erp', description: 'Enterprise resource planning', connected: false, configuring: false },
  { id: 'zendesk', name: 'Zendesk', category: 'support', description: 'Customer support & ticketing', connected: false, configuring: false },
];

const DEFAULT_DEPARTMENTS = [
  { name: 'Sales', enabled: true, level: 'assisted' as AutomationLevel },
  { name: 'Marketing', enabled: true, level: 'assisted' as AutomationLevel },
  { name: 'Finance', enabled: true, level: 'approval' as AutomationLevel },
  { name: 'Operations', enabled: true, level: 'assisted' as AutomationLevel },
  { name: 'Support', enabled: true, level: 'assisted' as AutomationLevel },
];

export function useOnboardingWizard() {
  const [state, setState] = useState<OnboardingState>({
    step: 'template',
    systems: DEFAULT_SYSTEMS,
    businessType: null,
    automation: { level: 'assisted', departments: DEFAULT_DEPARTMENTS },
    deploying: false,
    deployed: false,
    deployError: null,
    estimatedMinutes: 45,
    selectedTemplate: null,
  });

  const [deployResult, setDeployResult] = useState<DeploymentResult | null>(null);

  const stepIndex = STEPS.indexOf(state.step);
  const canGoNext = useMemo(() => {
    switch (state.step) {
      case 'template': return true;
      case 'connect': return state.systems.some(s => s.connected);
      case 'business': return state.businessType !== null;
      case 'automation': return true;
      case 'review': return !state.deploying;
      default: return false;
    }
  }, [state.step, state.systems, state.businessType, state.deploying]);

  const canGoBack = stepIndex > 0 && !state.deploying && !state.deployed;

  const goNext = useCallback(() => {
    if (stepIndex < STEPS.length - 1) {
      setState(s => ({ ...s, step: STEPS[stepIndex + 1] }));
    }
  }, [stepIndex]);

  const goBack = useCallback(() => {
    if (stepIndex > 0) {
      setState(s => ({ ...s, step: STEPS[stepIndex - 1] }));
    }
  }, [stepIndex]);

  const goToStep = useCallback((step: OnboardingStep) => {
    if (!state.deploying && !state.deployed) {
      setState(s => ({ ...s, step }));
    }
  }, [state.deploying, state.deployed]);

  const selectTemplate = useCallback((template: OnboardingTemplate) => {
    setState(s => ({ ...s, selectedTemplate: template }));
  }, []);

  const skipTemplate = useCallback(() => {
    setState(s => ({ ...s, selectedTemplate: null, step: 'connect' }));
  }, []);

  const autoDeployTemplate = useCallback(async (template: OnboardingTemplate) => {
    setState(s => ({ ...s, deploying: true, deployError: null, selectedTemplate: template }));
    try {
      const result = await api.deployOnboardingTemplate({
        templateId: template.id,
        connectedSystems: template.systems,
        businessType: template.id,
        automationLevel: template.automationLevel,
        departments: template.departments
          .filter(d => d.enabled)
          .map(d => ({ name: d.name, level: d.level })),
        agents: template.agents.map(a => a.name),
        workflows: template.workflows.map(w => ({
          name: w.name,
          steps: w.steps,
        })),
        strategies: template.strategies,
      });
      setDeployResult(result);
      setState(s => ({ ...s, deploying: false, deployed: true, step: 'review' }));
    } catch (err) {
      setState(s => ({
        ...s,
        deploying: false,
        deployError: err instanceof Error ? err.message : 'Template deployment failed',
      }));
    }
  }, []);

  const toggleSystem = useCallback((systemId: string) => {
    setState(s => ({
      ...s,
      systems: s.systems.map(sys =>
        sys.id === systemId
          ? { ...sys, configuring: true }
          : sys
      ),
    }));
    setTimeout(() => {
      setState(s => ({
        ...s,
        systems: s.systems.map(sys =>
          sys.id === systemId
            ? { ...sys, connected: !sys.connected, configuring: false }
            : sys
        ),
      }));
    }, 800);
  }, []);

  const setBusinessType = useCallback((type: BusinessType) => {
    setState(s => ({ ...s, businessType: type }));
  }, []);

  const setAutomationLevel = useCallback((level: AutomationLevel) => {
    setState(s => ({
      ...s,
      automation: {
        ...s.automation,
        level,
        departments: s.automation.departments.map(d => ({ ...d, level })),
      },
    }));
  }, []);

  const setDepartmentLevel = useCallback((name: string, level: AutomationLevel) => {
    setState(s => ({
      ...s,
      automation: {
        ...s.automation,
        departments: s.automation.departments.map(d =>
          d.name === name ? { ...d, level } : d
        ),
      },
    }));
  }, []);

  const toggleDepartment = useCallback((name: string) => {
    setState(s => ({
      ...s,
      automation: {
        ...s.automation,
        departments: s.automation.departments.map(d =>
          d.name === name ? { ...d, enabled: !d.enabled } : d
        ),
      },
    }));
  }, []);

  const deploy = useCallback(async () => {
    setState(s => ({ ...s, deploying: true, deployError: null }));
    try {
      const result = await api.deployOnboarding({
        connectedSystems: state.systems.filter(s => s.connected).map(s => s.id),
        businessType: state.businessType!,
        automationLevel: state.automation.level,
        departments: state.automation.departments
          .filter(d => d.enabled)
          .map(d => ({ name: d.name, level: d.level })),
      });
      setDeployResult(result);
      setState(s => ({ ...s, deploying: false, deployed: true }));
    } catch (err) {
      setState(s => ({
        ...s,
        deploying: false,
        deployError: err instanceof Error ? err.message : 'Deployment failed',
      }));
    }
  }, [state.systems, state.businessType, state.automation]);

  const connectedCount = state.systems.filter(s => s.connected).length;

  return {
    state,
    stepIndex,
    totalSteps: STEPS.length,
    canGoNext,
    canGoBack,
    connectedCount,
    deployResult,
    goNext,
    goBack,
    goToStep,
    selectTemplate,
    skipTemplate,
    autoDeployTemplate,
    toggleSystem,
    setBusinessType,
    setAutomationLevel,
    setDepartmentLevel,
    toggleDepartment,
    deploy,
  };
}
