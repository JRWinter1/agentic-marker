using System.Text.Json;
using AgenticMarker.Agent;

namespace AgenticMarker.Tools;

public class AssignMarkTool : ITool
{
    public string Name => "assign_mark";
    public string Description => "Records a numeric mark (0-100) for a specific assessment criterion. Valid criteria: 'Knowledge & Understanding', 'Criticality', 'Reading and Research', 'Writing Style'.";

    public JsonElement Parameters { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "criterion": {
                    "type": "string",
                    "description": "The criterion name, e.g. 'Knowledge & Understanding', 'Criticality', 'Reading and Research', 'Writing Style'"
                },
                "mark": {
                    "type": "number",
                    "description": "The numeric mark (0-100) for this criterion"
                }
            },
            "required": ["criterion", "mark"]
        }
        """).RootElement.Clone();

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, AgentState state)
    {
        var criterion = arguments.GetProperty("criterion").GetString() ?? "";
        var mark = (int)Math.Round(arguments.GetProperty("mark").GetDouble());

        if (string.IsNullOrWhiteSpace(criterion))
            return Task.FromResult(new ToolResult("Error: criterion is required.", state));
        if (mark < 0 || mark > 100)
            return Task.FromResult(new ToolResult("Error: mark must be between 0 and 100.", state));

        var newState = state with { Marks = state.Marks.SetItem(criterion, mark) };
        return Task.FromResult(new ToolResult($"Mark for '{criterion}' recorded: {mark}%.", newState));
    }
}
