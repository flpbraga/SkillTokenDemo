using System.Text.RegularExpressions;

namespace SkillTokenDemo.Core;

/// <summary>
/// Decision points a good clarifying session raises for the cancel-order task. A topic counts as covered
/// only when a QUESTION (a line with '?') mentions it, so a reply that just decides silently does not score.
/// </summary>
public static class ClarifyingTopics
{
    public static readonly IReadOnlyDictionary<string, string> Patterns = new Dictionary<string, string>
    {
        ["existing"] = @"OrderService|CancelAsync|CanTransitionTo|already|existing|exist",
        ["shipped"] = @"shipped|409|400|conflict|enviad",
        ["reason"] = @"reason|motivo|audit|log",
        ["no-hard-delete"] = @"delet|remov|soft|hard|status",
        ["tests"] = @"test|teste"
    };

    /// <summary>The topics raised as questions in the reply.</summary>
    public static IReadOnlyList<string> Covered(string reply)
    {
        var questionLines = reply.Split('\n').Where(line => line.Contains('?')).ToList();
        return Patterns
            .Where(p => questionLines.Any(line => Regex.IsMatch(line, p.Value, RegexOptions.IgnoreCase)))
            .Select(p => p.Key)
            .ToList();
    }
}
