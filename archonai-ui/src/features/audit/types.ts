export interface AuditEntry {
  id: string;
  eventType: string;
  category: string;
  source: string;
  subjectId: string;
  subjectType: string;
  action: string;
  resourceType: string;
  resourceId: string;
  description: string;
  metadata: Record<string, string>;
  checksum: string;
  previousEntryId: string | null;
  occurredAtUtc: string;
}

export interface AuditQueryResult {
  entries: AuditEntry[];
  totalCount: number;
  hasMore: boolean;
  queriedAtUtc: string;
}

export interface AuditLogStatus {
  isActive: boolean;
  totalEntries: number;
  agentActionEntries: number;
  workflowChangeEntries: number;
  userActivityEntries: number;
  latestChecksum: string;
  statusAsOfUtc: string;
}

export interface AuditIntegrityResult {
  integrityValid: boolean;
  verifiedAtUtc: string;
}

export interface AuditFilters {
  category: string;
  subjectId: string;
  resourceType: string;
  search: string;
}
