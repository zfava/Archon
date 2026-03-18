# Memory Layer Model

Technical reference for the enterprise memory hierarchy implementation.

## Domain Model

### MemoryLayer Enum
```
Session = 0       Ephemeral session context, auto-expires
Operational = 1   Active workflow/task state
Organizational = 2  Institutional knowledge, processes, norms
Strategic = 3     Goals, strategies, market intelligence
Relational = 4    Customer/vendor/team relationship context
Financial = 5     Budgets, forecasts, consequence records
```

### EnterpriseMemoryRecord
```
Id, TenantId, Layer, Category, Subject, Content,
Metadata (key-value), LinkedEntities[], Tags[],
Importance (0-1), CreatedBy, CreatedAtUtc, ExpiresAtUtc?
```

### MemoryEntityLink
```
EntityType, EntityId, Relationship
```

### EnterpriseMemoryQueryResult
```
Records[], TotalCount, LayerCounts (layer → count)
```

### EntityMemoryView
```
EntityType, EntityId, Memories[], LayerDistribution (layer → count)
```

## Service Interface

`IEnterpriseMemoryService` with seven operations:

| Method | Purpose |
|--------|---------|
| StoreAsync | Store a record, enforce session TTL |
| GetAsync | Retrieve by ID (tenant-scoped) |
| QueryAsync | Filter by layer/category/tag with faceted counts |
| GetEntityMemoryAsync | Get all memory linked to an entity |
| GetTimelineAsync | Chronological view with optional layer filter |
| DeleteAsync | Remove record (tenant-scoped) |
| ExpireSessionMemoryAsync | Bulk-remove old session memories |

## API Endpoints

All under `/api/v1/enterprise-memory`.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/` | OperatorOrAdmin | Store a memory record |
| GET | `/{recordId}` | GovernanceRead | Get specific record |
| GET | `/` | GovernanceRead | Query with layer/category/tag filters |
| GET | `/entity/{type}/{id}` | GovernanceRead | Entity-centric memory view |
| GET | `/timeline` | GovernanceRead | Chronological memory timeline |
| DELETE | `/{recordId}` | GovernanceWrite | Delete a record |
| POST | `/expire-sessions` | GovernanceWrite | Expire old session memories |

## Session TTL Behavior

- Session-layer records without explicit `ExpiresAtUtc` get a 4-hour default TTL
- Non-session layers never get automatic expiry
- `ExpireSessionMemory` removes session records older than the specified `maxAge`
- Expired records are filtered out of all query results

## Event Bus

Store operations publish layer-specific events:
- `memory.session.stored`
- `memory.operational.stored`
- `memory.organizational.stored`
- `memory.strategic.stored`
- `memory.relational.stored`
- `memory.financial.stored`

## Tests

23 tests in `EnterpriseMemoryTests.cs`:

- Layer classification: all 6 layers preserved on store/retrieve
- Session auto-expiry: default TTL set, non-session skipped
- Query by layer: correct filtering, layer counts returned
- Query by category and tag
- Entity linkage: linked records returned, empty for unlinked
- Tenant isolation: query isolation, get returns null for wrong tenant, delete blocked cross-tenant
- Retrieval/persistence: store, retrieve, null for unknown, delete
- Timeline: chronological ordering
- Session expiry: removes old sessions, keeps other layers
- Event publishing: layer-specific event type

## Frontend

- **Memory Explorer** (`/memory`): Layer-faceted view with colored badges per layer, tag display, entity links, importance indicators
- **Decision detail**: Linked memory section showing all memory records linked to the decision with layer badges
