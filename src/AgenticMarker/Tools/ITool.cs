using System.Text.Json;
using AgenticMarker.Agent;

namespace AgenticMarker.Tools;

public record ToolResult(string Output, AgentState State);

public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonElement Parameters { get; }
    Task<ToolResult> ExecuteAsync(JsonElement arguments, AgentState state);
}
