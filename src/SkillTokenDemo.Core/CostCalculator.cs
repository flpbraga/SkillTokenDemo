namespace SkillTokenDemo.Core;

/// <summary>
/// Turns a conversation into token totals. Every assistant turn re-reads the skill instructions and
/// the whole conversation so far (input), then writes its reply (output).
/// </summary>
public static class CostCalculator
{
    /// <summary>Output tokens are typically priced several times higher than input tokens.</summary>
    public const double OutputWeight = 5.0;

    public static ScenarioResult Calculate(Scenario scenario)
    {
        var input = 0;
        var output = 0;
        var history = 0;
        var assistantTurns = 0;
        var userTurns = 0;
        var codeLines = 0;
        var perTurn = new List<TurnUsage>();

        foreach (var turn in scenario.Turns)
        {
            var tokens = TokenEstimator.Estimate(turn.Text);

            if (turn.Speaker == Speaker.User)
            {
                userTurns++;
            }
            else
            {
                assistantTurns++;
                var turnInput = scenario.Skill.OverheadTokens + history;
                input += turnInput;
                output += tokens;
                codeLines += CountCodeLines(turn.Text);
                perTurn.Add(new TurnUsage(turn.Label, turnInput, tokens));
            }

            history += tokens;
        }

        return new ScenarioResult(scenario, input, output, assistantTurns, userTurns,
            input + output * OutputWeight, codeLines, perTurn);
    }

    public static double SavingPercent(double baseline, double actual) =>
        baseline == 0 ? 0 : (baseline - actual) / baseline * 100.0;

    public static int CountCodeLines(string text)
    {
        var inFence = false;
        var lines = 0;

        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }
            else if (inFence && !string.IsNullOrWhiteSpace(line))
            {
                lines++;
            }
        }

        return lines;
    }
}
