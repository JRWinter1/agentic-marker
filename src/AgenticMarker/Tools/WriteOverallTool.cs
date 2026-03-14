using System.Text.Json;
using AgenticMarker.Agent;

namespace AgenticMarker.Tools;

public class WriteOverallTool : ITool
{
    public string Name => "write_overall";
    public string Description => "Records the overall performance summary and final overall mark for the assignment.";

    public JsonElement Parameters { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "summary": {
                    "type": "string",
                    "description": "A holistic summary of the student's overall performance"
                },
                "overall_mark": {
                    "type": "number",
                    "description": "The final overall mark (0-100) for the assignment"
                }
            },
            "required": ["summary", "overall_mark"]
        }
        """).RootElement.Clone();

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, AgentState state)
    {
        var summary = arguments.GetProperty("summary").GetString() ?? "";
        var overallMark = (int)Math.Round(arguments.GetProperty("overall_mark").GetDouble());

        if (string.IsNullOrWhiteSpace(summary))
            return Task.FromResult(new ToolResult("Error: summary is required.", state));
        if (overallMark < 0 || overallMark > 100)
            return Task.FromResult(new ToolResult("Error: overall_mark must be between 0 and 100.", state));

        var newState = state with { OverallSummary = summary, OverallMark = overallMark };
        return Task.FromResult(new ToolResult($"Overall summary and mark ({overallMark}%) recorded successfully.", newState));
    }
}
