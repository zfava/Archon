using ArchonAI.Core.Models.Memory;

namespace ArchonAI.Core.Interfaces;

public interface IOrganizationalMemoryStore
{
    /// <summary>
    /// Store a strategy, outcome, pattern, or lesson in long-term organizational memory.
    /// Persists in both MemoryStore (for semantic search) and KnowledgeGraph (for relationships).
    /// </summary>
    global::System.Threading.Tasks.Task StoreAsync(
        OrganizationalMemoryEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Semantic search across organizational memory using natural-language query text.
    /// Scores combine semantic relevance, recency, importance, and knowledge graph context.
    /// </summary>
    global::System.Threading.Tasks.Task<OrganizationalMemorySearchResult> SearchAsync(
        string queryText,
        OrganizationalMemoryType? filterType = null,
        string? filterCategory = null,
        int topK = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve entries by type and optional category, ordered by occurrence time.
    /// </summary>
    global::System.Threading.Tasks.Task<OrganizationalTimeline> GetTimelineAsync(
        OrganizationalMemoryType entryType,
        string? category = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze stored memories and extract organizational insights (recurring patterns,
    /// strategy effectiveness, lessons learned).
    /// </summary>
    global::System.Threading.Tasks.Task<OrganizationalMemoryReport> AnalyzeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all entries related to a specific knowledge graph node.
    /// </summary>
    global::System.Threading.Tasks.Task<IReadOnlyList<OrganizationalMemoryEntry>> GetRelatedEntriesAsync(
        string knowledgeNodeId,
        CancellationToken cancellationToken = default);
}
