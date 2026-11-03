using System.Text.RegularExpressions;

namespace SkillTokenDemo.Core;

public enum ComplexityBand
{
    Simple,
    Moderate,
    Complex
}

public sealed record PromptComplexityReport(
    int Tokens,
    int Words,
    int Sentences,
    int Constraints,
    int Questions,
    int Score,
    ComplexityBand Band,
    IReadOnlyList<string> Suggestions);

/// <summary>
/// Scores how demanding a prompt is. The score is a heuristic, not a model measurement:
/// longer prompts with more constraints and open questions push the model towards longer answers.
/// </summary>
public static partial class PromptComplexity
{
    public static PromptComplexityReport Analyse(string prompt)
    {
        var tokens = TokenEstimator.Estimate(prompt);
        var words = string.IsNullOrWhiteSpace(prompt) ? 0 : TokenEstimator.CountWords(prompt);
        var sentences = Math.Max(words == 0 ? 0 : 1, SentenceEnd().Matches(prompt).Count);
        var constraints = ConstraintWords().Matches(prompt).Count;
        var questions = prompt.Count(c => c == '?');

        var score = Math.Min(100, words / 4 + sentences * 2 + constraints * 5 + questions * 3);
        var band = score switch
        {
            < 25 => ComplexityBand.Simple,
            < 55 => ComplexityBand.Moderate,
            _ => ComplexityBand.Complex
        };

        return new PromptComplexityReport(tokens, words, sentences, constraints, questions, score, band,
            BuildSuggestions(words, constraints, questions, band));
    }

    private static List<string> BuildSuggestions(int words, int constraints, int questions, ComplexityBand band)
    {
        var suggestions = new List<string>();

        if (words > 0 && words < 12)
        {
            suggestions.Add("Very short prompt: expect clarifying questions. /grill-me turns that into a deliberate step.");
        }

        if (constraints >= 6)
        {
            suggestions.Add("Many constraints in one prompt: split into a plan (/superpowers) before writing code.");
        }

        if (questions >= 3)
        {
            suggestions.Add("Several open questions: expect long, explanatory answers. /caveman trims the prose.");
        }

        if (band == ComplexityBand.Simple && words >= 12)
        {
            suggestions.Add("Clear, small task: /ponytail keeps the change minimal and /caveman keeps the reply short.");
        }

        if (suggestions.Count == 0)
        {
            suggestions.Add("Balanced prompt: any skill is optional; /ponytail is the safest default for code tasks.");
        }

        return suggestions;
    }

    [GeneratedRegex(@"[.!?](\s|$)")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\b(must|should|never|always|only|without|ensure|require[sd]?|avoid|do not|don't)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ConstraintWords();
}
