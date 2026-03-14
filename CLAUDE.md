# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build (enforces code style and analyzers — must pass with 0 warnings)
dotnet build src/AgenticMarker/AgenticMarker.csproj

# Run
dotnet run --project src/AgenticMarker -- <question.doc> <marking-brief.doc> <answer.docx>

# Example with included calibration data
dotnet run --project src/AgenticMarker -- Examples/FakeStudent/StudentAnswer.md Examples/FakeStudent/marking-brief.md Examples/FakeStudent/StudentAnswer.md
```

Output is written to `MarkedPapers/{StudentId}-Feedback.md` and `.docx` at the project root.

## Architecture

This is an **agentic loop** CLI tool that marks university assignments using an LLM. The architecture has three phases: startup → agent loop → output.

### The Agent Loop (`Agent/AgentLoop.cs`)

The core pattern: a `while` loop that sends messages + tool definitions to the LLM, executes any tool calls, threads the updated state back, and repeats until `finalise` is called or 20 iterations are reached. If the LLM responds without tool calls, it gets nudged to finalise.

### Immutable State Threading

`AgentState` is an immutable record with `ImmutableDictionary` collections. Tools never mutate state — they return a `ToolResult(string Output, AgentState State)` with a new state produced via `with` expressions. The loop reassigns `state = result.State` after each tool execution.

### System Prompt Assembly

The system prompt is composed from three sources concatenated at startup in `Program.cs`:
1. `prompts/persona.md` — reusable agent role and workflow instructions
2. Marking brief — passed as a CLI argument (.doc/.docx/.md), converted to markdown
3. Calibration examples — all `Examples/*/` directories containing `calibration.md`, with optional `StudentAnswer.*` and `marking-brief.md` loaded alongside

### Tools

Six tools registered in `ToolRegistry`, all implementing `ITool`. The LLM calls them via OpenAI-compatible `tool_calls`. Key validation: `FinaliseTool` checks for exact keys `"LO1"`–`"LO4"` in Feedback and exact criterion names `"Knowledge & Understanding"`, `"Criticality"`, `"Reading & Research"`, `"Writing Style"` in Marks.

### LLM Integration

`OpenRouterClient` wraps the OpenRouter API (OpenAI-compatible chat completions). Config is in `appsettings.json` — the `ApiKey` field must be set. Tool call arguments arrive as a JSON string inside the response that must be reparsed into `JsonElement`.

### Document Processing

`DocumentConverter` handles `.doc` (OLE2 binary via OpenMcdf with heuristic UTF-16LE text extraction), `.docx` (System.IO.Packaging with paragraph/table/heading extraction), and `.md`/`.txt` (pass-through). `FeedbackDocument` generates the output `.docx` from the final `AgentState`.

## Code Style

Enforced via `.editorconfig` and `EnforceCodeStyleInBuild=true` with `AnalysisLevel=latest-recommended`. Key rules:

- **Records over classes** for data types. Classes only for types with real behaviour or mutable resources.
- **Immutability by default** — `with` expressions, `ImmutableDictionary`/`IReadOnlyList`, init-only properties.
- File-scoped namespaces (warning).
- `var` everywhere. Pattern matching enforced.
- Private fields: `_camelCase`. Readonly fields enforced as warning.
- Unused members/usings are warnings, not suggestions.

## Configuration

`appsettings.json` is in `src/AgenticMarker/` alongside the project and copied to output on build. The `OpenRouter.ApiKey` field must be populated before running.

## Project Root Detection

`Program.cs` walks up from `AppContext.BaseDirectory` looking for the `Examples/` directory to locate calibration data. This matters when running from the `bin/` output directory.
