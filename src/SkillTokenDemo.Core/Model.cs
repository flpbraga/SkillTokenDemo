namespace SkillTokenDemo.Core;

public enum Speaker
{
    User,
    Assistant
}

/// <summary>One message in a simulated conversation.</summary>
public sealed record Turn(Speaker Speaker, string Label, string Text);

/// <summary>
/// A skill and the tokens its instructions add to every request while it is active.
/// The overhead figures are illustrative assumptions, not measurements.
/// </summary>
public sealed record SkillProfile(string Name, string Command, string Mechanism, int OverheadTokens);

/// <summary>A complete simulated conversation for one way of working.</summary>
public sealed record Scenario(SkillProfile Skill, IReadOnlyList<Turn> Turns, string Takeaway);

/// <summary>Token usage of one assistant reply: the input it was billed for and the output it wrote.</summary>
public sealed record TurnUsage(string Label, int Input, int Output);

public sealed record ScenarioResult(
    Scenario Scenario,
    int InputTokens,
    int OutputTokens,
    int AssistantTurns,
    int UserTurns,
    double CostUnits,
    int CodeLines,
    IReadOnlyList<TurnUsage>? PerTurn = null)
{
    /// <summary>Per-reply usage. Sums to <see cref="InputTokens"/> and <see cref="OutputTokens"/>.</summary>
    public IReadOnlyList<TurnUsage> PerTurnUsage => PerTurn ?? [];
}
