using AgenticMarker.LLM;
using AgenticMarker.Tools;

namespace AgenticMarker.Agent;

public class AgentLoop
{
    private readonly OpenRouterClient _llm;
    private readonly ToolRegistry _tools;
    private readonly int _maxIterations;

    public AgentLoop(OpenRouterClient llm, ToolRegistry tools, int maxIterations = 20)
    {
        _llm = llm;
        _tools = tools;
        _maxIterations = maxIterations;
    }

    public async Task<AgentState> RunAsync(string systemPrompt, string userMessage, AgentState state)
    {
        var messages = new List<Message>
        {
            Message.System(systemPrompt),
            Message.User(userMessage)
        };

        var toolDefs = _tools.GetToolDefinitions();
        var iteration = 0;

        while (!state.IsComplete && iteration < _maxIterations)
        {
            iteration++;
            Console.WriteLine($"\n=== Iteration {iteration} ===");

            // Ask the LLM
            Console.WriteLine("Thinking...");
            var response = await _llm.ChatAsync(messages, toolDefs);

            if (response.ToolCalls.Count > 0)
            {
                // Add assistant message with tool calls
                messages.Add(Message.AssistantWithToolCalls(response.Content, response.ToolCalls));

                // Execute each tool, threading state through functionally
                foreach (var toolCall in response.ToolCalls)
                {
                    Console.WriteLine($"  Tool: {toolCall.FunctionName}({toolCall.ArgumentsJson})");
                    var result = await _tools.ExecuteAsync(toolCall.FunctionName, toolCall.Arguments, state);
                    state = result.State;
                    Console.WriteLine($"  Result: {Truncate(result.Output, 200)}");
                    messages.Add(Message.Tool(toolCall.Id, result.Output));
                }
            }
            else
            {
                // No tool calls - the agent returned text only
                Console.WriteLine($"  Agent: {Truncate(response.Content, 300)}");
                messages.Add(Message.Assistant(response.Content ?? ""));

                if (!state.IsComplete)
                {
                    // Nudge the agent to use the finalise tool
                    Console.WriteLine("  Nudging agent to finalise...");
                    messages.Add(Message.User("You have not called the finalise tool yet. Please use the finalise tool to complete the marking, or continue using other tools if you have more work to do."));
                }
            }
        }

        if (!state.IsComplete)
            Console.WriteLine($"\nWarning: Loop ended after {_maxIterations} iterations without completion.");
        else
            Console.WriteLine("\nMarking complete!");

        return state;
    }

    private static string Truncate(string? s, int maxLen) =>
        s == null ? "" : s.Length <= maxLen ? s : s[..maxLen] + "...";
}
