# cavecrew

Decision guide. When to delegate to caveman subagents instead of doing the work inline.

## What it does

Tells the main thread when to spawn a caveman-style subagent versus the vanilla equivalent. The win: subagent tool-results inject back into main context verbatim, and caveman output is roughly 1/3 the size of vanilla prose. Across 20 delegations in one session, that is the difference between context exhaustion and finishing the task.

Codex agents:

| Agent | Job | Model / effort |
|---|---|---|
| `cavecrew-builder` | Normal bug fix or clearly scoped feature | Luna XHigh |
| `cavecrew-investigator` | Broad repository exploration | Terra Medium |
| `cavecrew-serious` | Complex or high-consequence work | Sol Medium |
| `cavecrew-reviewer` | Correctness and risk review | Sol Medium |
| `cavecrew-escalation-high` | Failed Medium attempt | Sol High |
| `cavecrew-escalation-max` | Failed High attempt | Sol Max |

Sol Ultra has no preset and is never selected automatically.

This skill is a decision guide, not a slash command. Invoke it with `$cavecrew` in Codex or by mentioning delegation.

## How to invoke

Triggers on phrases like "delegate to subagent", "use cavecrew", "spawn investigator", "save context", "compressed agent output".

## Example chaining

Unclear task → fix → verify:

1. Terra Medium `cavecrew-investigator` maps relevant code.
2. Luna XHigh `cavecrew-builder` handles clear normal work, or Sol Medium `cavecrew-serious` handles high-consequence work.
3. Sol Medium `cavecrew-reviewer` audits the resulting diff.

Parallel scout: spawn 2-3 `cavecrew-investigator` calls in one message with different angles (defs, callers, tests). Aggregate in main.

After a concrete Medium failure, use `cavecrew-escalation-high`. Use `cavecrew-escalation-max` only if High also fails and more reasoning is justified.

## Project model routing

This project defines runtime-native Cavecrew agents under `.opencode/agents/` for OpenCode and `.codex/agents/` for Codex:

### Codex

| Agent | Model / effort |
|---|---|
| `cavecrew-builder` | Luna XHigh |
| `cavecrew-investigator` | Terra Medium |
| `cavecrew-serious` | Sol Medium |
| `cavecrew-reviewer` | Sol Medium |
| `cavecrew-escalation-high` | Sol High |
| `cavecrew-escalation-max` | Sol Max |

Codex agents use the built-in OpenAI provider so native dispatch does not pass through Ollama's incompatible Responses endpoint.

### OpenCode

OpenCode's working Ollama routes remain unchanged:

| Agent | Model |
|---|---|
| `cavecrew-investigator` | `ollama/minimax-m3:cloud` |
| `cavecrew-builder` | `ollama/kimi-k2.7-code:cloud` |
| `cavecrew-reviewer` | `ollama/glm-5.2:cloud` |

Escalate only after a concrete failure: unresolved root cause, insufficient evidence, failed verification, or regression. Use Sol High before Sol Max. Never choose Sol Ultra automatically.

## See also

- [`SKILL.md`](./SKILL.md) — full decision matrix and output contracts
- [Codex Cavecrew agents](../../../.codex/agents/)
- [OpenCode Cavecrew agents](../../../.opencode/agents/)
- [Caveman README](../../README.md) — repository overview
