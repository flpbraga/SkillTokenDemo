using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SkillTokenDemo.Core;

namespace SkillTokenDemo.Tests;

/// <summary>Returns the given replies in order (the last one repeats) and records every request body.</summary>
public sealed class SequenceStubHandler(params string[] replies) : HttpMessageHandler
{
    private int _next;
    public List<JsonNode> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!);
        var text = replies[Math.Min(_next++, replies.Length - 1)];
        var json = new JsonObject
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
            ["usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = 5 }
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }
}

public class ClarifyingSessionTests
{
    private static LiveRunner Runner(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") }, "m");

    [Fact]
    public async Task Answers_questions_until_the_model_implements()
    {
        var stub = new SequenceStubHandler(
            "Does OrderService already have CancelAsync?",
            "Should a shipped order return 409?",
            "Understood.\n```csharp\napp.MapPost(\"/orders/{id}/cancel\", () => 1);\n```");

        var result = await ClarifyingSession.RunAsync(Runner(stub), "SYS", "Add the endpoint", ComparisonExample.AnswerFor);

        Assert.True(result.ReachedUnderstanding);
        Assert.Equal(2, result.QuestionRounds);
        Assert.Equal(["existing", "shipped"], result.TopicsCovered);
        Assert.Contains("```csharp", result.FinalReply);
        Assert.Equal(30, result.InputTokens);
        Assert.Equal(15, result.OutputTokens);
    }

    [Fact]
    public async Task A_reply_without_code_is_not_understanding_yet()
    {
        var stub = new SequenceStubHandler(
            "Understood. Please provide the existing code for clarity.",
            "```csharp\napp.MapPost(\"/orders/{id}/cancel\", () => 1);\n```");

        var result = await ClarifyingSession.RunAsync(Runner(stub), "SYS", "Add the endpoint", ComparisonExample.AnswerFor);

        Assert.True(result.ReachedUnderstanding);
        Assert.Equal(1, result.QuestionRounds);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Sends_the_whole_conversation_and_the_fact_sheet_answer()
    {
        var stub = new SequenceStubHandler("Should a shipped order return 409 or 400?", "```csharp\nvar done = true;\n```");

        await ClarifyingSession.RunAsync(Runner(stub), "SYS", "Add the endpoint", ComparisonExample.AnswerFor);

        var second = stub.Requests[1]["messages"]!.AsArray();
        Assert.Equal(3, second.Count);
        Assert.Equal("assistant", second[1]!["role"]!.GetValue<string>());
        Assert.Contains("409", second[2]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Stops_after_max_rounds_when_the_model_keeps_asking()
    {
        var stub = new SequenceStubHandler("Anything else I should know?");

        var result = await ClarifyingSession.RunAsync(Runner(stub), "SYS", "Add the endpoint", ComparisonExample.AnswerFor, maxRounds: 3);

        Assert.False(result.ReachedUnderstanding);
        Assert.Equal(4, stub.Requests.Count);
    }

    [Fact]
    public async Task Design_goal_stops_as_soon_as_the_model_stops_asking_even_without_code()
    {
        var stub = new SequenceStubHandler("Does OrderService already have CancelAsync?", "Design: reuse OrderService.CancelAsync; map NotAllowed to 409.");

        var result = await ClarifyingSession.RunAsync(
            Runner(stub), "SYS", "Add the endpoint", ComparisonExample.AnswerFor, ClarifyingGoal.Design);

        Assert.True(result.ReachedUnderstanding);
        Assert.Equal(1, result.QuestionRounds);
        Assert.Equal(2, stub.Requests.Count);
        Assert.DoesNotContain("```", result.FinalReply);
    }

    [Fact]
    public async Task Design_goal_also_stops_on_a_reply_that_jumps_straight_to_code()
    {
        // No question mark means the loop stops either way; the caller checks the final reply for code
        // to tell a good design (no code) from a skipped one (code appeared before any question).
        var stub = new SequenceStubHandler("```csharp\nvar x = 1;\n```");

        var result = await ClarifyingSession.RunAsync(
            Runner(stub), "SYS", "Add the endpoint", ComparisonExample.AnswerFor, ClarifyingGoal.Design);

        Assert.True(result.ReachedUnderstanding);
        Assert.Equal(1, stub.Requests.Count);
        Assert.Contains("```", result.FinalReply);
    }

    [Fact]
    public void Fact_sheet_answers_the_topics_a_question_raises()
    {
        Assert.Contains("409", ComparisonExample.AnswerFor("Should a shipped order return 409 or 400?"));
        Assert.Equal(ComparisonExample.NoMoreConstraints, ComparisonExample.AnswerFor("What colour is the sky?"));
    }
}
