import { useCallback, useEffect, useState } from 'react';
import { api } from '../../../api/client';
import type {
  ControlSettings,
  DepartmentRule,
  ExecutionMode,
  PlatformPolicy,
  SecurityMetrics,
  Tenant,
} from '../types';
import { DEFAULT_SETTINGS } from '../types';

const CONFIG_SCOPE = 'control-panel';
const SETTINGS_KEY = 'execution-settings';

export interface ControlPanelState {
  loading: boolean;
  saving: boolean;
  error: string | null;
  saved: boolean;
  settings: ControlSettings;
  policies: PlatformPolicy[];
  securityMetrics: SecurityMetrics | null;
  tenants: Tenant[];
  selectedTenantId: string;
}

export function useControlPanel() {
  const [state, setState] = useState<ControlPanelState>({
    loading: true,
    saving: false,
    error: null,
    saved: false,
    settings: DEFAULT_SETTINGS,
    policies: [],
    securityMetrics: null,
    tenants: [],
    selectedTenantId: 'default',
  });

  // Load tenants, policies, security metrics, and persisted settings
  const load = useCallback(async (tenantId: string) => {
    setState((s) => ({ ...s, loading: true, error: null }));

    try {
      const [tenantsRes, policiesRes, metricsRes] = await Promise.allSettled([
        api.listTenants() as Promise<Tenant[]>,
        api.listPolicies(tenantId) as Promise<PlatformPolicy[]>,
        api.getSecurityMetrics() as Promise<SecurityMetrics>,
      ]);

      const tenants = tenantsRes.status === 'fulfilled' ? tenantsRes.value : [];
      const policies = policiesRes.status === 'fulfilled' ? policiesRes.value : [];
      const securityMetrics = metricsRes.status === 'fulfilled' ? metricsRes.value : null;

      // Try to load persisted settings
      let settings = DEFAULT_SETTINGS;
      try {
        const config = (await api.getConfig(tenantId, CONFIG_SCOPE, SETTINGS_KEY)) as {
          value: string;
        };
        if (config?.value) {
          settings = JSON.parse(config.value) as ControlSettings;
        }
      } catch {
        // No persisted settings yet — use defaults
      }

      setState((s) => ({
        ...s,
        loading: false,
        tenants,
        policies,
        securityMetrics,
        settings,
        selectedTenantId: tenantId,
      }));
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      setState((s) => ({ ...s, loading: false, error: message }));
    }
  }, []);

  useEffect(() => {
    load(state.selectedTenantId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Persist settings to backend config
  const save = useCallback(async () => {
    setState((s) => ({ ...s, saving: true, error: null, saved: false }));

    try {
      const tenantId = state.selectedTenantId;
      const settings = state.settings;

      // Persist settings via ControlPlane config API
      await api.setConfig({
        tenantId,
        scope: CONFIG_SCOPE,
        key: SETTINGS_KEY,
        value: JSON.stringify(settings),
        description: 'Execution autonomy settings for ControlPanel UI',
      });

      // Sync execution mode to governance policies
      await syncPoliciesToMode(tenantId, settings);

      setState((s) => ({ ...s, saving: false, saved: true }));

      // Reload policies to reflect changes
      try {
        const policies = (await api.listPolicies(tenantId)) as PlatformPolicy[];
        setState((s) => ({ ...s, policies }));
      } catch {
        // Non-critical
      }
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      setState((s) => ({ ...s, saving: false, error: message }));
    }
  }, [state.selectedTenantId, state.settings]);

  // Update execution mode
  const setExecutionMode = useCallback((mode: ExecutionMode) => {
    setState((s) => ({
      ...s,
      saved: false,
      settings: { ...s.settings, executionMode: mode },
    }));
  }, []);

  // Update a department rule
  const setDepartmentRule = useCallback((updated: DepartmentRule) => {
    setState((s) => ({
      ...s,
      saved: false,
      settings: {
        ...s.settings,
        departmentRules: s.settings.departmentRules.map((r) =>
          r.department === updated.department ? updated : r,
        ),
      },
    }));
  }, []);

  // Switch tenant
  const selectTenant = useCallback(
    (tenantId: string) => {
      load(tenantId);
    },
    [load],
  );

  // Toggle a policy
  const togglePolicy = useCallback(async (policyId: string, enabled: boolean) => {
    try {
      await api.updatePolicy(policyId, { isEnabled: enabled });
      setState((s) => ({
        ...s,
        policies: s.policies.map((p) =>
          p.id === policyId ? { ...p, isEnabled: enabled } : p,
        ),
      }));
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      setState((s) => ({ ...s, error: message }));
    }
  }, []);

  return {
    ...state,
    setExecutionMode,
    setDepartmentRule,
    selectTenant,
    togglePolicy,
    save,
    reload: () => load(state.selectedTenantId),
  };
}

/** Sync department rules into PlatformPolicy entries on the backend. */
async function syncPoliciesToMode(
  tenantId: string,
  settings: ControlSettings,
) {
  for (const rule of settings.departmentRules) {
    if (rule.approval === 'inherit') continue;

    const policyName = `autonomy:${rule.department.toLowerCase()}`;
    const rules: Record<string, string> = {
      executionMode: settings.executionMode,
      departmentApproval: rule.approval,
      maxAutoCost: String(rule.maxAutoCost),
      maxAutoRisk: String(rule.maxAutoRisk),
    };

    try {
      await api.createPolicy({
        tenantId,
        name: policyName,
        description: `Execution autonomy policy for ${rule.department}`,
        policyType: 'Workflow',
        targetResource: rule.department.toLowerCase(),
        rules,
        priority: 10,
      });
    } catch {
      // Policy may already exist — that's fine
    }
  }
}
