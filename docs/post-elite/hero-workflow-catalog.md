# Hero Workflow Catalog

## 1. Vendor Selection

| Property | Value |
|---|---|
| Type | `vendor-selection` |
| Category | Strategic |
| Domain | Procurement |
| Steps | 7 |

### Description

End-to-end vendor evaluation: create decision, model financial consequences, evaluate trust tier, obtain approval, execute selection, record outcome, store institutional memory.

### Step Sequence

| # | Step | Subsystem | Requires Input |
|---|---|---|---|
| 1 | Create Decision | Decision Engine | Yes |
| 2 | Model Consequences | Financial Consequence Engine | Yes |
| 3 | Evaluate Trust Tier | Trust-Tiered Autonomy | No |
| 4 | Obtain Approval | Governance / Approvals | No |
| 5 | Execute Decision | Decision Engine + Outcome Learning | No |
| 6 | Record Outcome | Outcome Learning | Yes |
| 7 | Store Memory | Enterprise Memory | No |

### Key Inputs

| Input | Description | Example |
|---|---|---|
| `decisionTitle` | Title for the vendor selection decision | "Select cloud provider" |
| `domain` | Business domain | "procurement" |
| `riskLevel` | Risk classification (Low/Medium/High/Critical) | "High" |
| `expectedValue` | Expected monetary value | "500000" |
| `revenueImpactHigh` | Upper bound of revenue impact | "1000000" |
| `costImpactLow` | Lower bound of cost impact | "200000" |

### Demonstrates

- Decision creation with alternatives and risk assessment
- Financial consequence modeling with revenue/cost ranges
- Trust tier evaluation determining autonomy level
- Approval gate routing based on trust disposition
- Expected outcome recording at execution time
- Actual outcome capture with variance computation
- Institutional memory for future procurement decisions

---

## 2. Revenue Forecast Override

| Property | Value |
|---|---|
| Type | `revenue-forecast-override` |
| Category | Strategic |
| Domain | Finance |
| Steps | 7 |

### Description

Override a revenue forecast: create decision, model financial impact, evaluate trust, approve, execute the override, record forecast vs. actual, store memory.

### Step Sequence

| # | Step | Subsystem | Requires Input |
|---|---|---|---|
| 1 | Create Decision | Decision Engine | Yes |
| 2 | Model Consequences | Financial Consequence Engine | Yes |
| 3 | Evaluate Trust Tier | Trust-Tiered Autonomy | No |
| 4 | Obtain Approval | Governance / Approvals | No |
| 5 | Execute Override | Decision Engine + Outcome Learning | No |
| 6 | Record Outcome | Outcome Learning | Yes |
| 7 | Store Memory | Enterprise Memory | No |

### Key Inputs

| Input | Description | Example |
|---|---|---|
| `decisionTitle` | Title for the forecast override | "Q2 revenue forecast adjustment" |
| `domain` | Business domain | "finance" |
| `revenueImpactLow` | Lower bound of revenue adjustment | "-200000" |
| `revenueImpactHigh` | Upper bound of revenue adjustment | "500000" |
| `expectedValue` | Expected net impact | "150000" |

### Demonstrates

- Financial decision governance for forecast changes
- Impact quantification with revenue ranges
- Trust-tier gating for high-value financial changes
- Forecast accuracy tracking via outcome calibration
- Financial institutional memory

---

## 3. Compliance Exception Resolution

| Property | Value |
|---|---|
| Type | `compliance-exception-resolution` |
| Category | Compliance |
| Domain | Compliance |
| Steps | 8 |

### Description

Detect and resolve a compliance exception: raise exception, create remediation decision, model financial exposure, evaluate trust, approve remediation, execute, record outcome, store memory.

### Step Sequence

| # | Step | Subsystem | Requires Input |
|---|---|---|---|
| 1 | Raise Exception | Exception Intelligence | Yes |
| 2 | Create Decision | Decision Engine | Yes |
| 3 | Model Exposure | Financial Consequence Engine | Yes |
| 4 | Evaluate Trust Tier | Trust-Tiered Autonomy | No |
| 5 | Approve Remediation | Governance / Approvals | No |
| 6 | Execute Remediation | Decision Engine + Outcome Learning | No |
| 7 | Record Outcome | Outcome Learning | Yes |
| 8 | Store Memory | Enterprise Memory | No |

### Key Inputs

| Input | Description | Example |
|---|---|---|
| `exceptionTitle` | Title for the compliance exception | "GDPR data retention violation" |
| `exceptionSeverity` | Severity (Info/Warning/High/Critical) | "Critical" |
| `exceptionCategory` | Category of exception | "PolicyViolation" |
| `decisionTitle` | Remediation decision title | "Implement data retention cleanup" |
| `domain` | Business domain | "compliance" |
| `downsideRisk` | Financial exposure from non-compliance | "500000" |

### Demonstrates

- Exception detection and classification
- Remediation decision linked to exception artifact
- Financial exposure quantification for compliance gaps
- Governed remediation approval workflow
- Remediation effectiveness tracking
- Compliance pattern memory for future prevention

---

## Cross-Workflow Patterns

All three workflows share these architectural patterns:

1. **Decision as anchor**: Every workflow creates a `DecisionRecord` that serves as the central artifact, linking all downstream objects
2. **Financial quantification**: Every workflow attaches a `FinancialConsequence` model to the decision, ensuring economic visibility
3. **Trust-tier gating**: Every workflow evaluates the trust tier before proceeding, respecting tenant-configured autonomy boundaries
4. **Governed execution**: High-risk actions route through approval gates; auto-execution only when trust policy allows
5. **Outcome calibration**: Every workflow records predicted vs. actual outcomes, feeding the calibration intelligence system
6. **Institutional memory**: Every workflow stores a memory record, building organizational knowledge over time
7. **Artifact linkage**: Each step produces artifacts tracked in the workflow instance, enabling full traceability

## Executive Visibility

All artifacts created by hero workflows are visible in the Executive Command view:

- Decisions appear in the calibration section
- Exceptions appear in the "What Needs Attention" section
- Pending approvals appear in the "Awaiting Your Approval" section
- Outcomes feed the decision hit rate signal
- Bottlenecks and KPIs from the operational twin remain connected
