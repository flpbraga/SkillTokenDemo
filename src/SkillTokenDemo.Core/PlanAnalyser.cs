using System.Text.RegularExpressions;

namespace SkillTokenDemo.Core;

/// <summary>How many tasks a plan reply has, and how many put a test step before the implementation step.</summary>
public sealed record PlanMetrics(int TaskCount, int TasksWithTestFirst);

public static class PlanAnalyser
{
    private static readonly Regex TaskHeader = new(@"(?im)^.{0,10}\bTask\s*\d+\b", RegexOptions.Compiled);
    private static readonly Regex ImplementMarker = new(@"(?i)\bimplement\b", RegexOptions.Compiled);

    /// <summary>Splits the reply into "Task N" sections and checks each one for test-before-implementation order.</summary>
    public static PlanMetrics Analyse(string reply)
    {
        var headers = TaskHeader.Matches(reply).Cast<Match>().ToList();
        if (headers.Count == 0)
        {
            return new PlanMetrics(0, 0);
        }

        var testFirst = 0;
        for (var i = 0; i < headers.Count; i++)
        {
            var start = headers[i].Index;
            var end = i + 1 < headers.Count ? headers[i + 1].Index : reply.Length;
            var body = reply[start..end];

            var testIndex = body.IndexOf("test", StringComparison.OrdinalIgnoreCase);
            var implementIndex = FirstImplementationMarker(body);
            if (testIndex >= 0 && (implementIndex < 0 || testIndex < implementIndex))
            {
                testFirst++;
            }
        }

        return new PlanMetrics(headers.Count, testFirst);
    }

    /// <summary>The first sign of writing the solution itself: a code fence, or the word "implement".</summary>
    private static int FirstImplementationMarker(string body)
    {
        var fence = body.IndexOf("```", StringComparison.Ordinal);
        var implement = ImplementMarker.Match(body) is { Success: true } m ? m.Index : -1;
        if (fence < 0)
        {
            return implement;
        }

        return implement < 0 ? fence : Math.Min(fence, implement);
    }
}
