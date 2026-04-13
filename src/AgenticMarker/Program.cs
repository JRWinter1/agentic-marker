using System.CommandLine;
using Microsoft.Extensions.Configuration;
using AgenticMarker.Agent;
using AgenticMarker.Documents;
using AgenticMarker.LLM;
using AgenticMarker.Tools;

var questionArg = new Argument<FileInfo>("question", "Path to the question .doc/.docx file");
var markingBriefArg = new Argument<FileInfo>("marking-brief", "Path to the marking brief / rubric .doc/.docx file");
var answerArg = new Argument<FileInfo>("answer", "Path to the student answer .doc/.docx file");
var skipCalibrationOption = new Option<bool>("--skip-calibration", getDefaultValue: () => true, "Skip loading calibration examples (rubric-only mode)");
var rootCommand = new RootCommand("Agentic Marker - AI-powered assignment marking tool")
{
    questionArg,
    markingBriefArg,
    answerArg,
    skipCalibrationOption
};

rootCommand.SetHandler(async (FileInfo question, FileInfo markingBriefFile, FileInfo answer, bool skipCalibration) =>
{
    Console.WriteLine("Agentic Marker starting...\n");

    // Validate inputs
    if (!question.Exists)
    {
        Console.Error.WriteLine($"Error: Question file not found: {question.FullName}");
        return;
    }
    if (!markingBriefFile.Exists)
    {
        Console.Error.WriteLine($"Error: Marking brief not found: {markingBriefFile.FullName}");
        return;
    }
    if (!answer.Exists)
    {
        Console.Error.WriteLine($"Error: Answer file not found: {answer.FullName}");
        return;
    }

    // Load config
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .Build();

    // Convert documents to markdown
    Console.WriteLine("Reading documents...");
    var questionMd = DocumentConverter.ConvertToMarkdown(question.FullName);
    var markingBrief = DocumentConverter.ConvertToMarkdown(markingBriefFile.FullName);
    var answerMd = DocumentConverter.ConvertToMarkdown(answer.FullName);
    Console.WriteLine($"  Question: {question.Name} ({questionMd.Length} chars)");
    Console.WriteLine($"  Marking brief: {markingBriefFile.Name} ({markingBrief.Length} chars)");
    Console.WriteLine($"  Answer: {answer.Name} ({answerMd.Length} chars)");

    // Load prompts
    var promptsDir = Path.Combine(AppContext.BaseDirectory, "prompts");
    var persona = File.ReadAllText(Path.Combine(promptsDir, "persona.md"));

    var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
    var calibrationSection = "";

    if (!skipCalibration)
    {
        // Load calibration examples from all subdirectories under Examples/
        // Skip FakeStudent if real examples exist (FakeStudent is a fallback for fresh clones)
        var examplesDir = Path.Combine(projectRoot, "Examples");
        var allCalibrationFiles = Directory.GetFiles(examplesDir, "calibration.md", SearchOption.AllDirectories);
        var realCalibrationFiles = allCalibrationFiles
            .Where(f => !Path.GetFileName(Path.GetDirectoryName(f)!).Equals("FakeStudent", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var calibrationFiles = realCalibrationFiles.Length > 0 ? realCalibrationFiles : allCalibrationFiles;
        if (realCalibrationFiles.Length > 0 && allCalibrationFiles.Length > realCalibrationFiles.Length)
        {
            Console.WriteLine("  Skipping FakeStudent (real calibration examples found)");
        }

        var calibrationBlock = new System.Text.StringBuilder();
        foreach (var calFile in calibrationFiles.Order())
        {
            var calContent = File.ReadAllText(calFile);
            var calDir = Path.GetDirectoryName(calFile)!;
            var folderName = Path.GetFileName(calDir);
            calibrationBlock.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"### Example: {folderName}");
            calibrationBlock.AppendLine();

            // Load the rubric/marking brief if present in the same directory
            var rubricPath = Path.Combine(calDir, "marking-brief.md");
            if (File.Exists(rubricPath))
            {
                var rubricContent = File.ReadAllText(rubricPath);
                calibrationBlock.AppendLine("#### Rubric");
                calibrationBlock.AppendLine();
                calibrationBlock.AppendLine(rubricContent);
                calibrationBlock.AppendLine();
            }

            // Load the student answer if present in the same directory
            var answerFiles = Directory.GetFiles(calDir, "StudentAnswer.*");
            if (answerFiles.Length > 0)
            {
                var calAnswerMd = DocumentConverter.ConvertToMarkdown(answerFiles[0]);
                calibrationBlock.AppendLine("#### Student Answer");
                calibrationBlock.AppendLine();
                calibrationBlock.AppendLine(calAnswerMd);
                calibrationBlock.AppendLine();
                Console.WriteLine($"  Loaded calibration example: {folderName} (with rubric, student answer, {calAnswerMd.Length} chars)");
            }
            else
            {
                Console.WriteLine($"  Loaded calibration example: {folderName} (calibration.md only)");
            }

            calibrationBlock.AppendLine("#### Marking & Feedback");
            calibrationBlock.AppendLine();
            calibrationBlock.AppendLine(calContent);
            calibrationBlock.AppendLine();
        }

        calibrationSection = $"""

            ## Graded Examples (for calibration)

            Study these completed examples carefully. They show the expected quality, tone, detail level, and marking standards for your feedback.

            {calibrationBlock}
            """;
    }
    else
    {
        Console.WriteLine("  Skipping calibration examples (--skip-calibration is set)");
    }

    var systemPrompt = $"""
        {persona}

        {markingBrief}
        {calibrationSection}
        """;

    // Build initial state
    var state = new AgentState(
        QuestionMarkdown: questionMd,
        AnswerMarkdown: answerMd,
        MarkingBrief: markingBrief);

    // Register tools
    var tools = new ToolRegistry();
    tools.Register(new ReadCriterionTool());
    tools.Register(new WriteFeedbackTool());
    tools.Register(new AssignMarkTool());
    tools.Register(new WriteOverallTool());
    tools.Register(new WriteFeedforwardTool());
    tools.Register(new FinaliseTool());

    // Create LLM client and agent loop
    var llm = new OpenRouterClient(config);
    var agent = new AgentLoop(llm, tools);

    // Build user message with question and answer
    var userMessage = $"""
        ## Assignment Question

        {questionMd}

        ## Student Answer

        {answerMd}

        Please mark this assignment following your workflow. Start by reviewing the example feedback for calibration, then assess each learning outcome.
        """;

    // Run the agentic loop
    var result = await agent.RunAsync(systemPrompt, userMessage, state);

    // Generate output document in MarkedPapers folder
    var markedPapersDir = Path.Combine(projectRoot, "MarkedPapers");
    Directory.CreateDirectory(markedPapersDir);

    // Extract student identifier from the answer path (e.g. "Examples/FakeStudent/StudentAnswer.docx" → "FakeStudent")
    var studentId = ExtractStudentId(answer.FullName);
    var outputPath = Path.Combine(markedPapersDir, $"{studentId}-Feedback.md");
    FeedbackDocument.Generate(result, outputPath);

}, questionArg, markingBriefArg, answerArg, skipCalibrationOption);

return await rootCommand.InvokeAsync(args);

static string ExtractStudentId(string filePath)
{
    // Walk up the directory path looking for a folder name that looks like a student ID (all digits)
    var dir = Path.GetDirectoryName(filePath);
    while (dir != null)
    {
        var folderName = Path.GetFileName(dir);
        if (!string.IsNullOrEmpty(folderName) && folderName.All(char.IsDigit))
            return folderName;
        dir = Path.GetDirectoryName(dir);
    }

    // Fallback: use the answer filename
    return Path.GetFileNameWithoutExtension(filePath);
}

static string FindProjectRoot(string startDir)
{
    var dir = startDir;
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir, "Examples")))
            return dir;
        dir = Directory.GetParent(dir)?.FullName;
    }
    return startDir;
}
