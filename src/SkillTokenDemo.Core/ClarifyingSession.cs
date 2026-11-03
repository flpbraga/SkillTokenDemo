namespace SkillTokenDemo.Core;

/// <summary>One message in a clarifying session. User turns come from the simulated user and cost no tokens.</summary>
public sealed record DialogueTurn(string Role, string Text, int InputTokens, int OutputTokens);

public sealed record ClarifyingResult(
    IReadOnlyList<DialogueTurn> Turns,
    int QuestionRounds,
    bool ReachedUnderstanding,
    IReadOnlyList<string> TopicsCovered)
{
    public int InputTokens => Turns.Sum(t => t.InputTokens);
    public int OutputTokens => Turns.Sum(t => t.OutputTokens);
    public double Cost => InputTokens + OutputTokens * CostCalculator.OutputWeight;
    public string FinalReply => Turns.Last(t => t.Role == "assistant").Text;
}

/// <summary>What the loop treats as the end of clarifying, since the two skills finish on different signals.</summary>
public enum ClarifyingGoal
{
    /// <summary>grill-me: done once the reply contains code (small models often ask without a question mark).</summary>
    Implementation,

    /// <summary>brainstorming: done once the reply asks no question, whether or not it contains code — the
    /// caller then checks the final reply for code to tell a presented design from a skipped one.</summary>
    Design
}

/// <summary>
/// Runs a question-and-answer loop: the model asks, <c>answer</c> replies, until <paramref name="goal"/> says
/// the model is done or <c>maxRounds</c> replies have been answered.
/// </summary>
public static class ClarifyingSession
{
    public static async Task<ClarifyingResult> RunAsync(
        LiveRunner runner,
        string system,
        string firstMessage,
        Func<string, string> answer,
        ClarifyingGoal goal = ClarifyingGoal.Implementation,
        int maxRounds = 6,
        CancellationToken ct = default)
    {
        var conversation = new List<(string Role, string Text)> { ("user", firstMessage) };
        var turns = new List<DialogueTurn> { new("user", firstMessage, 0, 0) };
        var topics = new List<string>();
        var rounds = 0;

        while (true)
        {
            var reply = await runner.AskAsync(system, conversation, ct);
            conversation.Add(("assistant", reply.Text));
            turns.Add(new DialogueTurn("assistant", reply.Text, reply.InputTokens, reply.OutputTokens));
            topics.AddRange(ClarifyingTopics.Covered(reply.Text).Except(topics));

            var metrics = ReplyAnalyser.Analyse(reply.Text, []);
            var asks = goal == ClarifyingGoal.Implementation ? !metrics.HasCode : metrics.Questions > 0;
            if (!asks || rounds == maxRounds)
            {
                return new ClarifyingResult(turns, rounds + (asks ? 1 : 0), !asks, topics);
            }

            rounds++;
            var response = answer(reply.Text);
            conversation.Add(("user", response));
            turns.Add(new DialogueTurn("user", response, 0, 0));
        }
    }
}
