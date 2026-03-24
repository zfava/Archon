using System.Diagnostics;

namespace ArchonAI.Common.Observability;

/// <summary>
/// Standardized telemetry for model (LLM) invocations across all providers.
/// Emits metrics and creates activity spans for distributed tracing.
/// </summary>
public static class ModelInvocationTelemetry
{
    /// <summary>
    /// Records a successful model invocation with duration, token usage, and provider context.
    /// </summary>
    public static void RecordInvocation(
        string provider,
        string model,
        double durationMs,
        int inputTokens = 0,
        int outputTokens = 0,
        string? correlationId = null,
        string? agentId = null,
        string? taskType = null)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("provider", provider),
            new("model", model),
        };

        Telemetry.ModelInvocationsTotal.Add(1, tags);
        Telemetry.ModelInvocationDurationMs.Record(durationMs, tags);

        if (inputTokens > 0)
        {
            Telemetry.ModelInvocationTokensInput.Record(inputTokens, tags);
            Telemetry.ModelInvocationTokensTotal.Add(inputTokens, tags);
        }
        if (outputTokens > 0)
        {
            Telemetry.ModelInvocationTokensOutput.Record(outputTokens, tags);
            Telemetry.ModelInvocationTokensTotal.Add(outputTokens, tags);
        }

        ModelProviderHealthCheck.RecordSuccess(provider);
    }

    /// <summary>
    /// Records a failed model invocation.
    /// </summary>
    public static void RecordFailure(
        string provider,
        string model,
        double durationMs,
        string errorType,
        bool isTimeout = false)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("provider", provider),
            new("model", model),
            new("error_type", errorType),
        };

        Telemetry.ModelInvocationsFailed.Add(1, tags);
        Telemetry.ModelInvocationDurationMs.Record(durationMs, tags);

        if (isTimeout)
            Telemetry.ModelInvocationsTimedOut.Add(1, tags);

        ModelProviderHealthCheck.RecordFailure(provider);
    }

    /// <summary>
    /// Records a model invocation retry.
    /// </summary>
    public static void RecordRetry(string provider, string model, int attempt)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("provider", provider),
            new("model", model),
            new("attempt", attempt),
        };

        Telemetry.ModelInvocationsRetried.Add(1, tags);
    }

    /// <summary>
    /// Creates a tracing Activity span for a model invocation.
    /// Dispose the returned Activity when the invocation completes.
    /// </summary>
    public static Activity? StartInvocationSpan(
        string provider,
        string model,
        string? correlationId = null,
        string? agentId = null)
    {
        var activity = Telemetry.ActivitySource.StartActivity(
            $"model.invoke {provider}/{model}",
            ActivityKind.Client);

        if (activity is not null)
        {
            activity.SetTag("model.provider", provider);
            activity.SetTag("model.name", model);
            if (correlationId is not null) activity.SetTag("correlation.id", correlationId);
            if (agentId is not null) activity.SetTag("agent.id", agentId);
        }

        return activity;
    }
}
