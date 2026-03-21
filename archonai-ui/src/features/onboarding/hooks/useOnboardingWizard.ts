import { useState, useCallback, useMemo, useEffect } from 'react';
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

const MANUFACTURING_SYSTEMS: SystemConnection[] = [
  { id: 'sap', name: 'SAP ERP', category: 'erp', description: 'Enterprise resource planning for manufacturing', connected: false, configuring: false },
  { id: 'oracle-erp', name: 'Oracle ERP Cloud', category: 'erp', description: 'Cloud ERP for discrete and process manufacturing', connected: false, configuring: false },
  { id: 'mes', name: 'MES / Shop Floor', category: 'erp', description: 'Manufacturing execution system (Rockwell, Siemens, Ignition)', connected: false, configuring: false },
  { id: 'cmms', name: 'CMMS', category: 'erp', description: 'Maintenance management (Maximo, Fiix, UpKeep)', connected: false, configuring: false },
  { id: 'qms', name: 'Quality Management', category: 'erp', description: 'Quality system (InfinityQS, ETQ, MasterControl)', connected: false, configuring: false },
  { id: 'wms', name: 'Warehouse Management', category: 'erp', description: 'Warehouse and inventory management', connected: false, configuring: false },
];

const HEALTHCARE_SYSTEMS: SystemConnection[] = [
  { id: 'epic', name: 'Epic (FHIR)', category: 'erp', description: 'Epic EHR via FHIR API', connected: false, configuring: false },
  { id: 'cerner', name: 'Oracle Health / Cerner', category: 'erp', description: 'Cerner EHR integration', connected: false, configuring: false },
  { id: 'athenahealth', name: 'Athenahealth', category: 'erp', description: 'Practice management and EHR', connected: false, configuring: false },
  { id: 'clearinghouse', name: 'Clearinghouse', category: 'finance', description: 'Claims clearinghouse (Availity, Change Healthcare)', connected: false, configuring: false },
  { id: 'rcm-platform', name: 'RCM Platform', category: 'finance', description: 'Revenue cycle management (Waystar, nThrive)', connected: false, configuring: false },
  { id: 'nurse-scheduling', name: 'Staff Scheduling', category: 'erp', description: 'Nurse and staff scheduling system', connected: false, configuring: false },
];

const FINANCIAL_SERVICES_SYSTEMS: SystemConnection[] = [
  { id: 'bloomberg', name: 'Bloomberg', category: 'erp', description: 'Market data and trading systems', connected: false, configuring: false },
  { id: 'refinitiv', name: 'Refinitiv / LSEG', category: 'erp', description: 'Market data and risk analytics', connected: false, configuring: false },
  { id: 'custodian', name: 'Custodian / Prime Broker', category: 'finance', description: 'Custody and settlement systems', connected: false, configuring: false },
  { id: 'core-banking', name: 'Core Banking', category: 'erp', description: 'Core banking platform (Temenos, FIS, Finastra)', connected: false, configuring: false },
  { id: 'reg-reporting', name: 'Regulatory Reporting', category: 'erp', description: 'Regulatory filing platform (Axiom, Wolters Kluwer)', connected: false, configuring: false },
  { id: 'kyc-platform', name: 'KYC / Screening', category: 'erp', description: 'Identity verification and sanctions screening', connected: false, configuring: false },
];

const ENERGY_SYSTEMS: SystemConnection[] = [
  { id: 'scada', name: 'SCADA / OPC-UA', category: 'erp', description: 'Supervisory control and data acquisition', connected: false, configuring: false },
  { id: 'historian', name: 'Data Historian', category: 'erp', description: 'Process historian (OSIsoft PI, Honeywell PHD)', connected: false, configuring: false },
  { id: 'asset-mgmt', name: 'Asset Management', category: 'erp', description: 'Enterprise asset management (Maximo, SAP PM)', connected: false, configuring: false },
  { id: 'gis', name: 'GIS System', category: 'erp', description: 'Geographic information system for asset mapping', connected: false, configuring: false },
  { id: 'oms', name: 'Outage Management', category: 'erp', description: 'Outage management system', connected: false, configuring: false },
  { id: 'energy-trading', name: 'Energy Trading', category: 'finance', description: 'Energy trading platform (OpenLink, Allegro)', connected: false, configuring: false },
];

export function getSystemsForIndustry(
  businessType: BusinessType | null,
  templateId: string | null,
): SystemConnection[] {
  const industry = templateId === 'manufacturing' ? 'manufacturing'
    : templateId === 'healthcare' ? 'healthcare'
    : templateId === 'financial-services' ? 'financial-services'
    : templateId === 'energy' ? 'energy'
    : businessType;
  if (industry === 'manufacturing') {
    const existingIds = new Set(DEFAULT_SYSTEMS.map(s => s.id));
    const additions = MANUFACTURING_SYSTEMS.filter(s => !existingIds.has(s.id));
    return [...DEFAULT_SYSTEMS, ...additions];
  }
  if (industry === 'healthcare') {
    const existingIds = new Set(DEFAULT_SYSTEMS.map(s => s.id));
    const additions = HEALTHCARE_SYSTEMS.filter(s => !existingIds.has(s.id));
    return [...DEFAULT_SYSTEMS, ...additions];
  }
  if (industry === 'financial-services') {
    const existingIds = new Set(DEFAULT_SYSTEMS.map(s => s.id));
    const additions = FINANCIAL_SERVICES_SYSTEMS.filter(s => !existingIds.has(s.id));
    return [...DEFAULT_SYSTEMS, ...additions];
  }
  if (industry === 'energy') {
    const existingIds = new Set(DEFAULT_SYSTEMS.map(s => s.id));
    const additions = ENERGY_SYSTEMS.filter(s => !existingIds.has(s.id));
    return [...DEFAULT_SYSTEMS, ...additions];
  }
  return DEFAULT_SYSTEMS;
}

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

  // Update systems list when template or businessType changes
  useEffect(() => {
    const templateId = state.selectedTemplate?.id ?? null;
    const updated = getSystemsForIndustry(state.businessType, templateId);
    setState(s => {
      // Preserve connection state for systems that already exist
      const connectedMap = new Map(s.systems.map(sys => [sys.id, sys]));
      const merged = updated.map(sys => connectedMap.get(sys.id) ?? sys);
      // Only update if the set of IDs changed
      const currentIds = s.systems.map(x => x.id).join(',');
      const newIds = merged.map(x => x.id).join(',');
      if (currentIds === newIds) return s;
      return { ...s, systems: merged };
    });
  }, [state.businessType, state.selectedTemplate?.id]);

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
        trustTierDefaults: template.trustTierDefaults?.map(t => ({
          actionScope: t.actionScope,
          maxTier: t.maxTier,
          rationale: t.rationale,
        })),
      });
      setDeployResult(result as DeploymentResult);
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
      setDeployResult(result as DeploymentResult);
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
