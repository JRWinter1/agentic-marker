using System.Text.Json;
using AgenticMarker.Agent;

namespace AgenticMarker.Tools;

public class ReadCriterionTool : ITool
{
    public string Name => "read_criterion";
    public string Description => "Returns the mark band descriptors for a given assessment criterion from the marking brief. Valid criteria: 'Knowledge & Understanding', 'Criticality', 'Reading and Research', 'Writing Style'.";

    public JsonElement Parameters { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "criterion": {
                    "type": "string",
                    "description": "The criterion to read descriptors for, e.g. 'Knowledge & Understanding', 'Criticality', 'Reading and Research', 'Writing Style'"
                }
            },
            "required": ["criterion"]
        }
        """).RootElement.Clone();

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, AgentState state)
    {
        var criterion = arguments.GetProperty("criterion").GetString() ?? "";

        if (string.IsNullOrWhiteSpace(state.MarkingBrief))
            return Task.FromResult(new ToolResult("Error: Marking brief not loaded.", state));

        var output = $"# Criterion: {criterion}\n\n" +
                     $"Refer to the mark band descriptors below and assess the student's work against the '{criterion}' criterion.\n\n" +
                     state.MarkingBrief;

        return Task.FromResult(new ToolResult(output, state));
    }
}
