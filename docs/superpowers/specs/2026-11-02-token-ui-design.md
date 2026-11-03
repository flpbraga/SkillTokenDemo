# Token accounting UI: design

## Goal
A simple web page for presenting the demo. For each skill (Normal, grill-me, caveman, ponytail, superpowers)
it shows how many tokens were spent, and where. Numbers come from the existing `SkillTokenDemo.Core`;
nothing is reimplemented.

## Scope
In: estimated mode (offline), live mode (real API usage), per-skill totals, per-turn breakdown, transcript.
Out: run history, export, authentication, JS framework, charting library.

## Components

### 1. Core change (additive)
- New record `TurnUsage(string Label, int Input, int Output)`.
- `ScenarioResult` gets a trailing parameter `IReadOnlyList<TurnUsage> TurnUsage` (default empty) so existing
  callers and the CLI keep compiling.
- `CostCalculator.Calculate` fills it, one entry per assistant turn (input = overhead + history, output = reply tokens).
- `LiveRunner.RunAsync` fills it from the `usage` of each API call.
- Invariant: sum of `TurnUsage.Input` = `InputTokens`, sum of `TurnUsage.Output` = `OutputTokens`.

### 2. `src/SkillTokenDemo.Web`
- ASP.NET Core minimal API, net9.0, added to `SkillTokenDemo.sln`, references `Core`. Static files from `wwwroot`.
- `GET /api/results`: estimated results for all scenarios.
- `POST /api/live?model=<id>`: runs `LiveRunner` using `ANTHROPIC_API_KEY` read on the server.
  Missing key returns 400 with a clear message; an upstream API failure returns 502 with the message.
- Response shape (both endpoints): `{ mode, outputWeight, task, skills: [{ name, command, mechanism, takeaway,
  overheadTokens, turns, inputTokens, outputTokens, cost, codeLines, vsNormalPercent,
  turnUsage: [{ label, input, output }], transcript: [{ speaker, label, text }] }] }`.
- The API key never reaches the browser.

### 3. UI (`wwwroot/index.html`, vanilla HTML/JS/CSS, no build)
- Header: Estimated / Live toggle. Live asks for confirmation (about 17 billed calls). Fixed caveat banner:
  figures are illustrative; skill instructions in live mode are stand-ins.
- Summary table per skill: turns, overhead, input, output, cost (input + 5 x output), change vs Normal.
- Stacked bar per skill: skill overhead (turns x overhead), rest of input, output.
- Click a skill to expand the per-turn table (input/output per assistant reply) and the transcript.
- Light and dark theme via CSS variables; usable at phone width.

## Errors
Live failure shows an inline message; estimated mode keeps working and stays on screen.

## Testing
- xUnit (Core): per-turn sums equal totals for every scenario in `ScenarioCatalogue`.
- Manual: run the Web app, `curl /api/results`, check the shape and that totals match the CLI table.
- No `WebApplicationFactory` (avoids an extra test dependency).
