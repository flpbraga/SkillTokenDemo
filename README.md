# SkillTokenDemo

A .NET 9 demo comparing the token cost and prompt complexity of working with Claude Code **normally**
versus with the `grill-me`, `caveman`, `ponytail` and `superpowers` skills.

## Run

```bash
dotnet run --project src/SkillTokenDemo.Cli                       # comparison table
dotnet run --project src/SkillTokenDemo.Cli -- transcripts        # table plus every conversation
dotnet run --project src/SkillTokenDemo.Cli -- analyse "your prompt here"   # complexity of your own prompt
dotnet test

export ANTHROPIC_API_KEY=...                                      # live mode: real, billed API calls
dotnet run --project src/SkillTokenDemo.Cli -- live [--model <id>] [--transcripts]
```

Live mode replays each scenario's scripted user turns against the Claude Messages API (about 17 calls
in total, default model `claude-sonnet-5-5`) and reports the real `usage` counts. The system prompt for each
skill is a short stand-in (see `SkillInstructions.cs`), not the real `SKILL.md`, so the overhead of
`superpowers` in particular is understated. Assistant replies are live; user turns stay scripted.

## Layout

- `src/SkillTokenDemo.Core`: token estimator, prompt complexity scoring, cost model, scenario catalogue.
- `src/SkillTokenDemo.Cli`: console report.
- `tests/SkillTokenDemo.Tests`: xUnit tests.

## How the numbers work

- One task ("add a cancel-order endpoint") is played out as a hand-written conversation per skill.
- Each assistant turn re-reads the skill instructions plus the whole conversation so far (input),
  then writes its reply (output). Cost = input + 5 x output.
- Tokens are estimated offline (average of 4 characters per token and 0.75 words per token).

## Caveats

- The conversations are illustrative, not recorded sessions. Skill overhead figures are assumptions,
  except caveman's (~1,160 input tokens per request, from its README).
- Prompt caching, which makes repeated skill instructions much cheaper, is not modelled. This penalises
  the skills with large instructions (caveman, superpowers).
- Vendor benchmarks differ from this demo: ponytail reports ~45% fewer output tokens, caveman reports
  large output savings against a verbose baseline but far less against "answer concisely".
- To get real figures, replace the estimator with the `usage` field returned by the API.

## Sources

- https://github.com/dietrichgebert/ponytail
- https://github.com/robmitt/grill-me-skill
- https://github.com/obra/superpowers
- https://github.com/juliusbrussee/caveman
