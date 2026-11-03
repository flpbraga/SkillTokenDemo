using System.Text.RegularExpressions;

namespace SkillTokenDemo.Core;

/// <summary>
/// What a reply does with the code that already exists, measured from its text. No model is involved.
/// </summary>
public sealed record ReplyMetrics(
    int CodeLines,
    IReadOnlyList<string> NewTypes,
    IReadOnlyList<string> ReusedSymbols,
    bool HasTest,
    int Questions = 0,
    bool HasCode = false);

public static class ReplyAnalyser
{
    private static readonly Regex CodeFence = new(@"```[\s\S]*?(```|$)", RegexOptions.Compiled);

    // A declaration starts its own line (after optional modifiers), so prose such as "a class and" does not match.
    private static readonly Regex TypeDeclaration = new(
        @"^[ \t]*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|readonly)\s+)*(?:class|interface|record|struct|enum)\s+([A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex TestMarker =
        new(@"\[Fact\]|\[Test\]|\bAssert\.", RegexOptions.Compiled);

    /// <param name="reply">The model's reply.</param>
    /// <param name="knownSymbols">Names that already exist in the code the prompt gave the model.</param>
    public static ReplyMetrics Analyse(string reply, IReadOnlyCollection<string> knownSymbols)
    {
        var reused = knownSymbols
            .Where(s => Regex.IsMatch(reply, $@"\b{Regex.Escape(s)}\b"))
            .ToList();

        var newTypes = TypeDeclaration.Matches(reply)
            .Select(m => m.Groups[1].Value)
            .Where(name => !knownSymbols.Contains(name))
            .Distinct()
            .ToList();

        // Questions are counted in prose only, so a '?' inside code (ternaries, null-conditionals) does not count.
        var prose = CodeFence.Replace(reply, "");
        var questions = prose.Count(c => c == '?');

        return new ReplyMetrics(
            CostCalculator.CountCodeLines(reply),
            newTypes,
            reused,
            TestMarker.IsMatch(reply),
            questions,
            reply.Contains("```"));
    }
}
