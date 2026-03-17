import { Link } from 'react-router-dom';
import { useControlPanel } from './hooks/useControlPanel';
import { ExecutionModeSelector } from './components/ExecutionModeSelector';
import { DepartmentRules } from './components/DepartmentRules';
import { GovernanceStatus } from './components/GovernanceStatus';
import { EXECUTION_MODES } from './types';
import './control.css';

export function ControlPanel() {
  const {
    loading,
    saving,
    error,
    saved,
    settings,
    policies,
    securityMetrics,
    tenants,
    selectedTenantId,
    setExecutionMode,
    setDepartmentRule,
    selectTenant,
    togglePolicy,
    save,
  } = useControlPanel();

  const activeMode = EXECUTION_MODES.find((m) => m.mode === settings.executionMode);

  if (loading) {
    return (
      <div className="cp-container">
        <div className="cp-loading">
          <div className="phase-spinner" />
          <span>Loading control panel...</span>
        </div>
      </div>
    );
  }

  return (
    <div className="cp-container">
      {/* Header */}
      <header className="cp-header">
        <div className="cp-header-left">
          <Link to="/" className="sg-back-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
            Console
          </Link>
          <h1 className="cp-page-title">Control Panel</h1>

          {/* Runtime mode badge */}
          {activeMode && (
            <span className={`cp-runtime-badge cp-runtime-badge--${activeMode.mode}`}>
              {activeMode.label} Mode
            </span>
          )}
        </div>
        <div className="cp-header-right">
          {/* Tenant selector */}
          {tenants.length > 0 && (
            <div className="cp-tenant-selector">
              <label className="cp-field-label">Tenant</label>
              <select
                className="cp-select"
                value={selectedTenantId}
                onChange={(e) => selectTenant(e.target.value)}
              >
                <option value="default">Default</option>
                {tenants.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.displayName || t.name}
                  </option>
                ))}
              </select>
            </div>
          )}

          {/* Save button */}
          <button
            className={`cp-save-btn ${saved ? 'cp-save-btn--saved' : ''}`}
            onClick={save}
            disabled={saving || saved}
          >
            {saving ? (
              <>
                <div className="phase-spinner" />
                Saving...
              </>
            ) : saved ? (
              <>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                  <path d="M20 6L9 17l-5-5" />
                </svg>
                Saved
              </>
            ) : (
              'Save Settings'
            )}
          </button>
        </div>
      </header>

      {/* Error banner */}
      {error && (
        <div className="cp-error-banner">
          <span>{error}</span>
        </div>
      )}

      {/* Content */}
      <div className="cp-content">
        <div className="cp-main">
          <ExecutionModeSelector
            value={settings.executionMode}
            onChange={setExecutionMode}
          />

          <DepartmentRules
            rules={settings.departmentRules}
            globalMode={settings.executionMode}
            onChange={setDepartmentRule}
          />
        </div>

        <div className="cp-aside">
          <GovernanceStatus
            policies={policies}
            metrics={securityMetrics}
            onTogglePolicy={togglePolicy}
          />
        </div>
      </div>
    </div>
  );
}
