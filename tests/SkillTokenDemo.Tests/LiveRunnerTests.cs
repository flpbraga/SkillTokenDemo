using System.Net;
using System.Text;
using SkillTokenDemo.Core;

namespace SkillTokenDemo.Tests;

public class LiveRunnerTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private const string Reply = """
        {"content":[{"type":"text","text":"Done.\n```csharp\nvar x = 1;\n```"}],
         "usage":{"input_tokens":100,"cache_read_input_tokens":20,"output_tokens":10}}
        """;

    [Fact]
    public async Task Sums_usage_across_one_call_per_user_turn()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Reply);
        var runner = new LiveRunner(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") }, "test-model");
        var scenario = ScenarioCatalogue.All().Single(s => s.Skill.Name == "grill-me");
        var userTurns = scenario.Turns.Count(t => t.Speaker == Speaker.User);

        var result = await runner.RunAsync(scenario);

        Assert.Equal(userTurns, handler.Requests.Count);
        Assert.Equal(120 * userTurns, result.InputTokens);
        Assert.Equal(10 * userTurns, result.OutputTokens);
        Assert.Equal(userTurns, result.CodeLines);
    }

    [Fact]
    public async Task Sends_the_skill_instructions_as_the_system_prompt()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Reply);
        var runner = new LiveRunner(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") }, "test-model");

        await runner.RunAsync(ScenarioCatalogue.All().Single(s => s.Skill.Name == "caveman"));

        Assert.Contains("terse", handler.Requests[0]);
    }

    [Fact]
    public async Task Surfaces_api_errors()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"error":"bad key"}""");
        var runner = new LiveRunner(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") }, "test-model");

        await Assert.ThrowsAsync<HttpRequestException>(() => runner.RunAsync(ScenarioCatalogue.All()[0]));
    }
}
