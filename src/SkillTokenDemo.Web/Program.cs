using SkillTokenDemo.Core;

var builder = WebApplication.CreateBuilder(args);
// Explicit, so the key is read even when the environment is not "Development" (e.g. run from Rider without launchSettings).
builder.Configuration.AddUserSecrets<Program>(optional: true);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/results", () =>
{
    var results = ScenarioCatalogue.All().Select(CostCalculator.Calculate).ToList();
    return Results.Ok(ToResponse("estimated", results));
});

app.MapGet("/api/example", (string? kind) => kind == "text"
    ? Results.Ok(new { prompt = ComparisonExample.TextPrompt, context = "", knownSymbols = Array.Empty<string>() })
    : Results.Ok(new
    {
        prompt = ComparisonExample.Prompt,
        context = ComparisonExample.Context,
        knownSymbols = ComparisonExample.KnownSymbols
    }));

app.MapPost("/api/live", async (string? model, IConfiguration config, CancellationToken ct) =>
{
    var (http, chosenModel, error) = CreateLiveClient(config, model);
    if (http is null)
    {
        return Results.BadRequest(new { error });
    }

    try
    {
        using var client = http;
        var runner = new LiveRunner(client, chosenModel);
        var results = new List<ScenarioResult>();
        foreach (var scenario in ScenarioCatalogue.All())
        {
            results.Add(await runner.RunAsync(scenario, ct));
        }

        return Results.Ok(ToResponse($"live:{chosenModel}", results));
    }
    catch (HttpRequestException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPost("/api/compare", async (CompareRequest request, IConfiguration config, CancellationToken ct) =>
{
    var prompt = request.Prompt?.Trim() ?? "";
    if (prompt.Length == 0 || prompt.Length > 4000)
    {
        return Results.BadRequest(new { error = "The prompt must be between 1 and 4000 characters." });
    }

    if (request.Context is { Length: > 8000 })
    {
        return Results.BadRequest(new { error = "The existing code must be at most 8000 characters." });
    }

    var skillNames = ScenarioCatalogue.All().Select(s => s.Skill.Name).Where(n => n != "Normal").ToHashSet();
    if (request.Skill is null || !skillNames.Contains(request.Skill))
    {
        return Results.BadRequest(new { error = "Choose a valid skill: " + string.Join(", ", skillNames) + "." });
    }

    var (http, model, error) = CreateLiveClient(config, null);
    if (http is null)
    {
        return Results.BadRequest(new { error });
    }

    try
    {
        using var client = http;
        var runner = new LiveRunner(client, model);
        var known = request.KnownSymbols ?? [];
        var comparison = await PromptComparer.CompareAsync(runner, request.Skill, prompt, request.Context, ct);

        return Results.Ok(new
        {
            skill = comparison.Skill,
            prompt = comparison.Prompt,
            outputWeight = CostCalculator.OutputWeight,
            baseline = ToReply(comparison.Baseline, comparison.BaselineCost, known),
            withSkill = ToReply(comparison.WithSkill, comparison.WithSkillCost, known),
            // Positive = the skill made the reply cheaper than the Normal baseline.
            savingPercent = CostCalculator.SavingPercent(comparison.BaselineCost, comparison.WithSkillCost)
        });
    }
    catch (HttpRequestException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

static (HttpClient? Http, string Model, string? Error) CreateLiveClient(IConfiguration config, string? model)
{
    var apiKey = config["ANTHROPIC_API_KEY"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return (null, "", "Set ANTHROPIC_API_KEY on the server to use Live mode. It makes real, billed API calls.");
    }

    // ANTHROPIC_BASE_URL points the live mode at a local compatible server (e.g. Ollama). Unset = real Anthropic API.
    var baseUrl = config["ANTHROPIC_BASE_URL"];
    var chosenModel = string.IsNullOrWhiteSpace(model) ? config["ANTHROPIC_MODEL"] ?? LiveRunner.DefaultModel : model;
    var client = LiveRunner.CreateClient(apiKey, string.IsNullOrWhiteSpace(baseUrl) ? "https://api.anthropic.com/" : baseUrl);
    return (client, chosenModel, null);
}

static object ToReply(SingleReply reply, double cost, IReadOnlyCollection<string> knownSymbols)
{
    var metrics = ReplyAnalyser.Analyse(reply.Text, knownSymbols);
    return new
    {
        input = reply.InputTokens,
        output = reply.OutputTokens,
        cost,
        text = reply.Text,
        metrics = new
        {
            codeLines = metrics.CodeLines,
            newTypes = metrics.NewTypes,
            reusedSymbols = metrics.ReusedSymbols,
            hasTest = metrics.HasTest,
            questions = metrics.Questions,
            hasCode = metrics.HasCode,
            topicsCovered = ClarifyingTopics.Covered(reply.Text)
        }
    };
}

static object ToResponse(string mode, IReadOnlyList<ScenarioResult> results)
{
    var baseline = results[0];
    return new
    {
        mode,
        outputWeight = CostCalculator.OutputWeight,
        task = ScenarioCatalogue.TaskPrompt,
        skills = results.Select(r => new
        {
            name = r.Scenario.Skill.Name,
            command = r.Scenario.Skill.Command,
            mechanism = r.Scenario.Skill.Mechanism,
            takeaway = r.Scenario.Takeaway,
            overheadTokens = r.Scenario.Skill.OverheadTokens,
            overheadTotal = r.Scenario.Skill.OverheadTokens * r.AssistantTurns,
            turns = r.AssistantTurns + r.UserTurns,
            inputTokens = r.InputTokens,
            outputTokens = r.OutputTokens,
            cost = r.CostUnits,
            codeLines = r.CodeLines,
            // Positive = cheaper than the Normal baseline.
            vsNormalPercent = ReferenceEquals(r, baseline) ? 0 : CostCalculator.SavingPercent(baseline.CostUnits, r.CostUnits),
            turnUsage = r.PerTurnUsage.Select(t => new { label = t.Label, input = t.Input, output = t.Output }),
            transcript = r.Scenario.Turns.Select(t => new { speaker = t.Speaker.ToString(), label = t.Label, text = t.Text })
        })
    };
}

public sealed record CompareRequest(
    string? Skill,
    string? Prompt,
    string? Context = null,
    IReadOnlyList<string>? KnownSymbols = null);
