using System.Diagnostics;
using System.Text;
using Todo.AiEval;
using Todo.Core.Ai;
using Todo.Host.Ai;

// Asks the REAL model the prompts in AiPrompts and scores the answers against fixed cases. Never part
// of dotnet test or Check.cmd: the suite must not reach the real model (the AI foundation design's
// section 4), and an answer that changes with a model update is a measurement, not a regression.
//
// Run from the repository root:
//   dotnet run --project tests\Todo.AiEval
//   dotnet run --project tests\Todo.AiEval -- --feature split --model gemma4:e4b
//
// Change a prompt, run this, and compare the score and the failures with the previous run.

Console.OutputEncoding = Encoding.UTF8;

var options = new AiOptions();
string? only = null;

for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--model": options.Model = args[++i]; break;
        case "--url": options.BaseUrl = args[++i]; break;
        case "--feature": only = args[++i]; break;
    }
}

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
var client = OllamaClient.With(http, options);

if (!await client.IsReadyAsync())
{
    Console.Error.WriteLine($"Ollama at {options.BaseUrl} does not answer or does not have {options.Model}.");
    return 2;
}

Console.WriteLine($"Model {options.Model} at {options.BaseUrl}, today fixed at {Cases.Today:yyyy-MM-dd}.");

var results = new List<CaseResult>();

// The first call loads the model, which measured 15 s on its own. Paid here so it does not land on
// whichever case happens to run first and make that one look slow.
await Warm(client);

if (only is null or "parse")
{
    foreach (var c in Cases.Parse)
    {
        results.Add(await Run("parse", c.Line, () => Evaluate.ParseAsync(client, c)));
    }
}

if (only is null or "split")
{
    foreach (var c in Cases.Split)
    {
        results.Add(await Run("split", c.Title, () => Evaluate.SplitAsync(client, c)));
    }
}

if (only is null or "summary")
{
    foreach (var c in Cases.Summary)
    {
        results.Add(await Run("summary", c.Title, () => Evaluate.SummaryAsync(client, c)));
    }
}

if (only is null or "retro")
{
    foreach (var c in Cases.Retro)
    {
        results.Add(await Run("retro", c.Name, () => Evaluate.RetroAsync(client, c)));
    }
}

if (only is null or "review")
{
    foreach (var c in Cases.Review)
    {
        results.Add(await Run("review", c.Name, () => Evaluate.ReviewAsync(client, c)));
    }
}

if (only is null or "tags")
{
    foreach (var c in Cases.Tags)
    {
        results.Add(await Run("tags", c.Title, () => Evaluate.TagsAsync(client, c)));
    }
}

if (only is null or "search")
{
    foreach (var c in Cases.Search)
    {
        results.Add(await Run("search", c.Query, () => Evaluate.SearchAsync(client, c)));
    }

    foreach (var c in Cases.Duplicates)
    {
        results.Add(await Run("dupes", $"{c.Row} | {c.Existing}", () => Evaluate.DuplicateAsync(client, c)));
    }
}

Console.WriteLine();

foreach (var feature in results.GroupBy(r => r.Feature))
{
    var times = feature.Select(r => r.Seconds).Order().ToList();
    Console.WriteLine(
        $"{feature.Key,-8} {feature.Count(r => r.Failures.Count == 0)}/{feature.Count()} passed, "
            + $"median {times[times.Count / 2]:0.0} s, slowest {times[^1]:0.0} s");
}

return results.All(r => r.Failures.Count == 0) ? 0 : 1;

static async Task<CaseResult> Run(string feature, string name, Func<Task<(string Answer, List<string> Failures)>> evaluate)
{
    var clock = Stopwatch.StartNew();
    string answer;
    List<string> failures;

    try
    {
        (answer, failures) = await evaluate();
    }
    catch (Exception exception)
    {
        (answer, failures) = (string.Empty, [$"threw {exception.GetType().Name}: {exception.Message}"]);
    }

    var seconds = clock.Elapsed.TotalSeconds;
    var mark = failures.Count == 0 ? "PASS" : "FAIL";

    Console.WriteLine($"{mark} {feature,-8} {seconds,5:0.0} s  {name}");
    Console.WriteLine($"     -> {answer}");

    foreach (var failure in failures)
    {
        Console.WriteLine($"     !! {failure}");
    }

    return new CaseResult(feature, name, seconds, failures);
}

static Task Warm(OllamaClient client) =>
    client.CompleteAsync<SummaryAnswer>("Say hello.", AiPrompts.SummarySchema());
