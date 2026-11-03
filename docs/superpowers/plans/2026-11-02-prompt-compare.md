# Prompt comparison (with vs without skill) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In the web UI, type any prompt, pick a skill, and see what one reply costs with the skill and without it, measured from real API usage.

**Architecture:** `LiveRunner` gets one public single-turn call. A new `PromptComparer` runs two calls with the same user prompt: one with the Normal system prompt (baseline), one with the chosen skill's system prompt. A new `POST /api/compare` endpoint exposes it, and the existing page gets a compare panel. The endpoint uses whatever backend `ANTHROPIC_BASE_URL` points to (Claude or local Ollama).

**Tech Stack:** .NET 9, xUnit, ASP.NET Core minimal API, vanilla HTML/JS.

**Spec:** No separate spec file. The design was agreed in chat on 2026-11-02 and is summarised in Goal and Architecture above. Confirmed by the user on 2026-11-02: show both replies side by side.

## Global Constraints

- Cost = input tokens + 5 x output tokens, the same formula as `CostCalculator.OutputWeight` (5.0). Do not change it.
- Numbers come from the API `usage` field. Do not estimate them in this feature.
- The API key is read on the server only. It never appears in a response or in the page.
- Prompt limit: 1 to 4000 characters after trimming.
- Skill names accepted by the endpoint: `grill-me`, `caveman`, `ponytail`, `superpowers`. `Normal` is rejected, because it is the baseline.

## Review Focus

1. Empty or whitespace-only prompt: must return 400 and must not call the API.
2. Prompt over 4000 characters: must return 400 and must not call the API.
3. Unknown skill name (including `Normal` and any casing variant that is not exact): must return 400.
4. API returns an error (for example 401 or 400 for low credit): the endpoint returns 502 with the upstream message, and the page shows it inline without losing the rest of the screen.
5. Reply text containing HTML or code: must show as plain text in the page.

## File Structure

- Modify: `src/SkillTokenDemo.Core/LiveRunner.cs`. Adds the public single-turn call. Responsibility stays "talk to the Messages API".
- Create: `src/SkillTokenDemo.Core/PromptComparer.cs`. Runs the two calls and builds the comparison. One responsibility.
- Modify: `src/SkillTokenDemo.Web/Program.cs`. Adds `/api/compare` and moves the runner setup into one helper shared with `/api/live`.
- Modify: `src/SkillTokenDemo.Web/wwwroot/index.html`. Adds the compare panel.
- Create: `tests/SkillTokenDemo.Tests/PromptComparerTests.cs`. Tests against a stub HTTP handler.

## Interfaces

- `SingleReply(string Text, int InputTokens, int OutputTokens)`: public record in `LiveRunner.cs`.
- `LiveRunner.AskOnceAsync(string system, string prompt, CancellationToken ct = default) -> Task<SingleReply>`.
- `PromptComparison(string Skill, string Prompt, SingleReply Baseline, SingleReply WithSkill)`: record in `PromptComparer.cs`, with computed `BaselineCost` and `WithSkillCost` properties.
- `PromptComparer.CompareAsync(LiveRunner runner, string skillName, string prompt, CancellationToken ct = default) -> Task<PromptComparison>`. It calls the baseline first, then the skill.

---

### Task 1: Single-turn call in LiveRunner

**Files:**
- Modify: `src/SkillTokenDemo.Core/LiveRunner.cs`
- Test: `tests/SkillTokenDemo.Tests/PromptComparerTests.cs` (created here, extended in Task 2)

**Interfaces:**
- Consumes: the existing private `SendAsync(string system, JsonArray messages, CancellationToken ct)`.
- Produces: `SingleReply` and `AskOnceAsync` as listed above.

- [ ] **Step 1: Write the failing test**

Create a stub handler that records each request body and returns a fixed Messages response:

```csharp
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
```

Test:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SkillTokenDemo.Tests --filter AskOnce_sends_the_system_prompt_and_reads_usage`
Expected: FAIL, `AskOnceAsync` does not exist.

- [ ] **Step 3: Implement**

Add `public sealed record SingleReply(string Text, int InputTokens, int OutputTokens);` in `LiveRunner.cs`. Add `public async Task<SingleReply> AskOnceAsync(string system, string prompt, CancellationToken ct = default)`. It builds a `JsonArray` with one user message, calls the existing `SendAsync`, and maps the tuple to `SingleReply`. Do not change `SendAsync` or `RunAsync`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SkillTokenDemo.Tests --filter AskOnce_sends_the_system_prompt_and_reads_usage`
Expected: PASS. Then run `dotnet test tests/SkillTokenDemo.Tests`; all 17 existing tests must still pass.

---

### Task 2: PromptComparer

**Files:**
- Create: `src/SkillTokenDemo.Core/PromptComparer.cs`
- Test: `tests/SkillTokenDemo.Tests/PromptComparerTests.cs`

**Interfaces:**
- Consumes: `LiveRunner.AskOnceAsync`, `SkillInstructions.For(string)`, `CostCalculator.OutputWeight`.
- Produces: `PromptComparison` and `PromptComparer.CompareAsync` as listed above.

- [ ] **Step 1: Write the failing test**

Add to `PromptComparerTests.cs`, reusing `StubHandler` from Task 1:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SkillTokenDemo.Tests --filter Compare_sends_normal_first`
Expected: FAIL, `PromptComparer` does not exist.

- [ ] **Step 3: Implement**

In `PromptComparer.cs`, add the record `PromptComparison` with `BaselineCost` and `WithSkillCost` computed as `Input + Output * CostCalculator.OutputWeight`. Add `public static class PromptComparer` with `CompareAsync`. It calls `AskOnceAsync` for the baseline with `SkillInstructions.For("Normal")`, then for the skill with `SkillInstructions.For(skillName)`, both with the same `prompt`. It runs them sequentially: baseline first, then skill.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SkillTokenDemo.Tests`
Expected: PASS, 18 tests in total.

---

### Task 3: /api/compare endpoint

**Files:**
- Modify: `src/SkillTokenDemo.Web/Program.cs`

**Interfaces:**
- Consumes: `PromptComparer.CompareAsync`, `LiveRunner.CreateClient`, `ScenarioCatalogue.All()`, `CostCalculator.SavingPercent`.
- Produces: `POST /api/compare`, body `{ "skill": string, "prompt": string }`.

- [ ] **Step 1: Extract the runner setup**

Move the key lookup, `ANTHROPIC_BASE_URL` and `ANTHROPIC_MODEL` resolution out of `/api/live` into one private static method, `static (LiveRunner? Runner, IDisposable? Client, string? Error) CreateRunner(IConfiguration config, string? model)`. `/api/live` must use it and keep its current behaviour. Confirm `/api/live` still returns 400 with no key by running `curl -X POST localhost:5055/api/live` with the app started and no key configured.

- [ ] **Step 2: Add the endpoint**

Add `app.MapPost("/api/compare", ...)` taking a request record `CompareRequest(string? Skill, string? Prompt)`. Validation, in this order:
- Prompt trimmed is empty or longer than 4000 characters: 400 with `{ "error": "..." }`.
- Skill is not one of the names from `ScenarioCatalogue.All()` other than `Normal`: 400.
- `CreateRunner` returns an error: 400 with that error.
- `HttpRequestException` from the call: 502 with `{ "error": ex.Message }`.

On success, return:

```json
{
  "skill": "caveman",
  "prompt": "...",
  "outputWeight": 5,
  "baseline": { "input": 0, "output": 0, "cost": 0, "text": "..." },
  "withSkill": { "input": 0, "output": 0, "cost": 0, "text": "..." },
  "savingPercent": 0
}
```

`savingPercent` = `CostCalculator.SavingPercent(baseline.cost, withSkill.cost)`. Positive means the skill made it cheaper.

- [ ] **Step 3: Verify**

Build with `dotnet build SkillTokenDemo.sln`. Start the app against Ollama (the config in `appsettings.json` already points there). Then check the validation paths with curl and no real call:

```bash
curl -s -X POST localhost:5055/api/compare -H "content-type: application/json" -d '{"skill":"Normal","prompt":"hi"}'
curl -s -X POST localhost:5055/api/compare -H "content-type: application/json" -d '{"skill":"caveman","prompt":"   "}'
```

Expected: both return 400 with an `error` message.

Then a real call against Ollama:

```bash
curl -s -X POST localhost:5055/api/compare -H "content-type: application/json" -d '{"skill":"caveman","prompt":"Explain closures in one sentence"}'
```

Expected: 200 with `baseline` and `withSkill` both having `input > 0` and `output > 0`.

---

### Task 4: Compare panel in the UI

**Files:**
- Modify: `src/SkillTokenDemo.Web/wwwroot/index.html`

**Interfaces:**
- Consumes: `POST /api/compare` as defined in Task 3.

- [ ] **Step 1: Add the panel markup**

Insert a panel after the mode toggle and before the existing notice. It contains a `textarea#compare-prompt` (rows 3, maxlength 4000), a `select#compare-skill` with options `grill-me`, `caveman`, `ponytail`, `superpowers`, a `button#compare-send` labelled "Enviar", and a `div#compare-result`. Add one line of help text: "Usa o endpoint configurado (Claude ou Ollama). Cada envio faz duas chamadas reais."

- [ ] **Step 2: Add the rendering**

Add a click handler on `#compare-send`:
- Disable the button and show "Chamando o modelo..." in `#compare-result` while the request runs.
- POST JSON to `/api/compare`.
- On non-2xx, show the `error` field in the `.notice.error` style inside `#compare-result`. Do not clear previous results elsewhere on the page.
- On success, render a small table with two rows (Normal and the skill): input, output, cost. Below it, one line: "Com a skill: X% mais barato / mais caro que o Normal". Then two boxes side by side (one column on screens under 560px) showing each reply.
- Build all text with `textContent` or the existing `esc()` helper. Reply text must not be inserted as HTML.

- [ ] **Step 3: Verify**

Start the app, open the page, and send a prompt with each skill. Expected: table and both replies render, the button re-enables after each request, and a 400 or 502 shows its message inline. Check at a narrow viewport (about 375px) that there is no horizontal scroll.

---

### Task 5: Final verification

- [ ] **Step 1:** `dotnet build SkillTokenDemo.sln` with 0 errors and 0 warnings.
- [ ] **Step 2:** `dotnet test tests/SkillTokenDemo.Tests` with 18 of 18 passing.
- [ ] **Step 3:** Re-run the curl checks from Task 3 Step 3.
- [ ] **Step 4:** Report the result of each step. If anything fails, say so with the output.

## Commits

This plan has no commit steps. The git root is `~/Dev`, which contains unrelated staged changes from other projects, so a commit in this folder must be made by hand with an explicit path list.
