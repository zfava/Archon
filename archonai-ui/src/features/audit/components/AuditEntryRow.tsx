import { useState } from 'react';
import type { AuditEntry } from '../types';

interface Props {
  entry: AuditEntry;
}

function categoryColor(category: string): string {
  switch (category.toLowerCase()) {
    case 'agent': return '#6366f1';
    case 'workflow': return '#8b5cf6';
    case 'user': return '#06b6d4';
    case 'system': return '#71717a';
    case 'security': return '#f97316';
    default: return '#52525b';
  }
}

function formatTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
}

export function AuditEntryRow({ entry }: Props) {
  const [expanded, setExpanded] = useState(false);
  const color = categoryColor(entry.category);
  const metaEntries = Object.entries(entry.metadata);

  return (
    <div className="al-entry" onClick={() => setExpanded(!expanded)}>
      <div className="al-entry-main">
        {/* Timestamp */}
        <div className="al-entry-time">
          <span className="al-time-clock">{formatTime(entry.occurredAtUtc)}</span>
          <span className="al-time-date">{formatDate(entry.occurredAtUtc)}</span>
        </div>

        {/* Category badge */}
        <span className="al-cat-badge" style={{ background: `${color}20`, color }}>
          {entry.category}
        </span>

        {/* Description */}
        <span className="al-entry-desc">{entry.description}</span>

        {/* Action */}
        <span className="al-entry-action">{entry.action}</span>

        {/* Subject */}
        <span className="al-entry-subject" title={entry.subjectId}>
          {entry.subjectType}:{entry.subjectId.slice(0, 8)}
        </span>

        {/* Expand chevron */}
        <svg
          className={`al-entry-chevron ${expanded ? 'al-entry-chevron--open' : ''}`}
          width="12" height="12" viewBox="0 0 24 24"
          fill="none" stroke="currentColor" strokeWidth="2"
        >
          <path d="M9 18l6-6-6-6" />
        </svg>
      </div>

      {/* Expanded detail */}
      {expanded && (
        <div className="al-entry-detail">
          <div className="al-detail-grid">
            <div className="al-detail-item">
              <span className="al-detail-label">Event Type</span>
              <span className="al-detail-value">{entry.eventType}</span>
            </div>
            <div className="al-detail-item">
              <span className="al-detail-label">Source</span>
              <span className="al-detail-value">{entry.source}</span>
            </div>
            <div className="al-detail-item">
              <span className="al-detail-label">Subject</span>
              <span className="al-detail-value">{entry.subjectType} / {entry.subjectId}</span>
            </div>
            <div className="al-detail-item">
              <span className="al-detail-label">Resource</span>
              <span className="al-detail-value">{entry.resourceType} / {entry.resourceId}</span>
            </div>
            <div className="al-detail-item">
              <span className="al-detail-label">Entry ID</span>
              <span className="al-detail-value al-mono">{entry.id}</span>
            </div>
            <div className="al-detail-item">
              <span className="al-detail-label">Previous Entry</span>
              <span className="al-detail-value al-mono">{entry.previousEntryId ?? 'none (genesis)'}</span>
            </div>
            <div className="al-detail-item al-detail-item--full">
              <span className="al-detail-label">Checksum (SHA-256)</span>
              <span className="al-detail-value al-mono al-checksum">{entry.checksum}</span>
            </div>
          </div>

          {metaEntries.length > 0 && (
            <div className="al-detail-meta">
              <span className="al-detail-label">Metadata</span>
              <div className="al-meta-grid">
                {metaEntries.map(([k, v]) => (
                  <div key={k} className="al-meta-pair">
                    <span className="al-meta-key">{k}</span>
                    <span className="al-meta-val">{v}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
