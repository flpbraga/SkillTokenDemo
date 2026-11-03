namespace SkillTokenDemo.Core;

/// <summary>
/// Short stand-ins for each skill's instructions, used as the system prompt in live mode.
/// They paraphrase the public READMEs; they are not the real SKILL.md files, so live overhead
/// will be lower than a genuine install, especially for superpowers.
/// </summary>
public static class SkillInstructions
{
    private static readonly Dictionary<string, string> ByName = new()
    {
        ["Normal"] = "You are a helpful coding assistant.",
        ["grill-me"] = "You are a coding assistant. Before acting on a plan, interview the user one question at a time, " +
                       "walking the decision tree in dependency order and giving a recommended answer for each question. " +
                       "Only implement once you share an understanding.",
        ["caveman"] = "You are a coding assistant. Answer in terse, answer-first prose with no filler or pleasantries. " +
                      "Keep code, commands, paths and error messages verbatim. Keep numbers and negations.",
        ["ponytail"] = "You are a coding assistant. Read the relevant code first, then write the least code that works: " +
                       "does it need to exist, is it already here, does the standard library cover it? " +
                       "Never cut validation, security or error handling. Add one small test for risky logic.",
        ["superpowers"] = "You are a coding assistant following a structured process. Brainstorm first: classify the work, " +
                          "ask one clarifying question at a time, present a short design and wait for approval. " +
                          "Then write a short plan, implement test-first (red, green), and verify before claiming done."
    };

    public static string For(string skillName) =>
        ByName.TryGetValue(skillName, out var text) ? text : ByName["Normal"];
}
