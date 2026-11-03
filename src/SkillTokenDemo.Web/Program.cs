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
        prompt = kind == "vague" ? ComparisonExample.VaguePrompt : ComparisonExample.Prompt,
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
    if (ValidateInput(prompt, request.Context) is { } invalid)
    {
        return Results.BadRequest(new { error = invalid });
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

// grill-me: the model asks, the example's fact sheet answers, until the model stops asking.
app.MapPost("/api/clarify", async (CompareRequest request, IConfiguration config, CancellationToken ct) =>
{
    var prompt = request.Prompt?.Trim() ?? "";
    if (ValidateInput(prompt, request.Context) is { } invalid)
    {
        return Results.BadRequest(new { error = invalid });
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
        var message = PromptComparer.BuildMessage(prompt, request.Context);

        var baseline = await runner.AskOnceAsync(SkillInstructions.For("Normal"), message, ct);
        var session = await ClarifyingSession.RunAsync(
            runner, SkillInstructions.For("grill-me"), message, ComparisonExample.AnswerFor, ct: ct);

        return Results.Ok(new
        {
            outputWeight = CostCalculator.OutputWeight,
            topicCount = ClarifyingTopics.Patterns.Count,
            baseline = ToReply(baseline, baseline.InputTokens + baseline.OutputTokens * CostCalculator.OutputWeight, known),
            session = new
            {
                rounds = session.QuestionRounds,
                reachedUnderstanding = session.ReachedUnderstanding,
                calls = session.Turns.Count(t => t.Role == "assistant"),
                input = session.InputTokens,
                output = session.OutputTokens,
                cost = session.Cost,
                topicsCovered = session.TopicsCovered,
                final = ToMetrics(session.FinalReply, known),
                turns = session.Turns.Skip(1) // the first turn is the task the page already shows
                    .Select(t => new { role = t.Role, text = t.Text, input = t.InputTokens, output = t.OutputTokens })
            }
        });
    }
    catch (HttpRequestException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// superpowers: three of its process steps tested in isolation, each against its own fixed scenario.
app.MapPost("/api/superpowers", async (SuperpowersRequest request, IConfiguration config, CancellationToken ct) =>
{
    if (request.Mode is not ("brainstorm" or "plan" or "review"))
    {
        return Results.BadRequest(new { error = "Mode must be one of: brainstorm, plan, review." });
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
        object result = request.Mode switch
        {
            "brainstorm" => await RunBrainstorm(runner, ct),
            "plan" => await RunPlan(runner, ct),
            _ => await RunReview(runner, ct)
        };
        return Results.Ok(result);
    }
    catch (HttpRequestException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

static double Cost(SingleReply reply) => reply.InputTokens + reply.OutputTokens * CostCalculator.OutputWeight;

// Brainstorm: a vague task, clarified one question at a time, should end in a design with no code — contrasted
// with Normal, which gets the same message and (typically) starts implementing right away.
static async Task<object> RunBrainstorm(LiveRunner runner, CancellationToken ct)
{
    var message = PromptComparer.BuildMessage(ComparisonExample.VaguePrompt, ComparisonExample.Context);
    var normal = await runner.AskOnceAsync(SkillInstructions.For("Normal"), message, ct);
    var session = await ClarifyingSession.RunAsync(
        runner, SuperpowersScenarios.BrainstormSystem, message, ComparisonExample.AnswerFor, ClarifyingGoal.Design, ct: ct);
    var presentedDesignWithoutCode = !ReplyAnalyser.Analyse(session.FinalReply, []).HasCode;

    return new
    {
        mode = "brainstorm",
        outputWeight = CostCalculator.OutputWeight,
        topicCount = ClarifyingTopics.Patterns.Count,
        normal = new
        {
            input = normal.InputTokens, output = normal.OutputTokens, cost = Cost(normal), text = normal.Text,
            hasCode = ReplyAnalyser.Analyse(normal.Text, []).HasCode
        },
        rounds = session.QuestionRounds,
        reachedUnderstanding = session.ReachedUnderstanding,
        topicsCovered = session.TopicsCovered,
        presentedDesignWithoutCode,
        cost = session.Cost,
        input = session.InputTokens,
        output = session.OutputTokens,
        turns = session.Turns.Skip(1)
            .Select(t => new { role = t.Role, text = t.Text, input = t.InputTokens, output = t.OutputTokens })
    };
}

// Writing-plans: given an already-approved design, does the plan put a failing test before each task's
// implementation step (TDD order)? Compared against Normal asked for the same plan.
static async Task<object> RunPlan(LiveRunner runner, CancellationToken ct)
{
    var message = $"Existing code:\n{ComparisonExample.Context}\n\n{SuperpowersScenarios.ApprovedDesign}\n\n" +
                  "Write the implementation plan.";
    var normal = await runner.AskOnceAsync(SkillInstructions.For("Normal"), message, ct);
    var plan = await runner.AskOnceAsync(SuperpowersScenarios.WritingPlansSystem, message, ct);

    return new { mode = "plan", outputWeight = CostCalculator.OutputWeight, normal = ToPlanReply(normal), skill = ToPlanReply(plan) };
}

static object ToPlanReply(SingleReply reply)
{
    var m = PlanAnalyser.Analyse(reply.Text);
    return new
    {
        input = reply.InputTokens, output = reply.OutputTokens, cost = Cost(reply), text = reply.Text,
        taskCount = m.TaskCount, testFirst = m.TasksWithTestFirst
    };
}

// Code review: the given code has one planted defect (a shipped order can still be cancelled). Does the
// reply call it out? Compared against Normal asked to review the same code.
static async Task<object> RunReview(LiveRunner runner, CancellationToken ct)
{
    var message = $"{SuperpowersScenarios.CodeReviewPrompt}\n\n```csharp\n{SuperpowersScenarios.BuggyContext}\n```";
    var normal = await runner.AskOnceAsync(SkillInstructions.For("Normal"), message, ct);
    var review = await runner.AskOnceAsync(SuperpowersScenarios.CodeReviewSystem, message, ct);

    return new { mode = "review", outputWeight = CostCalculator.OutputWeight, normal = ToReviewReply(normal), skill = ToReviewReply(review) };
}

static object ToReviewReply(SingleReply reply) => new
{
    input = reply.InputTokens, output = reply.OutputTokens, cost = Cost(reply), text = reply.Text,
    foundDefect = SuperpowersScenarios.FoundPlantedDefect(reply.Text),
    grade = SuperpowersScenarios.GradeReview(reply.Text).ToString().ToLowerInvariant() // missed | partial | found
};

static string? ValidateInput(string prompt, string? context) =>
    prompt.Length is 0 or > 4000 ? "The prompt must be between 1 and 4000 characters."
    : context is { Length: > 8000 } ? "The existing code must be at most 8000 characters."
    : null;

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

static object ToReply(SingleReply reply, double cost, IReadOnlyCollection<string> knownSymbols) => new
{
    input = reply.InputTokens,
    output = reply.OutputTokens,
    cost,
    text = reply.Text,
    metrics = ToMetrics(reply.Text, knownSymbols)
};

static object ToMetrics(string text, IReadOnlyCollection<string> knownSymbols)
{
    var metrics = ReplyAnalyser.Analyse(text, knownSymbols);
    return new
    {
        codeLines = metrics.CodeLines,
        newTypes = metrics.NewTypes,
        reusedSymbols = metrics.ReusedSymbols,
        hasTest = metrics.HasTest,
        questions = metrics.Questions,
        hasCode = metrics.HasCode,
        topicsCovered = ClarifyingTopics.Covered(text)
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

public sealed record SuperpowersRequest(string? Mode);
