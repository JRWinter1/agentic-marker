# Agentic Marker

A C# command-line tool that marks university assignments using an **agentic loop** — an LLM that reasons, decides which actions to take, executes them, observes the results, and repeats until the job is done.

This project exists to demonstrate how agentic loops work in practice, using assignment marking as a concrete, relatable example.

## What is an Agentic Loop?

Most LLM integrations are **single-shot**: you send a prompt, get a response, done. An agentic loop is fundamentally different. Instead of one exchange, the LLM operates in a **continuous cycle** where it can take actions in the real world and react to their outcomes.

The key insight is the separation of **thinking** and **doing**:

```
┌────────────────────────────────────────────────────────────┐
│                                                            │
│   ┌──────────┐  write_feedback("LO1", ...)  ┌──────────┐   │
│   │          │ ───────────────────────────▶ │          │   │
│   │  BRAIN   │                              │  HANDS   │   │
│   │  (LLM)   │ ◀─────────────────────────── │  (Tools) │   │
│   │          │  "Feedback for LO1 saved"    │          │   │
│   └──────────┘                              └──────────┘   │
│        │                                                   │
│        │  Decides what to do next based on results so far  │
│        │                                                   │
│        └───────────────── loop back ────────────────┐      │
│                                                     │      │
│   ┌──────────┐  assign_mark("K&U", 62)    ┌──────────┐     │
│   │          │ ─────────────────────────▶ │          │     │
│   │  BRAIN   │                            │  HANDS   │     │
│   │  (LLM)   │ ◀───────────────────────── │  (Tools) │     │
│   │          │  "Mark for 'K&U' recorded" │          │     │
│   └──────────┘                            └──────────┘     │
│        │                                                   │
│        └─────────── continues until done ─────────────┘    │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

The LLM is not told exactly which tools to call or in what order. It receives a goal ("mark this assignment"), a set of available tools, and a workflow guideline — then it **decides** what to do at each step based on what it has done so far. This is what makes it "agentic" rather than scripted.

## The Three Components of an Agent

Every agentic system has the same three building blocks. Here's how this project implements each one:

### 1. The Brain (LLM)

The brain is a large language model accessed via API. It receives the full conversation history (what it's been told, what it's said, what tools it has called and their results) and decides what to do next.

In this project, the brain is configured in [`Program.cs:58-75`](src/AgenticMarker/Program.cs) where the system prompt is assembled from three parts:

```
System Prompt = Persona + Marking Brief + Graded Example
```

- **Persona** ([`prompts/persona.md`](prompts/persona.md)) — tells the LLM *who it is* and *how to work* (a university lecturer following a specific marking workflow)
- **Marking Brief** (passed as CLI argument) — gives it *domain knowledge* (learning outcomes, criteria, mark bands)
- **Graded Example** ([`Examples/FakeStudent/`](Examples/FakeStudent/)) — shows it *what good looks like* (a completed question + answer + feedback triple for calibration)

The LLM communicates with our code via the chat completions API. When it wants to take an action, it doesn't output text — it outputs a **tool call**: a structured JSON request saying "I want to call this function with these arguments". This is the bridge between thinking and doing.

The API call happens in [`OpenRouterClient.cs:101-121`](src/AgenticMarker/LLM/OpenRouterClient.cs), which sends the full message history plus tool definitions and parses back either text content or tool calls.

### 2. The Hands (Tools)

Tools are functions the LLM can call. The LLM never executes code directly — it expresses an *intent* ("I want to record feedback for LO1"), and our code executes it. The LLM doesn't even know tools are just C# methods. It only sees a name, a description, and a parameter schema.

Each tool in this project implements the [`ITool`](src/AgenticMarker/Tools/ITool.cs) interface:

```csharp
public interface ITool
{
    string Name { get; }                    // What the LLM calls it by
    string Description { get; }             // How the LLM knows when to use it
    JsonElement Parameters { get; }          // JSON Schema defining expected arguments
    Task<ToolResult> ExecuteAsync(           // What actually happens when called
        JsonElement arguments,
        AgentState state);
}
```

The LLM sees the tools as a menu of capabilities. Here is the menu for this project:

| Tool               | What the LLM uses it for       | What it actually does in code                                      |
|--------------------|--------------------------------|--------------------------------------------------------------------|
| `read_criterion`   | Look up mark band descriptors  | Returns the marking brief text                                     |
| `write_feedback`   | Record feedback for LO1–LO4   | `state with { Feedback = state.Feedback.SetItem(lo, text) }`       |
| `assign_mark`      | Record a mark for a criterion  | `state with { Marks = state.Marks.SetItem(criterion, mark) }`     |
| `write_overall`    | Record overall summary + mark  | `state with { OverallSummary = summary, OverallMark = mark }`     |
| `write_feedforward`| Record improvement suggestions | `state with { Feedforward = text }`                                |
| `finalise`         | Signal that marking is complete| Validates all sections exist, then `state with { IsComplete = true }` |

Notice the pattern: every tool receives the current state and returns a **new** state. Nothing is mutated. This is important for understanding the data flow.

### 3. The State (Memory)

The agent needs to remember what it has done across iterations. This project tracks state in an immutable record — [`AgentState.cs`](src/AgenticMarker/Agent/AgentState.cs):

```csharp
public record AgentState(
    string QuestionMarkdown = "",
    string AnswerMarkdown = "",
    string MarkingBrief = "",
    ImmutableDictionary<string, string>? Feedback = null,    // LO1 → feedback text
    ImmutableDictionary<string, int>? Marks = null,          // criterion → mark
    string OverallSummary = "",
    int? OverallMark = null,
    string Feedforward = "",
    bool IsComplete = false);
```

The state starts nearly empty and fills up as the LLM calls tools. Each tool produces a new state snapshot using `with`:

```
Iteration 1:  AgentState { Feedback: {},          Marks: {},          IsComplete: false }
Iteration 2:  AgentState { Feedback: {LO1: "..."}, Marks: {},          IsComplete: false }
Iteration 3:  AgentState { Feedback: {LO1: "..."}, Marks: {K&U: 65},   IsComplete: false }
   ...
Iteration N:  AgentState { Feedback: {LO1–LO4},   Marks: {all four},  IsComplete: true  }
```

But state is only half the story. The LLM also has its own memory: the **message history**. Every API call includes the full conversation so far — every system message, user message, assistant response, tool call, and tool result. This is how the LLM "remembers" what it has already assessed and what is left to do.

## The Loop Itself

Now we can look at the actual loop in [`AgentLoop.cs:19-75`](src/AgenticMarker/Agent/AgentLoop.cs). Here it is, annotated:

```
START
  │
  │  Initialise messages: [system prompt, user message with question + answer]
  │  Initialise state:    AgentState (empty, IsComplete = false)
  │
  ▼
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  while (!state.IsComplete && iteration < maxIterations)         │
│  │                                                              │
│  │  ┌───────────────────────────────────────────────────┐       │
│  │  │  THINK: Send messages + tool definitions to LLM   │       │
│  │  │         var response = await llm.ChatAsync(...)   │       │
│  │  └──────────────────────┬────────────────────────────┘       │
│  │                         │                                    │
│  │                         ▼                                    │
│  │           ┌──────────────────────────────┐                   │
│  │           │  Did the LLM return          │                   │
│  │           │  tool_calls in its response? │                   │
│  │           └──────┬──────────────┬────────┘                   │
│  │                  │              │                            │
│  │              YES ▼          NO  ▼                            │
│  │     ┌─────────────────┐  ┌────────────────────────┐          │
│  │     │ ACT: For each   │  │ The LLM just talked.   │          │
│  │     │ tool call:      │  │ Nudge it: "You haven't │          │
│  │     │                 │  │ called finalise yet."  │          │
│  │     │ 1. Execute tool │  └────────────────────────┘          │
│  │     │ 2. Get new state│                                      │
│  │     │ 3. Add result   │                                      │
│  │     │    to messages  │                                      │
│  │     └─────────────────┘                                      │
│  │            │                                                 │
│  │            ▼                                                 │
│  │    state = result.State   (immutable update)                 │
│  │                                                              │
│  └──────────── loop back ───────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
  │
  ▼
DONE — state contains all feedback, marks, summary, feedforward
  │
  ▼
Generate .docx output from final state
```

The critical lines in the code are:

```csharp
// AgentLoop.cs — the decision point
var response = await _llm.ChatAsync(messages, toolDefs);   // line 37 — THINK

if (response.ToolCalls.Count > 0)                          // line 39 — DECIDE
{
    foreach (var toolCall in response.ToolCalls)            // line 45 — ACT
    {
        var result = await _tools.ExecuteAsync(...);        // line 48 — execute
        state = result.State;                               // line 49 — update state
        messages.Add(Message.Tool(toolCall.Id, result.Output)); // line 51 — record result
    }
}
```

Each iteration, the LLM sees everything that has happened before (via the growing message list) and decides what to do next. It might call one tool, or several at once, or none at all. **The code doesn't control the order — the LLM does.**

## How Data Flows Through the System

Here is the complete data flow from input files to output document:

```
 Question.doc    StudentAnswer.docx
      │                  │
      ▼                  ▼
 ┌──────────────────────────┐
 │   DocumentConverter       │   .doc/.docx → markdown text
 │   (Documents/)            │
 └─────────┬────────────────┘
           │
           ▼
 ┌──────────────────────────┐
 │   Program.cs              │   Assembles:
 │                           │   • System prompt (persona + brief + example)
 │                           │   • User message (question + answer markdown)
 │                           │   • Initial AgentState (empty)
 │                           │   • Tool registry (6 tools)
 └─────────┬────────────────┘
           │
           ▼
 ┌──────────────────────────┐
 │   AgentLoop               │   The agentic loop:
 │   (Agent/)                │   messages + tools ←→ LLM ←→ tool execution
 │                           │
 │   Each iteration:         │
 │   LLM receives:           │    State transitions:
 │   ├─ system prompt        │    ┌─────────────────────────────────┐
 │   ├─ user message         │    │ {} ──write_feedback──▶ {LO1}   │
 │   ├─ prior assistant msgs │    │    ──assign_mark────▶ {K&U:65} │
 │   ├─ prior tool results   │    │    ──write_feedback──▶ {LO2}   │
 │   └─ tool definitions     │    │    ... (more iterations) ...   │
 │                           │    │    ──finalise────▶ IsComplete!  │
 │   LLM returns:            │    └─────────────────────────────────┘
 │   ├─ tool_calls (or text) │
 │   └─ we execute & loop    │
 └─────────┬────────────────┘
           │
           ▼
 ┌──────────────────────────┐
 │   FeedbackDocument        │   Final AgentState → .docx file
 │   (Documents/)            │   with headings, marks table,
 │                           │   feedback sections, feedforward
 └──────────────────────────┘
           │
           ▼
  StudentAnswer-Feedback.docx
```

## How Tool Calling Works at the API Level

The LLM doesn't "discover" tools at runtime or receive them via a protocol like MCP (Model Context Protocol). Instead, the tool definitions are sent as a **plain JSON array** in every API request, as part of the chat completions format.

When `AgentLoop` calls `_llm.ChatAsync(messages, toolDefs)`, the request body looks like this:

```json
{
  "model": "anthropic/claude-sonnet-4.6",
  "messages": [ ... ],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "write_feedback",
        "description": "Records qualitative feedback for a learning outcome...",
        "parameters": {
          "type": "object",
          "properties": {
            "learning_outcome": { "type": "string", "description": "..." },
            "feedback": { "type": "string", "description": "..." }
          },
          "required": ["learning_outcome", "feedback"]
        }
      }
    }
  ]
}
```

The `tools` array is a first-class property of the OpenAI chat completions API. OpenRouter uses this same format and translates it to whatever the underlying provider expects.

### The tool calling cycle

1. **We send** the `tools` array + message history to the API
2. **The LLM responds** with `tool_calls` instead of (or alongside) text content:
   ```json
   {
     "choices": [{
       "message": {
         "tool_calls": [{
           "id": "call_abc123",
           "type": "function",
           "function": {
             "name": "write_feedback",
             "arguments": "{\"learning_outcome\": \"LO1\", \"feedback\": \"You demonstrated...\"}"
           }
         }]
       }
     }]
   }
   ```
3. **We execute** the tool locally and send the result back as a `tool` role message:
   ```json
   { "role": "tool", "tool_call_id": "call_abc123", "content": "Feedback for LO1 recorded." }
   ```
4. **Loop** — the LLM sees the result and decides what to do next.

This is all handled in [`OpenRouterClient.cs`](src/AgenticMarker/LLM/OpenRouterClient.cs) — `ToApiFormat()` serialises our `ToolDefinition` records into the shape above, and `ParseResponse()` extracts `tool_calls` from the response.

### How does this compare to the Anthropic API?

If you called Anthropic directly instead of going through OpenRouter, the concept is identical but the JSON shape differs slightly:

|                     | OpenAI / OpenRouter                          | Anthropic                                                                        |
|---------------------|----------------------------------------------|----------------------------------------------------------------------------------|
| Tool schema key     | `tools[].function.parameters`                | `tools[].input_schema`                                                           |
| LLM requests a call | `tool_calls[].function.name` + `.arguments`  | `content[]` block with `type: "tool_use"`, `name`, `input`                      |
| Returning results   | `role: "tool"` message with `tool_call_id`   | `role: "user"` message containing a `type: "tool_result"` block with `tool_use_id` |

The JSON Schema that defines each tool's parameters is the same in both — so the `ITool.Parameters` property would not need to change. Only the `OpenRouterClient` serialisation/deserialisation code would need adapting.

The key takeaway: tool calling is **not a protocol** — it is just a structured part of the request/response JSON. The LLM sees the tool definitions on every API call and "knows" it can respond with tool calls because that capability is part of its training.

## A Typical Run (What You See in the Console)

When you run the tool, the console output makes the loop visible:

```
Agentic Marker starting...

Reading documents...
  Question: Question.doc (2847 chars)
  Answer: StudentAnswer.docx (14523 chars)
  Loaded graded example for calibration

=== Iteration 1 ===
Thinking...
  Tool: read_criterion({"criterion": "Knowledge & Understanding"})
  Result: # Criterion: Knowledge & Understanding...
  Tool: write_feedback({"learning_outcome": "LO1", "feedback": "You demonstrated a solid..."})
  Result: Feedback for LO1 recorded successfully.
  Tool: assign_mark({"criterion": "Knowledge & Understanding", "mark": 62})
  Result: Mark for 'Knowledge & Understanding' recorded: 62%.

=== Iteration 2 ===
Thinking...
  Tool: write_feedback({"learning_outcome": "LO2", "feedback": "Your critical analysis..."})
  Result: Feedback for LO2 recorded successfully.
  Tool: assign_mark({"criterion": "Criticality", "mark": 58})
  Result: Mark for 'Criticality' recorded: 58%.

  ... (more iterations for LO3, LO4, overall, feedforward) ...

=== Iteration 6 ===
Thinking...
  Tool: finalise({})
  Result: Marking finalised successfully. All sections are complete.

Marking complete!
Feedback written to: MarkedPapers/FakeStudent-Feedback.docx
```

Notice how each iteration shows the LLM thinking, then choosing which tools to call. Sometimes it calls multiple tools in one turn (read a criterion and write feedback together). Sometimes it calls just one. The LLM is making these decisions based on its understanding of the task and what it has done so far.

## Why "Agentic" and Not Just "Scripted"?

You might ask: why not just call the tools in a fixed order? You could write a script that does:
1. For each LO: write feedback, assign mark
2. Write overall
3. Write feedforward
4. Done

The difference is **flexibility and judgement**:

- The LLM might read the criterion descriptors before some LOs but not others, depending on whether it feels confident
- It decides how to weight its feedback based on the student's specific strengths and weaknesses
- If the `finalise` tool reports missing sections, the LLM can figure out what it missed and fill the gaps
- The same loop structure works for completely different tasks — change the system prompt and tools, and you have a different agent

The loop code in `AgentLoop.cs` is **domain-agnostic**. It knows nothing about marking, feedback, or learning outcomes. It only knows: send messages to an LLM, execute whatever tools it asks for, and repeat. All the domain knowledge lives in the prompts and tools.

## The Guardrails

Agentic loops need safety boundaries. This project implements three:

1. **Iteration cap** ([`AgentLoop.cs:30`](src/AgenticMarker/Agent/AgentLoop.cs)) — `maxIterations = 20` prevents runaway loops. If the LLM can't complete in 20 turns, something is wrong.

2. **Completion validation** ([`FinaliseTool.cs:21-47`](src/AgenticMarker/Tools/FinaliseTool.cs)) — the `finalise` tool checks that all required sections actually exist before allowing completion. The LLM can't just say "I'm done" — the code verifies it.

3. **Nudging** ([`AgentLoop.cs:60-64`](src/AgenticMarker/Agent/AgentLoop.cs)) — if the LLM responds with text instead of tool calls, the loop injects a user message reminding it to call `finalise` or keep working. This prevents the agent from getting stuck in a conversational mode.

## Running It Yourself

1. Set your OpenRouter API key in `appsettings.json`
2. Run:
   ```bash
   dotnet run --project src/AgenticMarker -- question.doc marking-brief.doc answer.docx
   ```
3. Watch the console to see the agentic loop in action
4. Open the generated `StudentAnswer-Feedback.docx` to see the output
