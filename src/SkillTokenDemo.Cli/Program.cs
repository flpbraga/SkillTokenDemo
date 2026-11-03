using SkillTokenDemo.Core;

return args switch
{
    [] or ["report"] => Report(showTranscripts: false),
    ["transcripts"] => Report(showTranscripts: true),
    ["analyse", .. var words] when words.Length > 0 => Analyse(string.Join(' ', words)),
    ["live", .. var options] => await Live(options),
    _ => Usage()
};

static async Task<int> Live(string[] options)
{
    var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.Error.WriteLine("Set ANTHROPIC_API_KEY to use live mode. It makes real, billed API calls.");
        return 1;
    }

    var modelIndex = Array.IndexOf(options, "--model");
    var model = modelIndex >= 0 && modelIndex + 1 < options.Length ? options[modelIndex + 1] : LiveRunner.DefaultModel;
    var showTranscripts = options.Contains("--transcripts");

    Console.WriteLine($"Live mode: model {model}. Calling the Claude API once per user turn...");

    using var http = LiveRunner.CreateClient(apiKey);
    var runner = new LiveRunner(http, model);
    var results = new List<ScenarioResult>();

    foreach (var scenario in ScenarioCatalogue.All())
    {
        Console.WriteLine($"  running {scenario.Skill.Name}");
        results.Add(await runner.RunAsync(scenario));
    }

    Console.WriteLine();
    return Report(showTranscripts, results, "LIVE usage from the API (skill instructions are short stand-ins, not the real SKILL.md files)");
}

static int Report(bool showTranscripts, IReadOnlyList<ScenarioResult>? precomputed = null, string? heading = null)
{
    var results = precomputed ?? ScenarioCatalogue.All().Select(CostCalculator.Calculate).ToList();
    var baseline = results[0];

    if (heading is not null)
    {
        Console.WriteLine(heading);
        Console.WriteLine();
    }

    Console.WriteLine("Task prompt");
    Console.WriteLine($"  {ScenarioCatalogue.TaskPrompt}");
    WriteComplexity(PromptComplexity.Analyse(ScenarioCatalogue.TaskPrompt), indent: "  ");
    Console.WriteLine();

    Console.WriteLine($"{"Skill",-12} {"Turns",5} {"Overhead",9} {"Input",7} {"Output",7} {"Code",5} {"Cost*",8} {"vs normal",10}");
    Console.WriteLine(new string('-', 70));

    foreach (var r in results)
    {
        var saving = CostCalculator.SavingPercent(baseline.CostUnits, r.CostUnits);
        var delta = r == baseline ? "baseline" : $"{-saving:+0;-0}%";
        Console.WriteLine(
            $"{r.Scenario.Skill.Name,-12} {r.AssistantTurns + r.UserTurns,5} {r.Scenario.Skill.OverheadTokens,9} " +
            $"{r.InputTokens,7} {r.OutputTokens,7} {r.CodeLines,5} {r.CostUnits,8:N0} {delta,10}");
    }

    Console.WriteLine();
    Console.WriteLine($"* Cost = input tokens + {CostCalculator.OutputWeight:0}x output tokens (output is priced higher).");
    Console.WriteLine("  Input counts the skill instructions plus the whole conversation, re-read on every assistant turn.");
    Console.WriteLine();

    foreach (var r in results)
    {
        Console.WriteLine($"{r.Scenario.Skill.Command,-13} {r.Scenario.Skill.Mechanism}");
        Console.WriteLine($"{"",13} -> {r.Scenario.Takeaway}");
    }

    if (showTranscripts)
    {
        foreach (var r in results)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {r.Scenario.Skill.Name} ===");
            foreach (var turn in r.Scenario.Turns)
            {
                Console.WriteLine($"[{turn.Speaker}: {turn.Label}, ~{TokenEstimator.Estimate(turn.Text)} tokens]");
                Console.WriteLine(turn.Text.TrimEnd());
                Console.WriteLine();
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine("Illustrative only: conversations are hand-written and token counts are estimates.");
    return 0;
}

static int Analyse(string prompt)
{
    Console.WriteLine("Prompt");
    Console.WriteLine($"  {prompt}");
    WriteComplexity(PromptComplexity.Analyse(prompt), indent: "  ");
    return 0;
}

static void WriteComplexity(PromptComplexityReport c, string indent)
{
    Console.WriteLine($"{indent}Complexity: {c.Band} (score {c.Score}/100) | ~{c.Tokens} tokens, {c.Words} words, " +
                      $"{c.Sentences} sentences, {c.Constraints} constraints, {c.Questions} questions");
    foreach (var suggestion in c.Suggestions)
    {
        Console.WriteLine($"{indent}Tip: {suggestion}");
    }
}

static int Usage()
{
    Console.WriteLine("""
        Usage:
          dotnet run --project src/SkillTokenDemo.Cli                     Comparison table
          dotnet run --project src/SkillTokenDemo.Cli -- transcripts      Table plus every conversation
          dotnet run --project src/SkillTokenDemo.Cli -- analyse <text>   Complexity of your own prompt
          dotnet run --project src/SkillTokenDemo.Cli -- live [--model <id>] [--transcripts]
                                                                          Real API usage (needs ANTHROPIC_API_KEY, billed)
        """);
    return 1;
}
