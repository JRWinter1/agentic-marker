using System.Text.Json;
using AgenticMarker.Agent;
using AgenticMarker.LLM;

namespace AgenticMarker.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();

    public void Register(ITool tool) => _tools[tool.Name] = tool;

    public ITool? GetTool(string name) => _tools.GetValueOrDefault(name);

    public List<ToolDefinition> GetToolDefinitions() =>
        _tools.Values.Select(t => new ToolDefinition(t.Name, t.Description, t.Parameters)).ToList();

    public async Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, AgentState state)
    {
        var tool = GetTool(toolName) ?? throw new InvalidOperationException($"Unknown tool: {toolName}");
        return await tool.ExecuteAsync(arguments, state);
    }
}
