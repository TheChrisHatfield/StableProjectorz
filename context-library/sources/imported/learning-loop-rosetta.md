<!--
meta:title: Learning Loop Rosetta Stone — Agent Operations
meta:hook_id: learning.loop
meta:search: Learning Loop Rosetta Stone
meta:purpose: Operational legend for adaptive learning loops on StableProjectorz cartridges
meta:audience: Coding agents, Hive handoff agents
meta:usage: Load before any learning loop; pair with docs/planning-rosetta-stone.md
meta:format: semantic-markdown-with-hook-tags
meta:status: active
meta:scope: smart-value-paint | repo-wide operational
meta:priority: high
meta:anchor_version: v1
meta:methodology: ROSETTASTONEMETHODOLOGY
-->

# Learning Loop Rosetta Stone — Agent Operations

**Hook:** `learning.loop` · **Search phrase:** Learning Loop Rosetta Stone  
**Feature scope:** Active feature `smart-value-paint` (Tasks 1–4 complete; MLP asset pending)  
**Methodology anchor:** `ROSETTASTONEMETHODOLOGY`

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

Load this cartridge **before** running a learning loop. Pair with:

| Artifact | Role | Hook |
|----------|------|------|
| [`docs/planning-rosetta-stone.md`](../../../docs/planning-rosetta-stone.md) | Feature METAANCHORs, source hooks, prior loop index | `planning.rosetta` |
| [`docs/specs/smart-value-paint/cartridge-insights.md`](../../../docs/specs/smart-value-paint/cartridge-insights.md) | Tertiary distillate from SMART_VALUE_PAINT_DEV_1 | `compiler.pipeline` |
| [`cartridge/mappings/source-correlation.json`](../../../cartridge/mappings/source-correlation.json) | Canonical sources + gaps | `context.document_sourcing` |
| [`.hive/continual-learning/learning-loop-state.json`](../../../.hive/continual-learning/learning-loop-state.json) | Prior loops, gate status, recommended next | `learning.close` |
| [`.cursor/plugins/hive-continual-learning/WORKFLOW.md`](../../../.cursor/plugins/hive-continual-learning/WORKFLOW.md) | CL memory promotion gates | `integration.cl_spec` |

**Do not** put loop methodology in `AGENTS.md` — it lives here and in session logs under `docs/proposals/learning-loop-*.md` (`integration.cl_spec`).

***

## [HOOK:learning.loop]

**HOOK_ID:** `learning.loop`

**Meaning:** Operational Rosetta — how to assess, run, promote, and close adaptive learning loops.

**Anchor summary:**
- Loop law: insight → METAANCHOR → source → cartridge → gap → outcome
- Truth split: behavioral in Spec Kit; operational here
- Active feature beacon defaults to `smart-value-paint`

**Inline refs:** unlocks `learning.assess`, `learning.beacon`, `learning.promote`, `learning.close`; relates to `planning.rosetta`, `integration.cl_spec`

***

## [HOOK:learning.assess]

**HOOK_ID:** `learning.assess`

**Meaning:** Context load + need score (0–12) → skip vs run.

**Anchor summary:**
- Read tasks, planning Rosetta, source-correlation, prior logs, state
- Score ≥ 3 → run adapted loop; &lt; 3 → state why skipped

**Inline refs:** unlocks `learning.beacon`; uses `spec.flow`

### [CTX:learning.assess.need_score]

**HOOK_ID:** `learning.assess`  
**Member type:** workflow  
**Promote to:** `.hive/continual-learning/learning-loop-state.json`

| Signal | Points (guide) |
|--------|----------------|
| Open planning gates in `tasks.md` | +3 |
| User says loop / mine / beacon / Rosetta on cartridge | +4 |
| Task 4+ complete; implementation / MLP transition | +2 |
| Cartridge ↔ Spec drift after ingest/emit | +3 |
| Last loop left `recommended_next` non-idle | +2 |

| Score | Action |
|-------|--------|
| ≥ 3 | Run adapted loop |
| &lt; 3 | State why skipped; proceed with implementation unless user insists |

***

## [HOOK:learning.beacon]

**HOOK_ID:** `learning.beacon`

**Meaning:** One primary source or planning surface that seeds cycle themes.

**Anchor summary:**
- Prefer SMART_VALUE_PAINT_DEV_1 + cartridge-insights for research gaps
- Prefer contracts under `Assets/_gm/.../SmartValuePaint/` for implementation_gap
- Prefer this file itself for mine_apply on learning-loop drift

**Inline refs:** unlocks `learning.mine`; relates to `compiler.pipeline`, `context.document_sourcing`

### [CTX:learning.beacon.svp_table]

**HOOK_ID:** `learning.beacon`  
**Member type:** micro  
**Promote to:** `cartridge/micro/learning-loop.md`

| Context | Beacon |
|---------|--------|
| Cartridge / Rosetta drift | This file + `planning.rosetta` |
| Research vs shipped (T5–T8 drafts) | `SMART_VALUE_PAINT_DEV_1` + `cartridge-insights.md` |
| Post Task 4 code | `IValuePaintAssist` / `ValuePaintProposalApplier` vs scaffold |
| Edge softness gap | Spec R2 + `BrushRibbon_UI_Hardness` |
| Multiple closed gates | Triangulate prior logs — do not re-mine primary beacons |

***

## [HOOK:learning.revalidate]

**HOOK_ID:** `learning.revalidate`

**Meaning:** Batch checkpoint — score novelty, drop duplicates, lock promotion batches.

**Anchor summary:**
- Every 10 cycles when total ≤ 50; every 20 when total &gt; 50
- Every 5 cycles for `mine_apply`

**Inline refs:** unlocks `learning.promote`

***

## [HOOK:learning.mine]

**HOOK_ID:** `learning.mine`

**Meaning:** Intermittent Rosetta anchor → source grep/read for **new** patterns only.

**Anchor summary:**
- Max 4 mine gates per loop
- Stop if 3 consecutive cycles are CONFIRM-only

**Inline refs:** uses `planning.rosetta`, `meta.anchor_policy`

***

## [HOOK:learning.promote]

**HOOK_ID:** `learning.promote`

**Meaning:** Apply deltas with outcome taxonomy.

**Anchor summary:**
- CONFIRM · PROMOTE · BACKLOG · FIX · CONFLICT · META-ONLY
- Precedence: measured outcomes &gt; behavioral AC &gt; reward formulas &gt; naming &gt; synthetic narrative

**Inline refs:** unlocks `spec.flow`, `context.delta`, `planning.rosetta`

### [CTX:learning.promote.targets]

**HOOK_ID:** `learning.promote`  
**Member type:** integration  
**Promote to:** `docs/proposals/learning-loop-*.md`

| Target | When |
|--------|------|
| `docs/specs/smart-value-paint/spec.md`, `plan.md`, `tasks.md` | PROMOTE behavioral gaps |
| `docs/planning-rosetta-stone.md` | New METAANCHORs / learning hooks |
| `cartridge/research-summary.md`, `cartridge/micro/learning-loop.md` | Cross-source synthesis |
| `cartridge/mappings/source-correlation.json` | New source links |
| `docs/delta/00_holistic.md` | META-ONLY integration laws |
| `.hive/continual-learning/learning-loop-state.json` | prior_loops, gates, recommended_next |

***

## [HOOK:learning.close]

**HOOK_ID:** `learning.close`

**Meaning:** Mandatory handoff — log + state + `ci-check`.

**Anchor summary:**
- Write `docs/proposals/learning-loop-<type>-<topic>-<YYYYMMDD>.md`
- Update `learning-loop-state.json`
- Run `py -3.11 -m hive_planner ci-check`

**Inline refs:** relates to `workflow.git_handoff`, `change.validation`

***

## METAANCHORs (loop methodology)

Resolve before inventing loop steps (`meta.anchor_policy`).

| METAANCHOR | Meaning | Planning target |
|------------|---------|-----------------|
| `ROSETTASTONEMETHODOLOGY` | Anchor-first retrieval, decompression, continuity | `docs/planning-rosetta-stone.md`, this file |
| `LEARNINGLOOPASSESS` | Load tasks/rosetta/correlation/logs/state; need score | this file § `learning.assess` |
| `LEARNINGLOOPBEACON` | One primary source or planning doc | beacon table above |
| `LEARNINGLOOPCYCLE` | Single insight traversal of loop law | per-cycle ledger row |
| `LEARNINGLOOPREVALIDATE` | Novelty checkpoint; lock promotion batch | revalidate table in log |
| `LEARNINGLOOPMINE` | Pattern → METAANCHOR → canonical source | max 4 gates per loop |
| `LEARNINGLOOPPROMOTE` | CONFIRM / PROMOTE / BACKLOG / FIX / CONFLICT / META-ONLY | Spec Kit + cartridge |
| `LEARNINGLOOPCLOSE` | Log + state + cartridge sync + ci-check | handoff complete |
| `LEARNINGLOOPTRIANGULATE` | CONVERGE / STACK / CONFLICT / META-ONLY | meta / meta_all |

***

## [HOOK:learning.cycle]

**HOOK_ID:** `learning.cycle`

**Meaning:** Loop law for every cycle.

```text
Insight → Rosetta METAANCHOR → source corroboration → cartridge check
  → spec/plan/tasks gap → CONFIRM | PROMOTE | BACKLOG | FIX | CONFLICT | META-ONLY
```

**Precedence on conflicts:**

```text
measured outcomes > behavioral AC > reward formulas > module naming > synthetic narrative
```

**Inline refs:** uses `learning.promote`, `planning.rosetta`

***

## Loop types (adapt before execute)

| Type | When | Cycles | Revalidate every |
|------|------|--------|------------------|
| `beacon` | Single open gate, one beacon | 50 (±10) | 10 |
| `meta` | 2–3 prior loops, integration question | 50 (±20) | 10 |
| `meta_all` | 4+ loops + cartridge + sources | 100–200 | 20 |
| `implementation_gap` | Task 4+ coding; planning closed | 20–40 | 10 |
| `mine_apply` | Cartridge/source/Rosetta drift only | 15–25 | 5 |

**Do not** invent `meta_all` when Tasks 1–4 are closed and the question is cartridge tagging — prefer `mine_apply` or `implementation_gap`.

***

## Outcome taxonomy

| Label | Action |
|-------|--------|
| CONFIRM | Already planned — no edit |
| PROMOTE | Patch spec/plan/tasks/rosetta/cartridge |
| BACKLOG | Draft only (e.g. T5–T8) until user confirms |
| FIX | Cartridge or drift correction |
| CONFLICT | Resolve with precedence |
| META-ONLY | Integration law → holistic + Spec open Q |

***

## [HOOK:integration.cl_spec]

**HOOK_ID:** `integration.cl_spec`

**Meaning:** Truth division for learning-loop content.

| Layer | Owner | Learning loop content |
|-------|-------|----------------------|
| Behavioral | Spec Kit `docs/specs/smart-value-paint/` | Requirements, ACs, contracts |
| Operational | This cartridge + proposals | Loop protocol, commands, handoff |
| Session evidence | `docs/proposals/learning-loop-*.md` | Cycle ledgers, promote sets |
| Durable ops | `AGENTS.md` | Commands/paths only — **not** loop methodology |

***

## Quick commands

```powershell
py -3.11 -m hive_planner ci-check
py -3.11 -m hive_planner spec-drift-check
py -3.11 -m hive_planner emit --feature smart-value-paint
py -3.11 -m hive_planner agents-propose --candidates docs/proposals/candidates.md
```

***

## Prior loops (this repo)

| Log | Type | Gate unlocked |
|-----|------|---------------|
| *(mlp-decimacon prior logs — foreign — do not re-debate)* | — | Ignored for StableProjectorz |
| `learning-loop-mine-apply-learning-loop-cartridge-20260715.md` | `mine_apply` | Rosetta-tagged learning.loop cartridge + planning index |
| `learning-loop-mine-apply-source4s-20260715.md` | `mine_apply` | source4s PT + Adaptive Routing correlated; MoS runtime CONFLICT dropped |
| `learning-loop-meta-multipass-decimacon-deltas-20260715.md` | `meta` | Multipass A/B/C combined Delta; Decimacon orientation OOS runtime |

**Locked laws (smart-value-paint):** tasks 1–4 sink path · deterministic stub behind `IValuePaintAssist` · accept via ribbon → `Apply_into_ColorBrushTex` · Decimacon/SDXL training out of scope v1 · Paint Transformer literature-only · Adaptive Routing analogy-not-runtime · **Decimacon runtime OOS** · scheduler≠paint reasoner · T5–T8 backlog until user confirm.

***

## Related cartridge layers

| Layer | File |
|-------|------|
| Micro brief | [`cartridge/micro/learning-loop.md`](../../../cartridge/micro/learning-loop.md) |
| Feature context | [`cartridge/micro/smart-value-paint.md`](../../../cartridge/micro/smart-value-paint.md) |
| Source correlation | [`cartridge/mappings/source-correlation.json`](../../../cartridge/mappings/source-correlation.json) |
| Research synthesis | [`cartridge/research-summary.md`](../../../cartridge/research-summary.md) |
| Tertiary insights | [`docs/specs/smart-value-paint/cartridge-insights.md`](../../../docs/specs/smart-value-paint/cartridge-insights.md) |
