# ArchonAI Grafana Dashboards & Alerting

Operational and security dashboards for monitoring the ArchonAI platform, with Grafana alerting rules for critical conditions.

## Dashboards

| Dashboard | File | Purpose |
|-----------|------|---------|
| **Operations** | `dashboards/archonai-operations.json` | System health, agent execution, connector status, governance |
| **Security** | `dashboards/archonai-security.json` | Auth failures, JWT errors, MFA success, cross-tenant blocks |

### Operations Dashboard Panels

**Row 1 — System Health**
- API request rate (req/s) — gauge
- API P95 latency (ms) — stat panel
- API error rate (%) — stat panel, red threshold at >1%
- Active workflows — stat panel

**Row 2 — Agent Execution**
- Task success rate per agent type (Finance, Sales, Ops, Marketing, Support) — bar chart
- Agent execution P95 latency — time series
- Task queue depth — time series
- Active agents — gauge

**Row 3 — Connectors**
- Connector request rate per system (Salesforce, HubSpot, Slack, QuickBooks, M365, Google Workspace) — time series
- Connector error rate (%) — time series with 5% alert threshold
- Circuit breaker state — table (CLOSED/HALF-OPEN/OPEN per connector)

**Row 4 — Governance**
- Pending approvals — stat panel, alert if >10
- Override rate (%) — stat panel
- Policy evaluations/sec — gauge

### Security Dashboard Panels

- Authentication failures/hour — time series with 20/hour alert threshold
- MFA challenge success rate — stat panel
- Cross-tenant access attempts blocked — counter
- JWT validation errors — time series
- Governance approval turnaround time — percentile time series (P50/P95/P99)
- Audit entries recorded — time series
- Audit integrity checks — stat panel
- RBAC policy changes — stat panel

## Alerting Rules

Alert rules are defined in `alerts/archonai-alerts.yaml` using Grafana alerting format.

| Alert | Severity | Condition | Duration |
|-------|----------|-----------|----------|
| `HighAPIErrorRate` | critical | 5xx error rate >1% | 2m |
| `AgentExecutionStalled` | warning | No tasks completing while queue >5 | 5m |
| `GovernanceApprovalBacklog` | warning | >10 pending approvals | 15m |
| `ConnectorCircuitOpen` | critical | Circuit breaker in OPEN state | 1m |
| `WorkerHealthMissing` | critical | Worker service not reporting | 2m |

## Import Methods

### Method 1: Helm Deployment (Recommended)

The Helm chart automatically provisions dashboards via ConfigMap. Dashboards are mounted into Grafana at `/etc/grafana/provisioning/dashboards/`.

```bash
helm upgrade --install archonai deploy/helm/archonai/ \
  --namespace archonai \
  --values deploy/helm/archonai/values.yaml
```

### Method 2: Grafana UI Import

1. Open Grafana (default: `http://localhost:3000`)
2. Navigate to **Dashboards > Import**
3. Click **Upload JSON file**
4. Select `archonai-operations.json` or `archonai-security.json`
5. Choose the Prometheus datasource when prompted
6. Click **Import**

### Method 3: Grafana API

```bash
# Import operations dashboard
curl -X POST http://localhost:3000/api/dashboards/db \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $GRAFANA_API_KEY" \
  -d "{\"dashboard\": $(cat dashboards/archonai-operations.json), \"overwrite\": true}"

# Import security dashboard
curl -X POST http://localhost:3000/api/dashboards/db \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $GRAFANA_API_KEY" \
  -d "{\"dashboard\": $(cat dashboards/archonai-security.json), \"overwrite\": true}"
```

### Method 4: Grafana Provisioning (File-Based)

Copy the dashboards to Grafana's provisioning directory:

```bash
cp dashboards/*.json /etc/grafana/provisioning/dashboards/
```

Ensure your provisioning config (`/etc/grafana/provisioning/dashboards/dashboards.yaml`) includes:

```yaml
apiVersion: 1
providers:
  - name: archonai
    orgId: 1
    folder: ArchonAI
    type: file
    options:
      path: /etc/grafana/provisioning/dashboards
```

## Configuring Alerts

### Grafana Alerting (Unified Alerting)

1. Navigate to **Alerting > Alert rules** in Grafana
2. Click **Import** or create rules manually matching `alerts/archonai-alerts.yaml`
3. Configure notification policies under **Alerting > Notification policies**

### Contact Points

Configure at least one contact point for alert delivery:

- **Slack**: Webhook URL to your `#ops-alerts` channel
- **PagerDuty**: Integration key for critical severity alerts
- **Email**: Distribution list for warning severity alerts

Recommended notification policy routing:

| Severity | Contact Point | Repeat Interval |
|----------|--------------|-----------------|
| critical | PagerDuty + Slack | 5m |
| warning | Slack + Email | 30m |

## Prerequisites

- **Prometheus** datasource configured in Grafana (see `deploy/monitoring/grafana/provisioning/datasources/prometheus.yml`)
- **ArchonAI services** exposing metrics at `/metrics` (Prometheus scrape targets configured in `deploy/monitoring/prometheus/prometheus.yml`)
- Grafana version 10.0+ (for unified alerting support)

## Metric Sources

All metrics use the `ArchonAI` meter defined in `src/ArchonAI.Common/Observability/Telemetry.cs`. In Prometheus, dots in metric names become underscores and counters receive a `_total` suffix (e.g., `archonai.gateway.requests.total` → `archonai_gateway_requests_total`).
