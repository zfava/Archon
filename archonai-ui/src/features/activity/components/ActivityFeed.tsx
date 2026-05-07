import type { AgentActivityEvent, SystemAlert } from '../types';

interface Props {
  events: AgentActivityEvent[];
  alerts: SystemAlert[];
}

function timeAgo(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  if (diff < 5000) return 'now';
  if (diff < 60000) return `${Math.floor(diff / 1000)}s`;
  if (diff < 3600000) return `${Math.floor(diff / 60000)}m`;
  return `${Math.floor(diff / 3600000)}h`;
}

function eventIcon(eventType: string): string {
  switch (eventType.toLowerCase()) {
    case 'taskstarted': case 'started': return '\u25B6';
    case 'taskcompleted': case 'completed': return '\u2713';
    case 'taskfailed': case 'failed': case 'error': return '\u2717';
    case 'registered': return '\u002B';
    case 'deregistered': return '\u2212';
    case 'statuschanged': return '\u21C4';
    default: return '\u2022';
  }
}

function eventClass(eventType: string): string {
  const t = eventType.toLowerCase();
  if (t.includes('fail') || t.includes('error')) return 'sa-feed-icon--error';
  if (t.includes('complet') || t.includes('success')) return 'sa-feed-icon--success';
  if (t.includes('start') || t.includes('register')) return 'sa-feed-icon--info';
  return 'sa-feed-icon--neutral';
}

function severityClass(severity: string): string {
  switch (severity.toLowerCase()) {
    case 'critical': return 'sa-alert--critical';
    case 'warning': return 'sa-alert--warning';
    case 'info': return 'sa-alert--info';
    default: return 'sa-alert--info';
  }
}

export function ActivityFeed({ events, alerts }: Props) {
  const unacknowledged = alerts.filter((a) => !a.isAcknowledged);

  return (
    <section className="sa-panel sa-panel--feed">
      <h2 className="sa-panel-title">Activity & Reasoning</h2>

      {/* Alerts */}
      {unacknowledged.length > 0 && (
        <div className="sa-alerts-section">
          <span className="sa-section-label">Active Alerts</span>
          {unacknowledged.map((alert) => (
            <div key={alert.alertId} className={`sa-alert-card ${severityClass(alert.severity)}`}>
              <div className="sa-alert-top">
                <span className="sa-alert-severity">{alert.severity}</span>
                <span className="sa-alert-comp">{alert.component}</span>
                <span className="sa-alert-time">{timeAgo(alert.raisedAtUtc)}</span>
              </div>
              <p className="sa-alert-msg">{alert.message}</p>
            </div>
          ))}
        </div>
      )}

      {/* Event feed */}
      <div className="sa-feed-section">
        <span className="sa-section-label">Recent Actions</span>
        {events.length === 0 ? (
          <div className="sa-panel-empty">No recent events</div>
        ) : (
          <div className="sa-feed-list">
            {events.map((ev, i) => (
              <div key={`${ev.agentId}-${i}`} className="sa-feed-item">
                <span className={`sa-feed-icon ${eventClass(ev.eventType)}`}>
                  {eventIcon(ev.eventType)}
                </span>
                <div className="sa-feed-body">
                  <div className="sa-feed-headline">
                    <span className="sa-feed-agent">{ev.agentName}</span>
                    <span className="sa-feed-type">{ev.eventType}</span>
                    <span className="sa-feed-time">{timeAgo(ev.occurredAtUtc)}</span>
                  </div>
                  {/* Reasoning summary — "Why this action is happening" */}
                  <p className="sa-feed-reason">{ev.description}</p>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}
