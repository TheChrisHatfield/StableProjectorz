<!--
meta:title: Learning Loop — cartridge micro brief
meta:hook_id: learning.loop
meta:search: adaptive learning loop micro brief
meta:purpose: Cartridge micro layer for learning-loop operations (not behavioral Spec)
meta:audience: Coding agents
meta:usage: Load after planning.rosetta when running or auditing learning loops
meta:format: semantic-markdown-with-hook-tags
meta:status: active
meta:scope: smart-value-paint | operational
meta:priority: high
meta:anchor_version: v1
-->

# Feature Micro Brief: learning-loop

**Hook:** `learning.loop`

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

1. [`docs/planning-rosetta-stone.md`](../../docs/planning-rosetta-stone.md) — `planning.rosetta`
2. Canonical runbook — [`context-library/sources/imported/learning-loop-rosetta.md`](../../context-library/sources/imported/learning-loop-rosetta.md)
3. Active feature Spec Kit — `spec.flow`: [`docs/specs/smart-value-paint/`](../../docs/specs/smart-value-paint/)
4. State — [`.hive/continual-learning/learning-loop-state.json`](../../.hive/continual-learning/learning-loop-state.json)

## [HOOK:learning.loop]

**HOOK_ID:** `learning.loop`

**Meaning:** Operational loop protocol for cartridge/spec drift and post-gate coding.

**Anchor summary:**
- Assess need score → pick type/beacon/cycles → execute loop law → promote → close
- Never store methodology in `AGENTS.md` (`integration.cl_spec`)

### [CTX:learning.loop.scope]

**HOOK_ID:** `learning.loop`  
**Member type:** micro  
**Promote to:** `context-library/sources/imported/learning-loop-rosetta.md`

| In scope | Out of scope |
|----------|--------------|
| Tagging, need scoring, promote ledgers | Replacing Spec Kit ACs |
| Cartridge ↔ Spec drift mines | Blind meta_all 200-cycle runs after gates closed |
| `implementation_gap` before MLP wiring | Writing product behavior into AGENTS.md |

## Active feature default

**smart-value-paint** — Tasks 1–4 complete. Prefer:

- `mine_apply` when Rosetta/cartridge/source4s markers drift (PDFs, correlation gaps)
- `implementation_gap` when coding T5+ (after user confirms backlog + MLP asset)
- Confirm-only on locked laws in prior SVP logs

## Canonical source4s (literature)

| Doc | Role |
|-----|------|
| `PAINT_Transformer.pdf` | Stroke-set / self-training baseline |
| `ADAPTIVE_ROUTING.pdf` | Expert-router analogy only (Decimacon OOS) |

Correlation: [`cartridge/mappings/source-correlation.json`](../mappings/source-correlation.json)

## Validation

```powershell
py -3.11 -m hive_planner ci-check
py -3.11 -m hive_planner spec-drift-check
```
