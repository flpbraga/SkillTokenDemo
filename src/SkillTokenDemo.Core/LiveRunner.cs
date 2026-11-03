using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SkillTokenDemo.Core;

/// <summary>The reply to one single-turn call and the usage it was billed for.</summary>
public sealed record SingleReply(string Text, int InputTokens, int OutputTokens);

/// <summary>
/// Replays each scenario's user turns against the Claude Messages API and records the real
/// <c>usage</c> counts. The assistant replies are live; the user turns stay scripted.
/// </summary>
public sealed class LiveRunner(HttpClient http, string model, int maxTokens = 2048)
{
    public const string DefaultModel = "claude-sonnet-5-5";

    public async Task<ScenarioResult> RunAsync(Scenario scenario, CancellationToken ct = default)
    {
        var system = SkillInstructions.For(scenario.Skill.Name);
        var messages = new JsonArray();
        var turns = new List<Turn>();
        var input = 0;
        var output = 0;
        var perTurn = new List<TurnUsage>();

        foreach (var turn in scenario.Turns.Where(t => t.Speaker == Speaker.User))
        {
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = turn.Text });
            turns.Add(turn);

            var reply = await SendAsync(system, messages, ct);
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = reply.Text });
            turns.Add(new Turn(Speaker.Assistant, "Live reply", reply.Text));

            input += reply.InputTokens;
            output += reply.OutputTokens;
            perTurn.Add(new TurnUsage("Live reply", reply.InputTokens, reply.OutputTokens));
        }

        var live = scenario with { Turns = turns };
        return new ScenarioResult(live, input, output, turns.Count(t => t.Speaker == Speaker.Assistant),
            turns.Count(t => t.Speaker == Speaker.User), input + output * CostCalculator.OutputWeight,
            CostCalculator.Calculate(live).CodeLines, perTurn);
    }

    /// <summary>One single-turn call with the given system prompt. Used by the prompt comparison.</summary>
    public async Task<SingleReply> AskOnceAsync(string system, string prompt, CancellationToken ct = default)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = prompt } };
        var reply = await SendAsync(system, messages, ct);
        return new SingleReply(reply.Text, reply.InputTokens, reply.OutputTokens);
    }

    private async Task<(string Text, int InputTokens, int OutputTokens)> SendAsync(
        string system, JsonArray messages, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["system"] = system,
            ["messages"] = JsonNode.Parse(messages.ToJsonString())
        };

        using var response = await http.PostAsJsonAsync("v1/messages", body, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Claude API returned {(int)response.StatusCode}: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var text = string.Concat(doc.RootElement.GetProperty("content").EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "text")
            .Select(b => b.GetProperty("text").GetString()));

        var usage = doc.RootElement.GetProperty("usage");
        var input = ReadInt(usage, "input_tokens") + ReadInt(usage, "cache_creation_input_tokens") +
                    ReadInt(usage, "cache_read_input_tokens");

        return (text, input, ReadInt(usage, "output_tokens"));
    }

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    /// <summary>Creates a client. Pass a local compatible server (e.g. Ollama at http://localhost:11434/) as baseUrl.</summary>
    public static HttpClient CreateClient(string apiKey, string baseUrl = "https://api.anthropic.com/")
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/") };
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        return client;
    }
}
