using System.Text.Json;
using AgenticMarker.Agent;

namespace AgenticMarker.Tools;

public class FinaliseTool : ITool
{
    public string Name => "finalise";
    public string Description => "Validates that all required sections are complete and ends the marking process. Call this as the final step.";

    public JsonElement Parameters { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {},
            "required": []
        }
        """).RootElement.Clone();

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, AgentState state)
    {
        var missing = new List<string>();

        var requiredLOs = new[] { "LO1", "LO2", "LO3", "LO4" };
        foreach (var lo in requiredLOs)
        {
            if (!state.Feedback.ContainsKey(lo))
                missing.Add($"Feedback for {lo}");
        }

        var requiredCriteria = new[] { "Knowledge & Understanding", "Criticality", "Reading and Research", "Writing Style" };
        foreach (var criterion in requiredCriteria)
        {
            if (!state.Marks.ContainsKey(criterion))
                missing.Add($"Mark for '{criterion}'");
        }

        if (string.IsNullOrWhiteSpace(state.OverallSummary))
            missing.Add("Overall summary");
        if (state.OverallMark is null)
            missing.Add("Overall mark");
        if (string.IsNullOrWhiteSpace(state.Feedforward))
            missing.Add("Feedforward");

        if (missing.Count > 0)
        {
            var output = $"Cannot finalise. The following sections are missing:\n- {string.Join("\n- ", missing)}\n\nPlease complete all sections before calling finalise.";
            return Task.FromResult(new ToolResult(output, state));
        }

        var newState = state with { IsComplete = true };
        return Task.FromResult(new ToolResult("Marking finalised successfully. All sections are complete.", newState));
    }
}
