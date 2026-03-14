using System.Collections.Immutable;
using AgenticMarker.Agent;
using AgenticMarker.Documents;
using Xunit;

namespace AgenticMarker.Tests;

public class FeedbackDocumentTests
{
    private static AgentState BuildRealisticState()
    {
        var feedback = ImmutableDictionary.CreateRange(new[]
        {
            KeyValuePair.Create("LO1",
                "What we asked for: Demonstrate a critical understanding of the key theoretical concepts " +
                "underpinning sustainable business practices and their application to real-world scenarios.\n\n" +
                "How you did: You provided a solid overview of the triple bottom line framework and referenced " +
                "Elkington (1997) appropriately. Your discussion of stakeholder theory was adequate, though it " +
                "would benefit from deeper engagement with more recent critiques of shareholder primacy."),
            KeyValuePair.Create("LO2",
                "What we asked for: Critically evaluate the effectiveness of corporate sustainability strategies " +
                "using appropriate analytical frameworks.\n\n" +
                "How you did: Your SWOT analysis of Unilever's Sustainable Living Plan was well-structured and " +
                "demonstrated competent use of the framework. However, you could have strengthened your argument " +
                "by incorporating Porter's shared value concept to provide a more nuanced evaluation."),
            KeyValuePair.Create("LO3",
                "What we asked for: Identify and synthesise relevant academic literature to support arguments " +
                "about sustainable business transformation.\n\n" +
                "How you did: You referenced 14 sources, which is adequate for this level. Your use of Brundtland " +
                "(1987) and Raworth (2017) showed good range. Some sources were descriptive rather than analytical " +
                "in how you deployed them. Try to use literature to build and challenge arguments, not just describe."),
            KeyValuePair.Create("LO4",
                "What we asked for: Communicate findings clearly and professionally using appropriate academic " +
                "conventions and referencing.\n\n" +
                "How you did: Your writing was generally clear and well-organised with a logical structure. " +
                "Harvard referencing was mostly consistent, with a few minor formatting errors in the bibliography. " +
                "Paragraphing was effective, though some transitions between sections could be smoother."),
        });

        var marks = ImmutableDictionary.CreateRange(new[]
        {
            KeyValuePair.Create("Criticality", 52),
            KeyValuePair.Create("Knowledge & Understanding", 58),
            KeyValuePair.Create("Reading and Research", 55),
            KeyValuePair.Create("Writing Style", 60),
        });

        return new AgentState(
            QuestionMarkdown: "Analyse the role of sustainability in modern business strategy.",
            AnswerMarkdown: "This essay examines sustainable business practices...",
            MarkingBrief: "Sample module marking criteria.",
            Feedback: feedback,
            Marks: marks,
            OverallSummary: "A competent piece of work that demonstrates a sound understanding of key sustainability " +
                "concepts. The essay engages with relevant literature and applies appropriate analytical frameworks. " +
                "To move into the upper mark bands, you would need to develop more critical depth in your analysis " +
                "and make stronger connections between theory and practice.",
            OverallMark: 55,
            Feedforward: "Focus on developing your critical voice. Rather than describing what theorists say, " +
                "evaluate and compare their positions. Use phrases like 'however', 'this is limited because', " +
                "and 'a more compelling interpretation' to signal analytical depth. Consider attending the " +
                "Academic Writing Workshop series offered by the Learning Hub.",
            IsComplete: true
        );
    }

    [Fact]
    public void GeneratesMarkdownAndDocx()
    {
        var state = BuildRealisticState();
        var tempDir = Path.Combine(Path.GetTempPath(), $"FeedbackTest-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var outputPath = Path.Combine(tempDir, "test-Feedback.md");
        var docxPath = Path.ChangeExtension(outputPath, ".docx");

        try
        {
            FeedbackDocument.Generate(state, outputPath);

            // Markdown file should exist and have content
            Assert.True(File.Exists(outputPath), "Generated .md file should exist");
            var md = File.ReadAllText(outputPath);
            Assert.True(md.Length > 0, "Generated .md file should not be empty");

            // Structural markers
            Assert.Contains("# Assignment Feedback", md);
            Assert.Contains("## Feedforward", md);
            Assert.Contains("Please reflect on this feedback", md);

            // Learning outcome headings
            Assert.Contains("## Learning Outcome 1", md);
            Assert.Contains("## Learning Outcome 2", md);
            Assert.Contains("## Learning Outcome 3", md);
            Assert.Contains("## Learning Outcome 4", md);

            // Feedback table headers
            Assert.Contains("What we asked for", md);
            Assert.Contains("How you did", md);

            // Overall performance with mark
            Assert.Contains("## Overall Performance: 55%", md);

            // Marks table content
            Assert.Contains("Knowledge & Understanding", md);
            Assert.Contains("Criticality", md);
            Assert.Contains("Reading and Research", md);
            Assert.Contains("Writing Style", md);

            // Docx should also exist (pandoc conversion)
            Assert.True(File.Exists(docxPath), "Generated .docx file should exist (via pandoc)");
            Assert.True(new FileInfo(docxPath).Length > 0, "Generated .docx should not be empty");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void BuildMarkdownProducesExpectedStructure()
    {
        var state = BuildRealisticState();
        var md = FeedbackDocument.BuildMarkdown(state);

        // Check table formatting
        Assert.Contains("| What we asked for | How you did |", md);
        Assert.Contains("|---|---|", md);

        // Check marks table
        Assert.Contains("| Criticality | 52% |", md);
        Assert.Contains("| Knowledge & Understanding | 58% |", md);
        Assert.Contains("| Reading and Research | 55% |", md);
        Assert.Contains("| Writing Style | 60% |", md);

        // Overall summary should be present
        Assert.Contains("sound understanding of key sustainability", md);
    }
}
