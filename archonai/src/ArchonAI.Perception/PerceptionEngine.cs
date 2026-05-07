using System.Text.RegularExpressions;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Perception;
using Microsoft.Extensions.Options;

namespace ArchonAI.Perception;

public sealed partial class PerceptionEngine : IPerceptionEngine
{
    private readonly PerceptionOptions _options;

    public PerceptionEngine(IOptions<PerceptionOptions> options)
    {
        _options = options.Value;
    }

    public global::System.Threading.Tasks.Task<PerceptionResult> ProcessObjectiveAsync(
        Objective objective,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedTitle = NormalizeText(objective.Title);
        string normalizedDescription = NormalizeText(objective.Description);

        var removedNoiseTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        normalizedDescription = FilterNoise(normalizedDescription, removedNoiseTokens);

        var normalizedConstraints = objective.Constraints
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(
                kv => NormalizeConstraintKey(kv.Key),
                kv =>
                {
                    string cleaned = NormalizeText(kv.Value);
                    return FilterNoise(cleaned, removedNoiseTokens);
                },
                StringComparer.OrdinalIgnoreCase);

        var normalizedObjective = objective with
        {
            Title = normalizedTitle,
            Description = normalizedDescription,
            Constraints = normalizedConstraints
        };

        List<string> validationErrors = Validate(normalizedObjective);
        IReadOnlyDictionary<string, string> extractedContext = ExtractContext(normalizedObjective);

        var result = new PerceptionResult(
            NormalizedObjective: normalizedObjective,
            IsValid: validationErrors.Count == 0,
            ValidationErrors: validationErrors,
            ExtractedContext: extractedContext,
            RemovedNoiseTokens: removedNoiseTokens.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            ProcessedAtUtc: DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    private List<string> Validate(Objective objective)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(objective.Title))
        {
            errors.Add("Objective title is required.");
        }

        if (string.IsNullOrWhiteSpace(objective.Description))
        {
            errors.Add("Objective description is required.");
        }

        if (objective.Title.Length > _options.MaxTitleLength)
        {
            errors.Add($"Objective title exceeds max length of {_options.MaxTitleLength}.");
        }

        if (objective.Description.Length > _options.MaxDescriptionLength)
        {
            errors.Add($"Objective description exceeds max length of {_options.MaxDescriptionLength}.");
        }

        foreach (string requiredConstraint in _options.RequiredConstraintKeys)
        {
            if (!objective.Constraints.ContainsKey(requiredConstraint))
            {
                errors.Add($"Required constraint '{requiredConstraint}' is missing.");
            }
        }

        return errors;
    }

    private static IReadOnlyDictionary<string, string> ExtractContext(Objective objective)
    {
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["objectiveType"] = objective.Constraints.GetValueOrDefault("objectivetype", "default"),
            ["source"] = objective.Constraints.GetValueOrDefault("source", "external"),
            ["priority"] = objective.Constraints.GetValueOrDefault("priority", "balanced")
        };

        if (objective.Description.Contains("compliance", StringComparison.OrdinalIgnoreCase))
        {
            context["complianceSignal"] = "true";
        }

        if (objective.Description.Contains("latency", StringComparison.OrdinalIgnoreCase) ||
            objective.Description.Contains("throughput", StringComparison.OrdinalIgnoreCase))
        {
            context["performanceSignal"] = "true";
        }

        return context;
    }

    private string FilterNoise(string input, ISet<string> removedNoiseTokens)
    {
        string output = input;
        foreach (string noise in _options.NoiseTokens.Where(token => !string.IsNullOrWhiteSpace(token)))
        {
            var pattern = $"\\b{Regex.Escape(noise)}\\b";
            bool hasMatch = Regex.IsMatch(output, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!hasMatch)
            {
                continue;
            }

            removedNoiseTokens.Add(noise.Trim());
            output = Regex.Replace(output, pattern, string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return NormalizeText(output);
    }

    private static string NormalizeConstraintKey(string key)
    {
        return NormalizeText(key).Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        return MultiWhitespaceRegex().Replace(trimmed, " ");
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex MultiWhitespaceRegex();
}
