using System.Text.Json;
using AgenticMarker.Agent;

namespace AgenticMarker.Tools;

public class WriteFeedforwardTool : ITool
{
    public string Name => "write_feedforward";
    public string Description => "Records feedforward suggestions — actionable advice for how the student can improve in future work.";

    public JsonElement Parameters { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "feedforward": {
                    "type": "string",
                    "description": "3-5 actionable suggestions for improvement in future assignments"
                }
            },
            "required": ["feedforward"]
        }
        """).RootElement.Clone();

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, AgentState state)
    {
        var feedforward = arguments.GetProperty("feedforward").GetString() ?? "";

        if (string.IsNullOrWhiteSpace(feedforward))
            return Task.FromResult(new ToolResult("Error: feedforward is required.", state));

        var newState = state with { Feedforward = feedforward };
        return Task.FromResult(new ToolResult("Feedforward recorded successfully.", newState));
    }
}
