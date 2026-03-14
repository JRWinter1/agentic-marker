using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles();

// Store job state in memory
var jobs = new ConcurrentDictionary<string, JobState>();

// Find the project root (contains Examples/) and the CLI project path
var projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
var cliProject = Path.Combine(projectRoot, "src", "AgenticMarker", "AgenticMarker.csproj");

// Upload files and start marking
app.MapPost("/api/mark", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();

    var questionFile = form.Files.GetFile("question");
    var rubricFile = form.Files.GetFile("rubric");
    var answerFiles = form.Files.GetFiles("answers");

    // Also accept question as pasted text
    var questionText = form["questionText"].FirstOrDefault();

    if (questionFile is null && string.IsNullOrWhiteSpace(questionText))
        return Results.BadRequest("Question file or text is required.");
    if (rubricFile is null)
        return Results.BadRequest("Rubric file is required.");
    if (answerFiles.Count == 0)
        return Results.BadRequest("At least one student answer is required.");

    var jobId = Guid.NewGuid().ToString("N")[..8];
    var uploadDir = Path.Combine(projectRoot, "uploads", jobId);
    Directory.CreateDirectory(uploadDir);

    // Save question
    string questionPath;
    if (questionFile is not null)
    {
        questionPath = Path.Combine(uploadDir, questionFile.FileName);
        await SaveFileAsync(questionFile, questionPath);
    }
    else
    {
        questionPath = Path.Combine(uploadDir, "question.md");
        await File.WriteAllTextAsync(questionPath, questionText);
    }

    // Save rubric
    var rubricPath = Path.Combine(uploadDir, rubricFile.FileName);
    await SaveFileAsync(rubricFile, rubricPath);

    // Save answers
    var answerPaths = new List<string>();
    foreach (var file in answerFiles)
    {
        var answerPath = Path.Combine(uploadDir, file.FileName);
        await SaveFileAsync(file, answerPath);
        answerPaths.Add(answerPath);
    }

    var job = new JobState(jobId, answerPaths.Count);
    jobs[jobId] = job;

    // Run marking in background
    _ = Task.Run(async () =>
    {
        for (var i = 0; i < answerPaths.Count; i++)
        {
            var answerPath = answerPaths[i];
            var studentName = Path.GetFileNameWithoutExtension(answerPath);
            job.AddEvent($"Starting: {studentName}");
            job.CurrentStudent = studentName;

            try
            {
                var (exitCode, output) = await RunCliAsync(cliProject, questionPath, rubricPath, answerPath, projectRoot);
                if (exitCode == 0)
                {
                    job.AddEvent($"Completed: {studentName}");
                    job.CompletedCount++;

                    // Find the feedback file
                    var markedDir = Path.Combine(projectRoot, "MarkedPapers");
                    var feedbackFiles = Directory.GetFiles(markedDir, $"{studentName}-Feedback.*");
                    foreach (var f in feedbackFiles)
                    {
                        job.ResultFiles.Add(f);
                    }
                }
                else
                {
                    job.AddEvent($"Failed: {studentName} — {output}");
                    job.CompletedCount++;
                }
            }
            catch (Exception ex)
            {
                job.AddEvent($"Error: {studentName} — {ex.Message}");
                job.CompletedCount++;
            }
        }

        job.IsComplete = true;
        job.AddEvent("All marking complete.");
    });

    return Results.Ok(new { jobId });
});

// SSE endpoint for progress
app.MapGet("/api/progress/{jobId}", async (string jobId, HttpContext context) =>
{
    if (!jobs.TryGetValue(jobId, out var job))
    {
        context.Response.StatusCode = 404;
        return;
    }

    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    var lastIndex = 0;
    while (!context.RequestAborted.IsCancellationRequested)
    {
        var events = job.GetEvents(lastIndex);
        foreach (var evt in events)
        {
            var data = JsonSerializer.Serialize(new
            {
                message = evt,
                completed = job.CompletedCount,
                total = job.TotalCount,
                current = job.CurrentStudent,
                done = job.IsComplete,
                files = job.IsComplete ? job.ResultFiles.Select(Path.GetFileName).ToList() : []
            });
            await context.Response.WriteAsync($"data: {data}\n\n");
            await context.Response.Body.FlushAsync();
            lastIndex++;
        }

        if (job.IsComplete)
            break;

        await Task.Delay(500);
    }
});

// Download result files
app.MapGet("/api/results/{fileName}", (string fileName) =>
{
    var markedDir = Path.Combine(projectRoot, "MarkedPapers");
    var filePath = Path.Combine(markedDir, fileName);

    if (!File.Exists(filePath))
        return Results.NotFound();

    var contentType = Path.GetExtension(filePath) switch
    {
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".md" => "text/markdown",
        _ => "application/octet-stream"
    };

    return Results.File(filePath, contentType, fileName);
});

// Fallback to index.html
app.MapFallbackToFile("index.html");

app.Run();

static async Task SaveFileAsync(IFormFile file, string path)
{
    await using var stream = File.Create(path);
    await file.CopyToAsync(stream);
}

static async Task<(int ExitCode, string Output)> RunCliAsync(
    string cliProject, string questionPath, string rubricPath, string answerPath, string workingDir)
{
    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"run --project \"{cliProject}\" -- \"{questionPath}\" \"{rubricPath}\" \"{answerPath}\"",
        WorkingDirectory = workingDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    using var process = Process.Start(psi)!;
    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    return (process.ExitCode, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
}

static string FindProjectRoot(string startDir)
{
    var dir = startDir;
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir, "Examples")))
            return dir;
        dir = Directory.GetParent(dir)?.FullName;
    }

    return startDir;
}

class JobState(string id, int totalCount)
{
    private readonly List<string> _events = [];
    private readonly object _lock = new();

    public string Id => id;
    public int TotalCount => totalCount;
    public int CompletedCount { get; set; }
    public string? CurrentStudent { get; set; }
    public bool IsComplete { get; set; }
    public List<string> ResultFiles { get; } = [];

    public void AddEvent(string message)
    {
        lock (_lock)
        {
            _events.Add(message);
        }
    }

    public List<string> GetEvents(int fromIndex)
    {
        lock (_lock)
        {
            return fromIndex < _events.Count
                ? _events[fromIndex..]
                : [];
        }
    }
}
