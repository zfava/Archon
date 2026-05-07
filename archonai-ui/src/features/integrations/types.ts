export type ConnectorCategory = 'crm' | 'erp' | 'finance' | 'marketing' | 'messaging' | 'productivity' | 'support';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'error';

export interface ConnectorInfo {
  id: string;
  name: string;
  category: ConnectorCategory;
  description: string;
  features: string[];
  status: ConnectionStatus;
  lastSyncedAt: string | null;
  totalRequests: number;
  failedRequests: number;
  rateLimitRemaining: number | null;
  errorMessage: string | null;
}

export interface MarketplaceState {
  connectors: ConnectorInfo[];
  loading: boolean;
  error: string | null;
  activeCategory: ConnectorCategory | 'all';
  searchQuery: string;
}

export interface ConnectorStatusDetail {
  isConnected: boolean;
  instanceUrl?: string;
  lastAuthenticatedAtUtc?: string;
  totalRequests: number;
  failedRequests: number;
  rateLimitRemaining: number;
  statusAsOfUtc: string;
}
