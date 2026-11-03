# SkillTokenDemo

A .NET 9 demo that measures what the `grill-me`, `caveman`, `ponytail` and `superpowers` skills change, compared
with working **normally**. It has two parts:

- a **web UI** where you send a prompt and see the reply with and without a skill, measured the way that fits
  each skill (tokens are the wrong yardstick for some of them);
- a **console report** with an estimated token/cost table for one scripted task.

## Quick start (web UI, local model)

The LLM runs **locally in Docker**: `compose.yaml` starts [Ollama](https://ollama.com) and downloads a small model,
so you need Docker running and no API key.

```bash
docker compose up -d                          # starts the local LLM (Ollama), pulls qwen2.5-coder:1.5b once
dotnet run --project src/SkillTokenDemo.Web   # then open http://localhost:5055
```

The app binds to port 5055 (`Urls` in `appsettings.json`); port 5000 is taken by AirPlay on macOS.
Every call goes to the model configured in `src/SkillTokenDemo.Web/appsettings.json`, so with the defaults nothing
is billed and nothing leaves your machine.

## Local LLM (Docker Compose)

`compose.yaml` defines two services:

| Service | What it does |
|---|---|
| `ollama` | the local LLM server, on `http://localhost:11434`. It speaks the Anthropic Messages API, which is what the app calls. Models are kept in the `ollama-models` volume, so they survive restarts |
| `model-pull` | runs once after `ollama` is healthy and downloads `qwen2.5-coder:1.5b` (about 1 GB) |

```bash
docker compose up -d                       # start (first run downloads the model)
curl localhost:11434/api/tags              # check it is up and which models are installed
docker compose exec ollama ollama list     # same, from inside the container
docker compose down                        # stop (the downloaded model is kept)
```

To try another model, pull it (`docker compose exec ollama ollama pull <model>`) and set `ANTHROPIC_MODEL` in
`appsettings.json`. Ollama runs on the CPU inside Docker on macOS, so larger models are slow.

## Web UI

Pick a skill, then **Load example** (it picks the example that suits the skill) and **Send**. Each skill is
measured by what it is for:

| Skill | What runs | What is measured |
|---|---|---|
| `caveman` | the same prose prompt with and without the skill | output tokens (terser writing) |
| `ponytail` | a code task that has existing code to reuse | lines of code written, existing names reused |
| `grill-me` | a vague task; the model asks, a fact sheet answers, until it implements | question rounds, decision points raised, total cost of the whole conversation |
| `superpowers` | three separate checks (card shown only for this skill) | see below |

`superpowers` is a multi-step process, so each step has its own button and its own row in the table:

- **Brainstorm**: a vague task is clarified one question at a time; it should end in a design with no code.
- **Writing-plans**: given an approved design, does each task put a failing test before its implementation?
- **Code review**: given the requirement and code with one planted defect (a shipped order can still be
  cancelled), is the defect found? Graded found / partly / missed.

**Results so far** keeps the latest run per skill (and per superpowers step) in the browser's local storage, so
you can compare skills side by side. The **i** icon on each row explains what its numbers mean.

Cost is `input tokens + 5 x output tokens` everywhere, taken from the API's `usage` field.

## Configuration

`src/SkillTokenDemo.Web/appsettings.json`:

| Key | Default | Meaning |
|---|---|---|
| `ANTHROPIC_BASE_URL` | `http://localhost:11434` | Messages API endpoint. Remove it to use `api.anthropic.com` |
| `ANTHROPIC_MODEL` | `qwen2.5-coder:1.5b` | model id sent with every request |
| `ANTHROPIC_API_KEY` | `local` | Ollama ignores it; a real Claude key goes in user secrets (below) |

To use Claude instead, remove `ANTHROPIC_BASE_URL`, set `ANTHROPIC_MODEL` (for example `claude-sonnet-5-5`) and
store the key outside the repository:

```bash
dotnet user-secrets set "ANTHROPIC_API_KEY" "<your key>" --project src/SkillTokenDemo.Web
```

Never put a real key in `appsettings.json`. User secrets override it and live in your home folder.

## Console report

```bash
dotnet run --project src/SkillTokenDemo.Cli                                 # estimated comparison table
dotnet run --project src/SkillTokenDemo.Cli -- transcripts                  # table plus every conversation
dotnet run --project src/SkillTokenDemo.Cli -- analyse "your prompt here"   # complexity of your own prompt

export ANTHROPIC_API_KEY=...                                                # live mode: real, billed API calls
dotnet run --project src/SkillTokenDemo.Cli -- live [--model <id>] [--transcripts]
```

One task ("add a cancel-order endpoint") is played out as a hand-written conversation per skill. Each assistant
turn re-reads the skill instructions plus the whole conversation so far (input), then writes its reply (output).
Tokens are estimated offline (about 4 characters per token). Live mode replays the scripted user turns against the
Claude Messages API (about 17 calls) and reports the real `usage` counts.

## Layout

- `src/SkillTokenDemo.Core`: token estimator, cost model, scenario catalogue, live runner, prompt comparison, reply
  analysis, grill-me conversation loop, superpowers scenarios.
- `src/SkillTokenDemo.Cli`: console report.
- `src/SkillTokenDemo.Web`: minimal API and the single-page UI (`wwwroot/index.html`).
- `tests/SkillTokenDemo.Tests`: xUnit tests (`dotnet test`).
- `compose.yaml`: the local LLM (Ollama in Docker) with a one-shot service that pulls the model.
- `docs/superpowers`: design spec and plan for the prompt comparison.

## Caveats

- The skill instructions are short stand-ins (see `SkillInstructions.cs` and `SuperpowersScenarios.cs`), not the
  real `SKILL.md` files, so the overhead of `superpowers` in particular is understated.
- The text metrics (code lines, reused names, questions, plan tasks, planted defect) are pattern matches on the
  reply, not a judge. They are cheap and repeatable, and they can be fooled; a review graded "found" means the
  reply names the shipped-order case, not that its fix is correct.
- A small local model is the limit of what you see. With `qwen2.5-coder:1.5b`, `grill-me` and brainstorming often
  implement without asking, and code review finds the planted defect in only some runs. `0.5b` is too small: it
  echoes the question back. Run each check several times before drawing conclusions.
- Prompt caching, which makes repeated skill instructions much cheaper, is not modelled.
- Vendor benchmarks differ from this demo: ponytail reports ~45% fewer output tokens, caveman reports large
  output savings against a verbose baseline but far less against "answer concisely".

## Sources

- https://github.com/dietrichgebert/ponytail
- https://github.com/robmitt/grill-me-skill
- https://github.com/obra/superpowers
- https://github.com/juliusbrussee/caveman
