using System.Diagnostics;
using System.Globalization;
using System.Text;
using AgenticMarker.Agent;

namespace AgenticMarker.Documents;

public static class FeedbackDocument
{
    public static void Generate(AgentState state, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Change extension to .md
        var mdPath = Path.ChangeExtension(outputPath, ".md");
        var docxPath = Path.ChangeExtension(outputPath, ".docx");

        var md = BuildMarkdown(state);
        File.WriteAllText(mdPath, md);
        Console.WriteLine($"Feedback written to: {mdPath}");

        // Convert to .docx via pandoc if available
        try
        {
            var psi = new ProcessStartInfo("pandoc", [mdPath, "-o", docxPath])
            {
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi)!;
            process.WaitForExit(10_000);
            if (process.ExitCode == 0)
                Console.WriteLine($"Feedback written to: {docxPath}");
            else
                Console.WriteLine($"pandoc conversion failed: {process.StandardError.ReadToEnd()}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"pandoc not available, skipping .docx conversion: {ex.Message}");
        }
    }

    internal static string BuildMarkdown(AgentState state)
    {
        var sb = new StringBuilder();

        // Title
        sb.AppendLine("# Assignment Feedback");
        sb.AppendLine();

        // Learning Outcome Feedback
        var loNames = new Dictionary<string, string>
        {
            ["LO1"] = "Learning Outcome 1",
            ["LO2"] = "Learning Outcome 2",
            ["LO3"] = "Learning Outcome 3",
            ["LO4"] = "Learning Outcome 4"
        };

        foreach (var (lo, feedback) in state.Feedback.OrderBy(kv => kv.Key))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"## {loNames.GetValueOrDefault(lo, lo)}");
            sb.AppendLine();

            var parts = SplitFeedback(feedback);

            sb.AppendLine("| What we asked for | How you did |");
            sb.AppendLine("|---|---|");
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {EscapeCell(parts.Asked)} | {EscapeCell(parts.Response)} |");
            sb.AppendLine();
        }

        // Marks table
        sb.AppendLine("## Marks");
        sb.AppendLine();
        sb.AppendLine("| Criterion | Mark |");
        sb.AppendLine("|---|---|");
        foreach (var (criterion, mark) in state.Marks.OrderBy(kv => kv.Key))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {criterion} | {mark}% |");
        }
        sb.AppendLine();

        // Overall Performance
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Overall Performance: {state.OverallMark ?? 0}%");
        sb.AppendLine();
        sb.AppendLine(state.OverallSummary);
        sb.AppendLine();

        // Feedforward
        sb.AppendLine("## Feedforward");
        sb.AppendLine();
        sb.AppendLine(state.Feedforward);
        sb.AppendLine();

        // Self-reflection
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*Please reflect on this feedback and consider bringing this to a Personal Tutor meeting for discussion.*");
        sb.AppendLine();

        return sb.ToString();
    }

    private static (string Asked, string Response) SplitFeedback(string feedback)
    {
        var markers = new[]
        {
            "How you did:",
            "How you did -",
            "\nYou ",
            "\nYour ",
            "\nThe ",
            "\nGood ",
            "\nSound ",
        };

        foreach (var marker in markers)
        {
            var idx = feedback.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx > 10)
            {
                var asked = feedback[..idx].Trim();
                var response = feedback[idx..].Trim();

                asked = StripPrefix(asked, "What we asked for:");
                response = StripPrefix(response, "How you did:");
                response = StripPrefix(response, "How you did -");

                return (asked, response);
            }
        }

        return ("See learning outcome description.", StripPrefix(feedback, "What we asked for:"));
    }

    private static string StripPrefix(string text, string prefix)
    {
        text = text.Trim();
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            text = text[prefix.Length..].Trim();
        return text;
    }

    private static string EscapeCell(string text) =>
        text.Replace("|", "\\|").Replace("\n", "<br>");
}
