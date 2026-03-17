import { useState, useCallback, useEffect, useMemo } from 'react';
import { api } from '../../../api/client';
import type {
  ConnectorInfo,
  ConnectorCategory,
  MarketplaceState,
  ConnectionStatus,
  ConnectorStatusDetail,
} from '../types';

const CONNECTORS: Omit<ConnectorInfo, 'status' | 'lastSyncedAt' | 'totalRequests' | 'failedRequests' | 'rateLimitRemaining' | 'errorMessage'>[] = [
  // CRM
  { id: 'salesforce', name: 'Salesforce', category: 'crm', description: 'Enterprise CRM with accounts, contacts, opportunities, and pipeline management.', features: ['Account sync', 'Contact management', 'Opportunity tracking', 'SOQL queries'] },
  { id: 'hubspot', name: 'HubSpot', category: 'crm', description: 'Inbound marketing, sales CRM, and customer service platform.', features: ['Contact sync', 'Deal tracking', 'Pipeline management', 'Marketing automation'] },

  // ERP
  { id: 'erp', name: 'ERP System', category: 'erp', description: 'Enterprise resource planning for orders, inventory, and supply chain.', features: ['Order sync', 'Inventory tracking', 'Supply chain data', 'Resource planning'] },

  // Finance
  { id: 'quickbooks', name: 'QuickBooks', category: 'finance', description: 'Accounting, invoicing, and financial reporting for small to mid-size business.', features: ['Invoice creation', 'Financial reports', 'Transaction history', 'Customer management'] },

  // Messaging
  { id: 'slack', name: 'Slack', category: 'messaging', description: 'Team messaging with channels, alerts, and workflow notifications.', features: ['Send messages', 'Post alerts', 'Channel management', 'Webhook events'] },

  // Productivity
  { id: 'microsoft365', name: 'Microsoft 365', category: 'productivity', description: 'Outlook email, Teams messaging, SharePoint docs, and OneDrive files.', features: ['Email sync', 'Teams messaging', 'SharePoint access', 'OneDrive files'] },
  { id: 'google-workspace', name: 'Google Workspace', category: 'productivity', description: 'Gmail, Google Docs, Sheets, and Drive integration.', features: ['Gmail sync', 'Google Docs', 'Sheets read/write', 'Drive files'] },

  // Marketing
  { id: 'mailchimp', name: 'Mailchimp', category: 'marketing', description: 'Email marketing campaigns, audience management, and analytics.', features: ['Campaign creation', 'Audience segmentation', 'Email analytics', 'Template management'] },
  { id: 'google-ads', name: 'Google Ads', category: 'marketing', description: 'Search and display advertising campaign management and reporting.', features: ['Campaign management', 'Performance reports', 'Budget optimization', 'Keyword tracking'] },

  // Support
  { id: 'zendesk', name: 'Zendesk', category: 'support', description: 'Customer support ticketing, knowledge base, and agent workspace.', features: ['Ticket management', 'Knowledge base', 'Agent assignment', 'SLA tracking'] },
];

function initConnectors(): ConnectorInfo[] {
  return CONNECTORS.map(c => ({
    ...c,
    status: 'disconnected' as ConnectionStatus,
    lastSyncedAt: null,
    totalRequests: 0,
    failedRequests: 0,
    rateLimitRemaining: null,
    errorMessage: null,
  }));
}

export function useIntegrationMarketplace() {
  const [state, setState] = useState<MarketplaceState>({
    connectors: initConnectors(),
    loading: true,
    error: null,
    activeCategory: 'all',
    searchQuery: '',
  });

  // Load statuses on mount
  useEffect(() => {
    let cancelled = false;
    async function loadStatuses() {
      const statusEndpoints: { id: string; path: string }[] = [
        { id: 'salesforce', path: '/connectors/salesforce/status' },
        { id: 'hubspot', path: '/connectors/hubspot/status' },
        { id: 'slack', path: '/connectors/slack/status' },
        { id: 'quickbooks', path: '/connectors/quickbooks/status' },
        { id: 'microsoft365', path: '/connectors/microsoft365/status' },
        { id: 'google-workspace', path: '/connectors/google-workspace/status' },
      ];

      const results = await Promise.allSettled(
        statusEndpoints.map(ep =>
          api.getConnectorStatus(ep.path).then(s => ({ id: ep.id, status: s as ConnectorStatusDetail }))
        )
      );

      if (cancelled) return;

      setState(prev => {
        const updated = prev.connectors.map(c => {
          const match = results.find(
            r => r.status === 'fulfilled' && r.value.id === c.id
          );
          if (match && match.status === 'fulfilled') {
            const s = match.value.status;
            return {
              ...c,
              status: (s.isConnected ? 'connected' : 'disconnected') as ConnectionStatus,
              lastSyncedAt: s.lastAuthenticatedAtUtc ?? null,
              totalRequests: s.totalRequests,
              failedRequests: s.failedRequests,
              rateLimitRemaining: s.rateLimitRemaining,
            };
          }
          return c;
        });
        return { ...prev, connectors: updated, loading: false };
      });
    }

    loadStatuses();
    return () => { cancelled = true; };
  }, []);

  const connect = useCallback(async (connectorId: string) => {
    setState(prev => ({
      ...prev,
      connectors: prev.connectors.map(c =>
        c.id === connectorId ? { ...c, status: 'connecting' as ConnectionStatus, errorMessage: null } : c
      ),
    }));

    try {
      await api.connectIntegration(connectorId);
      setState(prev => ({
        ...prev,
        connectors: prev.connectors.map(c =>
          c.id === connectorId
            ? { ...c, status: 'connected' as ConnectionStatus, lastSyncedAt: new Date().toISOString() }
            : c
        ),
      }));
    } catch (err) {
      setState(prev => ({
        ...prev,
        connectors: prev.connectors.map(c =>
          c.id === connectorId
            ? { ...c, status: 'error' as ConnectionStatus, errorMessage: err instanceof Error ? err.message : 'Connection failed' }
            : c
        ),
      }));
    }
  }, []);

  const disconnect = useCallback(async (connectorId: string) => {
    setState(prev => ({
      ...prev,
      connectors: prev.connectors.map(c =>
        c.id === connectorId ? { ...c, status: 'connecting' as ConnectionStatus } : c
      ),
    }));

    try {
      await api.disconnectIntegration(connectorId);
      setState(prev => ({
        ...prev,
        connectors: prev.connectors.map(c =>
          c.id === connectorId
            ? { ...c, status: 'disconnected' as ConnectionStatus, lastSyncedAt: null, totalRequests: 0, failedRequests: 0, rateLimitRemaining: null, errorMessage: null }
            : c
        ),
      }));
    } catch (err) {
      setState(prev => ({
        ...prev,
        connectors: prev.connectors.map(c =>
          c.id === connectorId
            ? { ...c, status: 'error' as ConnectionStatus, errorMessage: err instanceof Error ? err.message : 'Disconnect failed' }
            : c
        ),
      }));
    }
  }, []);

  const setCategory = useCallback((category: MarketplaceState['activeCategory']) => {
    setState(prev => ({ ...prev, activeCategory: category }));
  }, []);

  const setSearch = useCallback((query: string) => {
    setState(prev => ({ ...prev, searchQuery: query }));
  }, []);

  const filtered = useMemo(() => {
    let list = state.connectors;
    if (state.activeCategory !== 'all') {
      list = list.filter(c => c.category === state.activeCategory);
    }
    if (state.searchQuery.trim()) {
      const q = state.searchQuery.toLowerCase();
      list = list.filter(c =>
        c.name.toLowerCase().includes(q) ||
        c.description.toLowerCase().includes(q) ||
        c.category.includes(q)
      );
    }
    return list;
  }, [state.connectors, state.activeCategory, state.searchQuery]);

  const stats = useMemo(() => {
    const connected = state.connectors.filter(c => c.status === 'connected').length;
    const errored = state.connectors.filter(c => c.status === 'error').length;
    const total = state.connectors.length;
    return { connected, errored, total };
  }, [state.connectors]);

  return {
    state,
    filtered,
    stats,
    connect,
    disconnect,
    setCategory,
    setSearch,
  };
}
