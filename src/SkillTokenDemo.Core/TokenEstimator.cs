namespace SkillTokenDemo.Core;

/// <summary>
/// Offline token estimate. Averages two common rules of thumb (4 characters per token,
/// 0.75 words per token) so the demo needs no tokenizer or API key. Real counts vary by model.
/// </summary>
public static class TokenEstimator
{
    public static int Estimate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var byCharacters = text.Length / 4.0;
        var byWords = CountWords(text) / 0.75;
        return (int)Math.Ceiling((byCharacters + byWords) / 2.0);
    }

    public static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
