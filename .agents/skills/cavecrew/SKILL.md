---
name: cavecrew
description: >
  Decision guide for delegating to compressed caveman-style subagents. Routes
  Codex work across Luna XHigh, Terra Medium, Sol Medium, Sol High, and Sol Max
  while preserving existing Ollama-backed OpenCode agents. Use for investigation,
  scoped implementation, serious work, review, and evidence-based escalation.
  Trigger: "delegate to subagent", "use cavecrew", "spawn investigator",
  "spawn builder/reviewer/serious", "save context", "compressed agent output".
---

Cavecrew uses runtime-native subagents that emit compressed output, so main context shrinks per delegation.

## Model routing

Use runtime-native project agents. OpenCode definitions live in `.opencode/agents/`; Codex definitions live in `.codex/agents/`.

### Codex

| Task | Agent | Model / effort |
|---|---|---|
| Normal bug fix or clearly scoped feature | `cavecrew-builder` | Luna XHigh |
| Unclear task requiring exploration across several repository areas | `cavecrew-investigator` | Terra Medium |
| Complex bug, architecture, auth, payments, migrations, concurrency, or data-integrity work | `cavecrew-serious` | Sol Medium |
| Correctness, regression, security, or test review | `cavecrew-reviewer` | Sol Medium |
| Terra Medium or Sol Medium produced a concrete failed attempt | `cavecrew-escalation-high` | Sol High |
| Sol High failed and additional reasoning is still justified | `cavecrew-escalation-max` | Sol Max |
| Sol Ultra | No preset; only when user explicitly requests it and Max is insufficient |

Escalate from evidence, not task size or vague difficulty. Failure means unresolved root cause, insufficient evidence, failed verification, or regression. Pass prior findings and failure details into the escalation agent. Do not override agent-file model or reasoning assignments at spawn time. Do not use Sol Ultra automatically.

### OpenCode

OpenCode remains on its working Ollama setup:

| Agent | Model | Use |
|---|---|---|
| `cavecrew-investigator` | `ollama/minimax-m3:cloud` | Investigation and diagnosis |
| `cavecrew-builder` | `ollama/kimi-k2.7-code:cloud` | Scoped implementation |
| `cavecrew-reviewer` | `ollama/glm-5.2:cloud` | Correctness and risk review |

Do not copy Codex model assignments into `.opencode/agents/`.

## When to use cavecrew vs alternatives

| Task | Use |
|---|---|
| "Where is X defined / what calls Y / list uses of Z" | `cavecrew-investigator` |
| Unclear task requiring broad repository exploration | `cavecrew-investigator` |
| Normal bug fix or clearly scoped feature | `cavecrew-builder` |
| Complex or high-consequence task | `cavecrew-serious` |
| Review diff, branch, or file for bugs | `cavecrew-reviewer` |
| Medium attempt failed with concrete evidence | `cavecrew-escalation-high` |
| High attempt failed and more reasoning is justified | `cavecrew-escalation-max` |
| Architecture discussion needing full prose | Main thread |
| One-line answer you already know | Main thread, no subagent |

Prefer parallel agents for independent read-heavy work. Avoid parallel write-heavy agents touching overlapping files.

## Why this exists (the real win)

Subagent tool results get injected into main context verbatim. A vanilla `Explore` that returns 2k tokens of prose costs 2k tokens of main-context budget every time. The same finding from `cavecrew-investigator` returns ~700 tokens. Across 20 delegations in one session that's the difference between context exhaustion and finishing the task.

## Output contracts

What main thread can rely on per agent:

**`cavecrew-investigator`**
```
<Header>:
- path:line — `symbol` — short note
totals: <counts>.
```
Or `No match.` Always file-path-first, line-number-attached, backticked symbols. Safe to grep with `path:\d+`.

**`cavecrew-builder`**
```
<path:line-range> - <change in at most 10 words>.
verified: <check and result>.
```
Or one of: `needs-confirm.` / `ambiguous.` / `regressed.` (terminal first token).

**`cavecrew-reviewer`**
```
path:line: <severity>: <problem>. <fix>.
totals: <critical>, <warning>, <note>, <question>
```
Or `No issues.` Findings sorted file then line ascending.

**`cavecrew-serious` and escalation agents**

Return root cause or failure cause, path-attached changes or findings, and verification result. Use normal English for auth, payments, migrations, security warnings, irreversible actions, or any compressed wording that could be misread.

## Chaining patterns

**Unclear task to fix:**
1. `cavecrew-investigator` maps relevant code with Terra Medium.
2. Main thread chooses `cavecrew-builder` for clear normal work or `cavecrew-serious` for high-consequence work.
3. `cavecrew-reviewer` audits the diff with Sol Medium.

**Failed attempt:**
1. Capture exact failure, evidence, and verification result.
2. Use `cavecrew-escalation-high` once.
3. Use `cavecrew-escalation-max` only if High fails and the task still warrants it.

**Parallel scout** (when investigation is broad):
Spawn 2-3 `cavecrew-investigator` calls in one message (different angles: defs vs callers vs tests). Aggregate in main thread.

**Single-shot edit** (when site is already known):
Skip investigator. Hand exact path:line to `cavecrew-builder` directly.

## What NOT to do

- Don't retry the same failed agent three times without changing evidence or approach.
- Don't route an unclear task directly to the builder.
- Don't send ordinary scoped work to Sol merely because Sol is available.
- Don't use Max before High unless the user explicitly requests Max.
- Don't use Ultra automatically.
- Don't expect prose. Cavecrew output is structured; paraphrase it for human-facing explanations.

## Auto-clarity (inherited)

Subagents drop caveman → normal English for security warnings, irreversible-action confirmations, and any output where fragment ambiguity could be misread. Resume caveman after.
