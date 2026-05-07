using ArchonAI.Core.Interfaces;

namespace ArchonAI.Models;

/// <summary>
/// Provides structured system prompts for all eight phases of the ArchonAI intelligence loop.
/// Each prompt instructs the LLM to return strict JSON with no markdown or preamble.
/// </summary>
public sealed class SystemPromptProvider : ISystemPromptProvider
{
    private static readonly Dictionary<string, PhaseDefinition> Phases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Perception"] = new(
            Prompt: """
You are the Perception module of an enterprise AI operations platform.
Analyze the provided business signals and produce a structured analysis.

You MUST identify anomalies, trends, and opportunities from the input data.
Respond ONLY with valid JSON. No markdown, no explanation, no preamble.

Return a JSON object with this exact structure:
{"anomalies": [{"signal": "string", "severity": "low|medium|high|critical", "confidence": 0.0}], "trends": [{"metric": "string", "direction": "increasing|decreasing|stable", "magnitude": 0.0, "confidence": 0.0}], "opportunities": [{"description": "string", "potential_impact": "string", "confidence": 0.0}]}
""",
            Schema: """
{"type":"object","required":["anomalies","trends","opportunities"],"properties":{"anomalies":{"type":"array","items":{"type":"object","required":["signal","severity","confidence"],"properties":{"signal":{"type":"string"},"severity":{"type":"string","enum":["low","medium","high","critical"]},"confidence":{"type":"number"}}}},"trends":{"type":"array","items":{"type":"object","required":["metric","direction","magnitude","confidence"],"properties":{"metric":{"type":"string"},"direction":{"type":"string","enum":["increasing","decreasing","stable"]},"magnitude":{"type":"number"},"confidence":{"type":"number"}}}},"opportunities":{"type":"array","items":{"type":"object","required":["description","potential_impact","confidence"],"properties":{"description":{"type":"string"},"potential_impact":{"type":"string"},"confidence":{"type":"number"}}}}}}
"""),
        ["GoalGeneration"] = new(
            Prompt: """
You are the Goal Generation module of an enterprise AI operations platform.
Generate prioritized goals based on the provided analysis and organizational context.

Respond ONLY with valid JSON. No markdown, no explanation, no preamble.

Return a JSON object with this exact structure:
{"goals": [{"objective": "string", "success_criteria": "string", "constraints": ["string"], "impact": "low|medium|high|critical", "confidence": 0.0}]}
""",
            Schema: """
{"type":"object","required":["goals"],"properties":{"goals":{"type":"array","items":{"type":"object","required":["objective","success_criteria","constraints","impact","confidence"],"properties":{"objective":{"type":"string"},"success_criteria":{"type":"string"},"constraints":{"type":"array","items":{"type":"string"}},"impact":{"type":"string","enum":["low","medium","high","critical"]},"confidence":{"type":"number"}}}}}}
"""),
        ["Reasoning"] = new(
            Prompt: """
You are the Reasoning module of an enterprise AI operations platform.
Evaluate proposed actions against organizational policies, constraints, and risk tolerance.

Respond ONLY with valid JSON. No markdown, no explanation, no preamble.

Return a JSON object with this exact structure:
{"evaluations": [{"action": "string", "feasibility": "low|medium|high", "risk_level": "low|medium|high|critical", "compliance": "compliant|non_compliant|requires_review", "confidence": 0.0}]}
""",
            Schema: """
{"type":"object","required":["evaluations"],"properties":{"evaluations":{"type":"array","items":{"type":"object","required":["action","feasibility","risk_level","compliance","confidence"],"properties":{"action":{"type":"string"},"feasibility":{"type":"string","enum":["low","medium","high"]},"risk_level":{"type":"string","enum":["low","medium","high","critical"]},"compliance":{"type":"string","enum":["compliant","non_compliant","requires_review"]},"confidence":{"type":"number"}}}}}}
"""),
        ["Simulation"] = new(
            Prompt: """
You are the Simulation module of an enterprise AI operations platform.
Project outcomes from the provided assumptions and current metrics.

Respond ONLY with valid JSON. No markdown, no explanation, no preamble.

Return a JSON object with this exact structure:
{"projections": [{"metric": "string", "baseline": 0.0, "projected": 0.0, "confidence": 0.0, "assumptions": ["string"]}]}
""",
            Schema: """
{"type":"object","required":["projections"],"properties":{"projections":{"type":"array","items":{"type":"object","required":["metric","baseline","projected","confidence","assumptions"],"properties":{"metric":{"type":"string"},"baseline":{"type":"number"},"projected":{"type":"number"},"confidence":{"type":"number"},"assumptions":{"type":"array","items":{"type":"string"}}}}}}}
"""),
        ["TaskGraph"] = new(
            Prompt: """
You are the Task Graph module of an enterprise AI operations platform.
Decompose the given objective into an executable directed acyclic graph of tasks.

Respond ONLY with valid JSON. No markdown, no explanation, no preamble.

Return a JSON object with this exact structure:
{"tasks": [{"id": "string", "description": "string", "dependencies": ["string"], "agent_type": "string", "duration": "string", "rollback": "string"}]}
""",
            Schema: """
{"type":"object","required":["tasks"],"properties":{"tasks":{"type":"array","items":{"type":"object","required":["id","description","dependencies","agent_type","duration","rollback"],"properties":{"id":{"type":"string"},"description":{"type":"string"},"dependencies":{"type":"array","items":{"type":"string"}},"agent_type":{"type":"string"},"duration":{"type":"string"},"rollback":{"type":"string"}}}}}}
"""),
        ["Execution"] = new(
            Prompt: """
You are the Execution module of an enterprise AI operations platform.
Execute the assigned task using available tools and report all actions, results, and side effects.

Respond ONLY with valid JSON. No markdown, no explanation, no preamble.

Return a JSON object with this exact structure:
{"actions_taken": ["string"], "results": ["string"], "side_effects": ["string"], "confidence": 0.0, "blockers": ["string"]}
""",
            Schema: """
{"type":"object","required":["actions_taken","results","side_effects","confidence","blockers"],"properties":{"actions_taken":{"type":"array","items":{"type":"string"}},"results":{"type":"array","items":{"type":"string"}},"side_effects":{"type":"array","items":{"type":"string"}},"confidence":{"type":"number"},"blockers":{"type":"array","items":{"type":"string"}}}}
"""),
        ["OutcomeEvaluation"] = new(
            Prompt: """
You are the Outcome Evaluation module of an enterprise AI operations platform.
Compare actual results against predicted outcomes and quantify the variance.

Respond ONLY with valid JSON. No markdown, no explanation, no preamble.

Return a JSON object with this exact structure:
{"variance_percent": 0.0, "direction": "better|worse|neutral", "root_cause": "string", "recalibration_signal": "string"}
""",
            Schema: """
{"type":"object","required":["variance_percent","direction","root_cause","recalibration_signal"],"properties":{"variance_percent":{"type":"number"},"direction":{"type":"string","enum":["better","worse","neutral"]},"root_cause":{"type":"string"},"recalibration_signal":{"type":"string"}}}
"""),
        ["Learning"] = new(
            Prompt: """
You are the Learning module of an enterprise AI operations platform.
Extract lessons from the completed execution cycle and recommend calibration adjustments.

Respond ONLY with valid JSON. No markdown, no explanation, no preamble.

Return a JSON object with this exact structure:
{"lessons": [{"context": "string", "insight": "string", "confidence": 0.0, "domains": ["string"]}], "calibration_adjustments": ["string"]}
""",
            Schema: """
{"type":"object","required":["lessons","calibration_adjustments"],"properties":{"lessons":{"type":"array","items":{"type":"object","required":["context","insight","confidence","domains"],"properties":{"context":{"type":"string"},"insight":{"type":"string"},"confidence":{"type":"number"},"domains":{"type":"array","items":{"type":"string"}}}}},"calibration_adjustments":{"type":"array","items":{"type":"string"}}}}
"""),
    };

    public string GetPrompt(string phase)
    {
        if (!Phases.TryGetValue(phase, out var definition))
            throw new ArgumentException($"Unknown intelligence loop phase: '{phase}'. Known phases: {string.Join(", ", Phases.Keys)}");
        return definition.Prompt;
    }

    public string GetSchema(string phase)
    {
        if (!Phases.TryGetValue(phase, out var definition))
            throw new ArgumentException($"Unknown intelligence loop phase: '{phase}'. Known phases: {string.Join(", ", Phases.Keys)}");
        return definition.Schema;
    }

    public IReadOnlyList<string> GetPhases() => Phases.Keys.ToList().AsReadOnly();

    private sealed record PhaseDefinition(string Prompt, string Schema);
}
