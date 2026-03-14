using System.Text.Json;
using AgenticMarker.Agent;

namespace AgenticMarker.Tools;

public class WriteFeedbackTool : ITool
{
    public string Name => "write_feedback";
    public string Description => "Records feedback text for a specific learning outcome. Call this for each of LO1, LO2, LO3, and LO4.";

    public JsonElement Parameters { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "learning_outcome": {
                    "type": "string",
                    "description": "The learning outcome identifier, e.g. 'LO1', 'LO2', 'LO3', 'LO4'"
                },
                "feedback": {
                    "type": "string",
                    "description": "The detailed feedback text for this learning outcome"
                }
            },
            "required": ["learning_outcome", "feedback"]
        }
        """).RootElement.Clone();

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, AgentState state)
    {
        var lo = arguments.GetProperty("learning_outcome").GetString() ?? "";
        var feedback = arguments.GetProperty("feedback").GetString() ?? "";

        if (string.IsNullOrWhiteSpace(lo))
            return Task.FromResult(new ToolResult("Error: learning_outcome is required.", state));
        if (string.IsNullOrWhiteSpace(feedback))
            return Task.FromResult(new ToolResult("Error: feedback is required.", state));

        var newState = state with { Feedback = state.Feedback.SetItem(lo, feedback) };
        return Task.FromResult(new ToolResult($"Feedback for {lo} recorded successfully.", newState));
    }
}
