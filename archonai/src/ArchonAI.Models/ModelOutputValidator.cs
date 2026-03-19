using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ArchonAI.Core.Models.Models;

namespace ArchonAI.Models;

/// <summary>
/// Validates model output against expected schemas and attempts repair of malformed JSON.
/// Tracks validation metrics per intelligence-loop phase.
/// </summary>
public sealed class ModelOutputValidator
{
    private static readonly Meter Meter = new("ArchonAI.Models.Validation", "1.0.0");

    private static readonly Counter<long> ValidCounter =
        Meter.CreateCounter<long>("archonai_model_response_valid", description: "Count of valid model responses");

    private static readonly Counter<long> InvalidCounter =
        Meter.CreateCounter<long>("archonai_model_response_invalid", description: "Count of invalid model responses that could not be repaired");

    private static readonly Counter<long> RepairedCounter =
        Meter.CreateCounter<long>("archonai_model_response_repaired", description: "Count of model responses that required repair");

    /// <summary>
    /// Validates and optionally repairs a model response when JSON output was requested.
    /// Returns the response with SchemaValid set and content repaired if possible.
    /// </summary>
    public ModelResponse ValidateAndRepair(ModelResponse response, string? jsonSchema, string? phase = null)
    {
        if (jsonSchema is null || !response.IsSuccess)
            return response;

        var content = response.Content.Trim();
        var warnings = new List<string>(response.Warnings);
        var tags = new KeyValuePair<string, object?>("phase", phase ?? "unknown");

        // Attempt to parse as JSON
        JsonNode? parsed = TryParseJson(content);

        // If parse fails, attempt repair
        if (parsed is null)
        {
            var repaired = AttemptJsonRepair(content);
            parsed = TryParseJson(repaired);
            if (parsed is not null)
            {
                RepairedCounter.Add(1, tags);
                warnings.Add("JSON_REPAIRED: Model output required repair to produce valid JSON.");
                content = repaired;
            }
            else
            {
                InvalidCounter.Add(1, tags);
                return response with
                {
                    SchemaValid = false,
                    Warnings = warnings.Concat(new[]
                    {
                        "JSON_INVALID: Model output is not valid JSON and could not be repaired."
                    }).ToList(),
                };
            }
        }

        // Validate against schema if provided — lightweight key-presence check
        bool schemaValid = ValidateAgainstSchema(parsed, jsonSchema, out var schemaWarnings);
        warnings.AddRange(schemaWarnings);

        if (schemaValid)
            ValidCounter.Add(1, tags);
        else
            InvalidCounter.Add(1, tags);

        return response with
        {
            Content = content,
            SchemaValid = schemaValid,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Generates a repair prompt that can be sent back to the LLM to fix invalid JSON output.
    /// </summary>
    public static string BuildRepairPrompt(string rawResponse, string jsonSchema)
    {
        return $"""
Your previous response was not valid JSON. Here is what you returned:

{rawResponse}

Please respond with ONLY valid JSON matching this schema:

{jsonSchema}

Do not include markdown code fences, explanations, or any text outside the JSON object.
""";
    }

    private static JsonNode? TryParseJson(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts common JSON repair strategies:
    /// 1. Extract JSON from markdown code blocks (```json ... ``` or ``` ... ```)
    /// 2. Strip trailing commas before } or ]
    /// 3. Fix single-quoted strings to double-quoted
    /// 4. Strip leading/trailing non-JSON text (find first { or [ and last } or ])
    /// </summary>
    internal static string AttemptJsonRepair(string text)
    {
        // Extract from markdown code block
        var codeBlockMatch = Regex.Match(text, @"```(?:json)?\s*\n?([\s\S]*?)\n?```", RegexOptions.Singleline);
        if (codeBlockMatch.Success)
            text = codeBlockMatch.Groups[1].Value.Trim();

        // Strip leading/trailing non-JSON text — find the outermost { } or [ ]
        text = ExtractOutermostJson(text);

        // Strip trailing commas before } or ]
        text = Regex.Replace(text, @",\s*([}\]])", "$1");

        // Fix single-quoted strings (common LLM mistake) — only if no double quotes present in value positions
        if (!text.Contains('"') && text.Contains('\''))
            text = text.Replace('\'', '"');

        return text;
    }

    /// <summary>
    /// Finds the outermost JSON object or array boundaries in text that may have preamble/postamble.
    /// </summary>
    private static string ExtractOutermostJson(string text)
    {
        int objStart = text.IndexOf('{');
        int arrStart = text.IndexOf('[');

        int start;
        char open, close;

        if (objStart < 0 && arrStart < 0)
            return text;

        if (objStart >= 0 && (arrStart < 0 || objStart <= arrStart))
        {
            start = objStart;
            open = '{';
            close = '}';
        }
        else
        {
            start = arrStart;
            open = '[';
            close = ']';
        }

        // Find matching close bracket by counting nesting depth
        int depth = 0;
        bool inString = false;
        bool escape = false;
        int end = -1;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
                continue;

            if (c == open)
                depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    end = i;
                    break;
                }
            }
        }

        if (end > start)
            return text[start..(end + 1)];

        return text[start..];
    }

    /// <summary>
    /// Lightweight schema validation: checks that all required top-level keys from the schema exist.
    /// </summary>
    private static bool ValidateAgainstSchema(JsonNode parsed, string schemaJson, out List<string> warnings)
    {
        warnings = new List<string>();
        try
        {
            var schema = JsonNode.Parse(schemaJson);
            var required = schema?["required"]?.AsArray();
            var properties = schema?["properties"];

            if (required is null || properties is null)
                return true; // No required fields defined — pass

            if (parsed is not JsonObject obj)
            {
                warnings.Add("SCHEMA_MISMATCH: Expected JSON object, got different type.");
                return false;
            }

            bool valid = true;
            foreach (var reqField in required)
            {
                var fieldName = reqField?.GetValue<string>();
                if (fieldName is not null && obj[fieldName] is null)
                {
                    warnings.Add($"SCHEMA_MISSING_FIELD: Required field '{fieldName}' is missing.");
                    valid = false;
                }
            }
            return valid;
        }
        catch (JsonException)
        {
            // Schema itself is malformed — can't validate
            warnings.Add("SCHEMA_PARSE_ERROR: Could not parse validation schema.");
            return false;
        }
    }
}
