# Memory API Guide

Operator-facing reference for the ArchonAI memory subsystem. Maps three API route groups to the six-layer enterprise memory hierarchy.

---

## Six-Layer Memory Model

| Layer | Enum | Retention | Purpose | Example Content |
|-------|------|-----------|---------|-----------------|
| **Session** | 0 | Ephemeral (4h default TTL) | Current user/agent context | Chat history, active filters, draft states |
| **Operational** | 1 | Active workflow lifespan | Running task state, recent actions | Workflow step results, queue positions, error logs |
| **Organizational** | 2 | Long-lived, manually managed | Institutional knowledge | Process definitions, team capabilities, norms |
| **Strategic** | 3 | Persistent | Long-horizon plans and intelligence | Goals, strategy evaluations, competitive analysis |
| **Relational** | 4 | Persistent | Relationship context | Customer preferences, vendor terms, team dynamics |
| **Financial** | 5 | Persistent, auditable | Financial state and consequence tracking | Budget allocations, forecast snapshots |

### Key Behaviors

- **Session auto-expiry**: Session-layer records without explicit `ExpiresAtUtc` get a 4-hour default TTL. Non-session layers never auto-expire.
- **Layer-specific events**: Each store publishes `memory.<layer>.stored` (e.g., `memory.financial.stored`).
- **Query faceting**: All queries return `LayerCounts` showing distribution of matches across layers.
- **Importance scoring**: Each record carries an importance weight (0-1) influencing retrieval priority.

---

## API Route Groups

### 1. `/api/v1/memory/` — Memory Retrieval & Optimization

Low-level memory operations: search, compression, deduplication, clustering, and index management.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/status` | Memory retrieval optimizer status |
| `POST` | `/search` | Semantic search with embedding query |
| `POST` | `/compress` | Compress memory in a scope |
| `POST` | `/deduplicate` | Remove duplicate memory entries |
| `POST` | `/cluster` | Cluster memory records |
| `POST` | `/summarize` | Create summaries from source records |
| `POST` | `/index/rebuild` | Rebuild memory index for a scope |

**Auth**: `OperatorOrAdmin`

**When to use**: Direct memory maintenance — rebuilding indices after bulk imports, deduplicating after migrations, searching across all scopes with raw embedding vectors.

---

### 2. `/api/v1/org-memory/` — Organizational Memory

Dual-persistence store backed by both `MemoryStore` (vector search) and `KnowledgeGraph` (relationship traversal). Maps to the **Organizational** and **Strategic** layers.

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/store` | Store organizational memory entry |
| `POST` | `/search` | Search with query text and optional type filter |
| `GET` | `/timeline/{entryType}` | Chronological view by entry type |
| `POST` | `/analyze` | Analyze organizational memory |
| `GET` | `/related/{knowledgeNodeId}` | Get entries linked to a knowledge node |

**Auth**: `OperatorOrAdmin`

**When to use**: Working with institutional knowledge — strategies, outcomes, patterns, lessons learned. The `/related` endpoint traverses knowledge graph connections to surface contextually linked entries.

---

### 3. `/api/v1/enterprise-memory/` — Enterprise Memory Hierarchy

Full six-layer memory with entity linkage, layer faceting, and session TTL management.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/` | OperatorOrAdmin | Store a memory record (any layer) |
| `GET` | `/{recordId}` | GovernanceRead | Retrieve a specific record by ID |
| `GET` | `/` | GovernanceRead | Query with layer/category/tag filters |
| `GET` | `/entity/{entityType}/{entityId}` | GovernanceRead | Entity-centric memory view |
| `GET` | `/timeline` | GovernanceRead | Chronological memory timeline |
| `DELETE` | `/{recordId}` | GovernanceWrite | Delete a record |
| `POST` | `/expire-sessions` | GovernanceWrite | Bulk-remove old session memories |

**When to use**: The primary API for six-layer memory. Use for storing decision context, querying by entity, reviewing memory timelines, and managing session cleanup.

---

## Entity Linkage

Every memory record can link to first-class entities:

```
MemoryEntityLink {
  EntityType: "decision" | "workflow" | "customer" | "team" | "vendor" | "goal" | "action" | ...
  EntityId: "<uuid>"
  Relationship: "informed_by" | "about" | "resulted_from" | "impacts" | ...
}
```

This enables entity-centric queries: "show me everything linked to decision X" — with layer distribution showing whether knowledge is operational, strategic, relational, etc.

---

## Integration Points

| System | Linkage |
|--------|---------|
| **Decision Engine** | Decision detail shows linked memory with layer badges. Entity type `decision`. |
| **Workflows** | Workflow execution stores operational memory. Entity type `workflow`. |
| **Financial Consequence** | Consequence records mirrored as financial-layer memory. Entity type `financial_consequence`. |
| **Outcome Learning** | Outcome records create organizational memory (lessons learned). Entity type `outcome`. |
| **Trust Tiers** | Strategic-layer memory can inform trust tier evaluations (higher confidence context). |

---

## Route Group → Layer Mapping

| Route Group | Primary Layers | Storage Backend |
|-------------|---------------|-----------------|
| `/memory/` | All (raw access) | MemoryStore (vector) |
| `/org-memory/` | Organizational, Strategic | MemoryStore + KnowledgeGraph |
| `/enterprise-memory/` | All six layers | EnterpriseMemoryStore (PostgreSQL) |

---

## Frontend

- **Memory Explorer** (`/memory`): Layer-faceted view with colored badges per layer, tag display, entity links, importance indicators.
- **Decision Detail**: Linked memory section showing all memory records tied to the decision with layer badges.
