namespace SkillTokenDemo.Core;

/// <summary>
/// One prompt answered twice with real API usage: once with the Normal system prompt (baseline) and once
/// with the chosen skill's system prompt.
/// </summary>
public sealed record PromptComparison(
    string Skill,
    string Prompt,
    SingleReply Baseline,
    SingleReply WithSkill)
{
    public double BaselineCost => Cost(Baseline);
    public double WithSkillCost => Cost(WithSkill);

    private static double Cost(SingleReply reply) => reply.InputTokens + reply.OutputTokens * CostCalculator.OutputWeight;
}

public static class PromptComparer
{
    /// <summary>
    /// Runs the baseline first, then the skill, sequentially, with the same user message. When
    /// <paramref name="context"/> is given (existing code), it is sent before the task.
    /// </summary>
    public static async Task<PromptComparison> CompareAsync(
        LiveRunner runner, string skillName, string prompt, string? context = null, CancellationToken ct = default)
    {
        var message = BuildMessage(prompt, context);

        var baseline = await runner.AskOnceAsync(SkillInstructions.For("Normal"), message, ct);
        var withSkill = await runner.AskOnceAsync(SkillInstructions.For(skillName), message, ct);
        return new PromptComparison(skillName, prompt, baseline, withSkill);
    }

    /// <summary>The user message: the task alone, or the existing code followed by the task.</summary>
    public static string BuildMessage(string prompt, string? context) =>
        string.IsNullOrWhiteSpace(context) ? prompt : $"Existing code:\n{context}\n\nTask:\n{prompt}";
}
