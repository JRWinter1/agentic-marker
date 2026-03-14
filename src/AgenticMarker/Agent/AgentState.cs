using System.Collections.Immutable;

namespace AgenticMarker.Agent;

public record AgentState(
    string QuestionMarkdown = "",
    string AnswerMarkdown = "",
    string MarkingBrief = "",
    ImmutableDictionary<string, string>? Feedback = null,
    ImmutableDictionary<string, int>? Marks = null,
    string OverallSummary = "",
    int? OverallMark = null,
    string Feedforward = "",
    bool IsComplete = false)
{
    public ImmutableDictionary<string, string> Feedback { get; init; } =
        Feedback ?? ImmutableDictionary<string, string>.Empty;

    public ImmutableDictionary<string, int> Marks { get; init; } =
        Marks ?? ImmutableDictionary<string, int>.Empty;
}
