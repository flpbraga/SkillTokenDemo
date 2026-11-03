using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SkillTokenDemo.Core;

namespace SkillTokenDemo.Tests;

/// <summary>Returns a fixed Messages reply and records every request body it receives.</summary>
public sealed class StubHandler(string replyJson) : HttpMessageHandler
{
    public List<JsonNode> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(replyJson, Encoding.UTF8, "application/json")
        };
    }
}

public class AskOnceTests
{
    [Fact]
    public async Task AskOnce_sends_the_system_prompt_and_reads_usage()
    {
        var stub = new StubHandler("""{"content":[{"type":"text","text":"OK"}],"usage":{"input_tokens":31,"output_tokens":2}}""");
        var runner = new LiveRunner(new HttpClient(stub) { BaseAddress = new Uri("http://test/") }, "m");

        var reply = await runner.AskOnceAsync("SYSTEM TEXT", "Say OK");

        Assert.Equal("OK", reply.Text);
        Assert.Equal(31, reply.InputTokens);
        Assert.Equal(2, reply.OutputTokens);
        Assert.Equal("SYSTEM TEXT", stub.Requests[0]["system"]!.GetValue<string>());
    }
}

public class PromptComparerTests
{
    [Fact]
    public async Task Compare_sends_normal_first_then_the_skill_with_the_same_prompt()
    {
        var stub = new StubHandler("""{"content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":100,"output_tokens":10}}""");
        var runner = new LiveRunner(new HttpClient(stub) { BaseAddress = new Uri("http://test/") }, "m");

        var result = await PromptComparer.CompareAsync(runner, "caveman", "Explain closures");

        Assert.Equal(SkillInstructions.For("Normal"), stub.Requests[0]["system"]!.GetValue<string>());
        Assert.Equal(SkillInstructions.For("caveman"), stub.Requests[1]["system"]!.GetValue<string>());
        Assert.Equal("Explain closures", stub.Requests[1]["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal(100 + 10 * CostCalculator.OutputWeight, result.BaselineCost);
    }

    [Fact]
    public async Task Compare_puts_the_existing_code_before_the_task()
    {
        var stub = new StubHandler("""{"content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":1,"output_tokens":1}}""");
        var runner = new LiveRunner(new HttpClient(stub) { BaseAddress = new Uri("http://test/") }, "m");

        await PromptComparer.CompareAsync(runner, "ponytail", "Add the endpoint", context: "class OrderService { }");

        var content = stub.Requests[1]["messages"]![0]!["content"]!.GetValue<string>();
        Assert.StartsWith("Existing code:", content);
        Assert.Contains("class OrderService { }", content);
        Assert.EndsWith("Add the endpoint", content);
    }
}
