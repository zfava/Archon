using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Decisions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresFinancialConsequenceStore : IFinancialConsequenceService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresFinancialConsequenceStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private string TableName => $"{_schema}.financial_consequences";

    public PostgresFinancialConsequenceStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresFinancialConsequenceStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    // ── Initialization ──────────────────────────────────────────────

    private Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return Task.CompletedTask;
        // Table creation is managed by DbUp migrations in ArchonAI.Migrations.
        // See Scripts/007_create_financial_consequences.sql
        _initialized = true;
        return Task.CompletedTask;
    }

    // ── AttachAsync ─────────────────────────────────────────────────

    public async Task<FinancialConsequence> AttachAsync(FinancialConsequence consequence, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {TableName}
                (id, decision_id, tenant_id, expected_revenue_impact_low, expected_revenue_impact_high,
                 expected_cost_impact_low, expected_cost_impact_high, expected_margin_impact,
                 expected_cash_timing_impact, labor_impact, downside_risk, upside_potential,
                 confidence_adjustment, roi_estimate_low, roi_estimate_high, break_even_estimate,
                 assumptions, notes, created_by, created_at_utc, updated_at_utc)
            VALUES
                (@id, @decisionId, @tenantId, @revenueImpactLow, @revenueImpactHigh,
                 @costImpactLow, @costImpactHigh, @marginImpact,
                 @cashTimingImpact, @laborImpact, @downsideRisk, @upsidePotential,
                 @confidenceAdjustment, @roiEstimateLow, @roiEstimateHigh, @breakEvenEstimate,
                 @assumptions::jsonb, @notes, @createdBy, @createdAtUtc, @updatedAtUtc)
        ", conn);

        AddParameters(cmd, consequence);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("FinancialConsequence attached for decision {DecisionId}.", consequence.DecisionId);
        return consequence;
    }

    // ── GetByDecisionAsync ──────────────────────────────────────────

    public async Task<FinancialConsequence?> GetByDecisionAsync(Guid decisionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {TableName} WHERE decision_id = @decisionId", conn);
        cmd.Parameters.AddWithValue("decisionId", decisionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapConsequence(reader) : null;
    }

    // ── UpdateAsync ─────────────────────────────────────────────────

    public async Task<FinancialConsequence?> UpdateAsync(Guid decisionId, FinancialConsequence consequence, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {TableName} SET
                expected_revenue_impact_low  = @revenueImpactLow,
                expected_revenue_impact_high = @revenueImpactHigh,
                expected_cost_impact_low     = @costImpactLow,
                expected_cost_impact_high    = @costImpactHigh,
                expected_margin_impact       = @marginImpact,
                expected_cash_timing_impact  = @cashTimingImpact,
                labor_impact                 = @laborImpact,
                downside_risk                = @downsideRisk,
                upside_potential             = @upsidePotential,
                confidence_adjustment        = @confidenceAdjustment,
                roi_estimate_low             = @roiEstimateLow,
                roi_estimate_high            = @roiEstimateHigh,
                break_even_estimate          = @breakEvenEstimate,
                assumptions                  = @assumptions::jsonb,
                notes                        = @notes,
                updated_at_utc               = @updatedAtUtc
            WHERE decision_id = @decisionId
        ", conn);

        cmd.Parameters.AddWithValue("revenueImpactLow", (object?)consequence.ExpectedRevenueImpactLow ?? DBNull.Value);
        cmd.Parameters.AddWithValue("revenueImpactHigh", (object?)consequence.ExpectedRevenueImpactHigh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("costImpactLow", (object?)consequence.ExpectedCostImpactLow ?? DBNull.Value);
        cmd.Parameters.AddWithValue("costImpactHigh", (object?)consequence.ExpectedCostImpactHigh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("marginImpact", (object?)consequence.ExpectedMarginImpact ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cashTimingImpact", (object?)consequence.ExpectedCashTimingImpact ?? DBNull.Value);
        cmd.Parameters.AddWithValue("laborImpact", (object?)consequence.LaborImpact ?? DBNull.Value);
        cmd.Parameters.AddWithValue("downsideRisk", (object?)consequence.DownsideRisk ?? DBNull.Value);
        cmd.Parameters.AddWithValue("upsidePotential", (object?)consequence.UpsidePotential ?? DBNull.Value);
        cmd.Parameters.AddWithValue("confidenceAdjustment", (object?)consequence.ConfidenceAdjustment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("roiEstimateLow", (object?)consequence.RoiEstimateLow ?? DBNull.Value);
        cmd.Parameters.AddWithValue("roiEstimateHigh", (object?)consequence.RoiEstimateHigh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("breakEvenEstimate", (object?)consequence.BreakEvenEstimate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("assumptions", JsonSerializer.Serialize(consequence.Assumptions, JsonOpts));
        cmd.Parameters.AddWithValue("notes", (object?)consequence.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("updatedAtUtc", consequence.UpdatedAtUtc);
        cmd.Parameters.AddWithValue("decisionId", decisionId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows == 0) return null;

        return await GetByDecisionAsync(decisionId, ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void AddParameters(NpgsqlCommand cmd, FinancialConsequence c)
    {
        cmd.Parameters.AddWithValue("id", c.Id);
        cmd.Parameters.AddWithValue("decisionId", c.DecisionId);
        cmd.Parameters.AddWithValue("tenantId", c.TenantId);
        cmd.Parameters.AddWithValue("revenueImpactLow", (object?)c.ExpectedRevenueImpactLow ?? DBNull.Value);
        cmd.Parameters.AddWithValue("revenueImpactHigh", (object?)c.ExpectedRevenueImpactHigh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("costImpactLow", (object?)c.ExpectedCostImpactLow ?? DBNull.Value);
        cmd.Parameters.AddWithValue("costImpactHigh", (object?)c.ExpectedCostImpactHigh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("marginImpact", (object?)c.ExpectedMarginImpact ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cashTimingImpact", (object?)c.ExpectedCashTimingImpact ?? DBNull.Value);
        cmd.Parameters.AddWithValue("laborImpact", (object?)c.LaborImpact ?? DBNull.Value);
        cmd.Parameters.AddWithValue("downsideRisk", (object?)c.DownsideRisk ?? DBNull.Value);
        cmd.Parameters.AddWithValue("upsidePotential", (object?)c.UpsidePotential ?? DBNull.Value);
        cmd.Parameters.AddWithValue("confidenceAdjustment", (object?)c.ConfidenceAdjustment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("roiEstimateLow", (object?)c.RoiEstimateLow ?? DBNull.Value);
        cmd.Parameters.AddWithValue("roiEstimateHigh", (object?)c.RoiEstimateHigh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("breakEvenEstimate", (object?)c.BreakEvenEstimate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("assumptions", JsonSerializer.Serialize(c.Assumptions, JsonOpts));
        cmd.Parameters.AddWithValue("notes", (object?)c.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdBy", c.CreatedBy);
        cmd.Parameters.AddWithValue("createdAtUtc", c.CreatedAtUtc);
        cmd.Parameters.AddWithValue("updatedAtUtc", c.UpdatedAtUtc);
    }

    private static FinancialConsequence MapConsequence(NpgsqlDataReader r)
    {
        return new FinancialConsequence(
            Id: r.GetGuid(r.GetOrdinal("id")),
            DecisionId: r.GetGuid(r.GetOrdinal("decision_id")),
            TenantId: r.GetGuid(r.GetOrdinal("tenant_id")),
            ExpectedRevenueImpactLow: GetNullableDecimal(r, "expected_revenue_impact_low"),
            ExpectedRevenueImpactHigh: GetNullableDecimal(r, "expected_revenue_impact_high"),
            ExpectedCostImpactLow: GetNullableDecimal(r, "expected_cost_impact_low"),
            ExpectedCostImpactHigh: GetNullableDecimal(r, "expected_cost_impact_high"),
            ExpectedMarginImpact: GetNullableDecimal(r, "expected_margin_impact"),
            ExpectedCashTimingImpact: GetNullableString(r, "expected_cash_timing_impact"),
            LaborImpact: GetNullableString(r, "labor_impact"),
            DownsideRisk: GetNullableDecimal(r, "downside_risk"),
            UpsidePotential: GetNullableDecimal(r, "upside_potential"),
            ConfidenceAdjustment: r.IsDBNull(r.GetOrdinal("confidence_adjustment")) ? null : r.GetDouble(r.GetOrdinal("confidence_adjustment")),
            RoiEstimateLow: GetNullableDecimal(r, "roi_estimate_low"),
            RoiEstimateHigh: GetNullableDecimal(r, "roi_estimate_high"),
            BreakEvenEstimate: GetNullableString(r, "break_even_estimate"),
            Assumptions: JsonSerializer.Deserialize<List<string>>(r.GetString(r.GetOrdinal("assumptions")), JsonOpts) ?? new(),
            Notes: GetNullableString(r, "notes"),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at_utc")),
            UpdatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("updated_at_utc")));
    }

    private static decimal? GetNullableDecimal(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetDecimal(ord);
    }

    private static string? GetNullableString(NpgsqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }
}
