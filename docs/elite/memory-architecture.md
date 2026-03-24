# Memory Architecture

ArchonAI manages knowledge across a six-layer enterprise memory hierarchy. Each layer has distinct retention policies, access patterns, and behavioral characteristics.

## Layer Hierarchy

| Layer | Retention | Purpose | Example Content |
|-------|-----------|---------|-----------------|
| **Session** (L0) | Ephemeral, auto-expires (default 4h) | Current user/agent context | Chat history, active filters, draft states |
| **Operational** (L1) | Active lifespan of related workflows | Running task state, recent actions | Workflow step results, queue positions, error logs |
| **Organizational** (L2) | Long-lived, manually managed | Institutional knowledge | Process definitions, team capabilities, norms, policies |
| **Strategic** (L3) | Persistent | Long-horizon plans and intelligence | Goals, strategy evaluations, competitive analysis, market signals |
| **Relational** (L4) | Persistent | Relationship context | Customer preferences, vendor terms, team dynamics, interaction history |
| **Financial** (L5) | Persistent, auditable | Financial state and consequence tracking | Budget allocations, forecast snapshots, consequence records |

## Behavioral Differences

These layers are not just labels — each has distinct system behavior:

1. **Session memory auto-expires**. A background `ExpireSessionMemory` operation removes session records past their TTL. Non-session layers never auto-expire.

2. **Layer-specific events**. Each store operation publishes `memory.<layer>.stored` events (e.g., `memory.financial.stored`), enabling downstream systems to react differently per layer.

3. **Query faceting**. All queries return `LayerCounts` alongside results, showing the distribution of matching records across layers. This lets operators see at a glance whether knowledge is concentrated in operational vs. strategic layers.

4. **Importance scoring**. Each record carries an importance weight (0-1). Strategic and financial memories typically have higher importance than session memories, influencing retrieval priority.

## Entity Linkage

Every memory record can link to any number of first-class entities:

```
MemoryEntityLink {
  EntityType: "decision" | "workflow" | "customer" | "team" | "vendor" | "goal" | "action" | ...
  EntityId: "<uuid or identifier>"
  Relationship: "informed_by" | "about" | "resulted_from" | "impacts" | ...
}
```

This enables entity-centric views: "show me everything the system knows about this customer" or "what memory is linked to decision X" — with layer distribution showing whether the knowledge is operational, strategic, relational, etc.

## Integration Points

| System | Linkage |
|--------|---------|
| **Decision Engine** | Decision detail shows linked memory with layer badges. Entity type `decision`. |
| **Workflows** | Workflow execution can store operational memory. Entity type `workflow`. |
| **Financial Consequence** | Financial consequence records can be mirrored as financial-layer memory. Entity type `financial_consequence`. |
| **Outcome Learning** | Outcome records create organizational memory (lessons learned). Entity type `outcome`. |
| **Trust Tiers** | Memory layer can inform trust tier evaluations (strategic memory → higher confidence context). |

## Relationship to Existing Memory Systems

The enterprise memory hierarchy builds on top of existing infrastructure:

- **MemoryRecord / IMemoryStore**: Low-level storage with semantic search. Enterprise memory adds layer classification and entity linkage.
- **OrganizationalMemoryStore**: Focuses on strategies/outcomes/patterns/lessons. Maps naturally to the Organizational and Strategic layers.
- **KnowledgeGraphStore**: Graph relationships between entities. Enterprise memory's entity links complement the knowledge graph.

The hierarchy doesn't replace these — it provides a structured classification that makes memory queryable, faceted, and behaviorally distinct by layer.
