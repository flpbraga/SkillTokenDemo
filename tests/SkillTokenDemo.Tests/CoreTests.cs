using SkillTokenDemo.Core;

namespace SkillTokenDemo.Tests;

public class TokenEstimatorTests
{
    [Fact]
    public void Empty_text_is_zero_tokens() => Assert.Equal(0, TokenEstimator.Estimate("  "));

    [Fact]
    public void Longer_text_costs_more_tokens() =>
        Assert.True(TokenEstimator.Estimate("one two three four five six") > TokenEstimator.Estimate("one two"));
}

public class PromptComplexityTests
{
    [Fact]
    public void Short_prompt_is_simple_and_suggests_grill_me()
    {
        var report = PromptComplexity.Analyse("Add a cancel endpoint");

        Assert.Equal(ComplexityBand.Simple, report.Band);
        Assert.Contains(report.Suggestions, s => s.Contains("/grill-me"));
    }

    [Fact]
    public void Constraint_heavy_prompt_scores_higher()
    {
        var plain = PromptComplexity.Analyse("Add an endpoint to cancel an order.");
        var heavy = PromptComplexity.Analyse(
            "Add an endpoint to cancel an order. It must never hard-delete, should only allow open orders, " +
            "must always log, and should not change the schema. Avoid new dependencies.");

        Assert.True(heavy.Score > plain.Score);
        Assert.True(heavy.Constraints >= 5);
    }
}

public class ScenarioTests
{
    private static readonly Dictionary<string, ScenarioResult> Results =
        ScenarioCatalogue.All().Select(CostCalculator.Calculate).ToDictionary(r => r.Scenario.Skill.Name);

    [Fact]
    public void Input_includes_skill_overhead_on_every_assistant_turn()
    {
        var result = Results["superpowers"];
        Assert.True(result.InputTokens >= result.Scenario.Skill.OverheadTokens * result.AssistantTurns);
    }

    [Fact]
    public void Ponytail_writes_less_code_and_output_than_normal()
    {
        Assert.True(Results["ponytail"].CodeLines < Results["Normal"].CodeLines);
        Assert.True(Results["ponytail"].OutputTokens < Results["Normal"].OutputTokens);
    }

    [Fact]
    public void Caveman_reduces_output_but_keeps_the_rework_round()
    {
        Assert.True(Results["caveman"].OutputTokens < Results["Normal"].OutputTokens);
        Assert.Equal(Results["Normal"].AssistantTurns, Results["caveman"].AssistantTurns);
    }

    [Fact]
    public void Superpowers_is_the_most_expensive_flow() =>
        Assert.Equal("superpowers", Results.Values.MaxBy(r => r.CostUnits)!.Scenario.Skill.Name);

    [Theory]
    [InlineData("Normal")]
    [InlineData("grill-me")]
    [InlineData("caveman")]
    [InlineData("ponytail")]
    [InlineData("superpowers")]
    public void Per_turn_usage_sums_to_totals(string skill)
    {
        var result = Results[skill];

        Assert.Equal(result.AssistantTurns, result.PerTurnUsage.Count);
        Assert.Equal(result.InputTokens, result.PerTurnUsage.Sum(t => t.Input));
        Assert.Equal(result.OutputTokens, result.PerTurnUsage.Sum(t => t.Output));
    }

    [Fact]
    public void Saving_percent_is_relative_to_baseline() =>
        Assert.Equal(25.0, CostCalculator.SavingPercent(200, 150));
}
