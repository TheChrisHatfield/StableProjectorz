<!--
meta:title: Learning Loop Log — mine_apply source4s PDFs + cartridge
meta:hook_id: learning.close
meta:search: learning loop mine_apply Paint Transformer Adaptive Routing
meta:purpose: Session evidence for forcing source4s into cartridge and correlating literature
meta:audience: Coding agents, reviewers
meta:usage: Confirm-only on locked promotions; do not re-debate Decimacon runtime
meta:format: semantic-markdown-with-hook-tags
meta:status: active
meta:scope: smart-value-paint
meta:priority: normal
meta:anchor_version: v1
-->

# Learning Loop Log — mine_apply source4s + cartridge

**Feature:** `smart-value-paint`  
**Type:** `mine_apply` · **Cycles:** 20 (condensed) · **Revalidate every:** 5  
**Beacon:** `source4s/PAINT_Transformer.pdf` + `source4s/ADAPTIVE_ROUTING.pdf` (primary); corroborate with SMART_VALUE_PAINT_DEV_1 + learning-loop-rosetta  
**Date:** 2026-07-15  
**Need score:** 9 — user asked learning loop with sources/cartridges (+4); new PDFs indexed but absent from emit (+3); Task 4+ MLP transition (+2)

## Loop law

```text
Insight → Rosetta METAANCHOR → source corroboration → cartridge check
  → spec/plan/tasks gap → CONFIRM | PROMOTE | BACKLOG | FIX | CONFLICT | META-ONLY
```

## Revalidation

| Checkpoint | Cycles | Action |
|------------|--------|--------|
| R1 | 5 | Lock: PDFs must land in `source-context` via `source4s --force` |
| R2 | 10 | Lock: Paint Transformer = literature; do not swap DTO for set-prediction params |
| R3 | 15 | Lock: Adaptive Routing = analogy only; Decimacon OOS remains |
| R4 | 20 | Close this log + state + ci-check |

## Cycle batches (condensed)

### Cycles 1–5 — Assess + ingest drift

| # | Insight | METAANCHOR | Outcome |
|---|---------|------------|---------|
| 1 | Tasks 1–4 closed; active = smart-value-paint | LEARNINGLOOPASSESS | CONFIRM |
| 2 | 4 docs / 241 chunks; PDFs missing from prior emit | LEARNINGLOOPMINE | FIX (`source4s --force`) |
| 3 | Force emit now lists AR + PT + SMART_VALUE | LEARNINGLOOPBEACON | CONFIRM |
| 4 | `source-correlation.json` lacked PDF entries | LEARNINGLOOPPROMOTE | FIX |
| 5 | Prior recommended_next was idle implementation_gap | LEARNINGLOOPASSESS | CONFIRM (user override → mine_apply) |

### Cycles 6–10 — Paint Transformer

| # | Insight | METAANCHOR | Outcome |
|---|---------|------------|---------|
| 6 | Stroke-set prediction / feed-forward > RL | LEARNINGLOOPCYCLE | CONFIRM (vs SMART_VALUE thread) |
| 7 | Self-training without off-the-shelf dataset | LEARNINGLOOPPROMOTE | BACKLOG (T8 only) |
| 8 | Do not replace `ValuePaintProposal` with stroke-set tensor API for T5 | LEARNINGLOOPPROMOTE | CONFIRM (lock) |
| 9 | Insights doc lacked PDF sections | LEARNINGLOOPPROMOTE | PROMOTE |
| 10 | planning.rosetta sourcing list missing source4s | ROSETTASTONEMETHODOLOGY | PROMOTE |

### Cycles 11–15 — Adaptive Routing

| # | Insight | METAANCHOR | Outcome |
|---|---------|------------|---------|
| 11 | Learned router over expert policies | LEARNINGLOOPMINE | META-ONLY (analogy) |
| 12 | Narrative wanting MoS runtime before MLP | LEARNINGLOOPTRIANGULATE | CONFLICT → drop (locked Decimacon OOS) |
| 13 | Correlation gap `expert-router-runtime` = out_of_scope_v1 | LEARNINGLOOPPROMOTE | FIX |
| 14 | learning-loop micro missing source4s table | LEARNINGLOOPPROMOTE | PROMOTE |
| 15 | Stop mine gates (no new Spec AC) | LEARNINGLOOPMINE | CONFIRM |

### Cycles 16–20 — Close

| # | Insight | METAANCHOR | Outcome |
|---|---------|------------|---------|
| 16 | T5–T8 stay backlog until user + MLP | LEARNINGLOOPPROMOTE | BACKLOG |
| 17 | Edge softness → hardness still T7 | LEARNINGLOOPCYCLE | BACKLOG |
| 18 | No AGENTS.md methodology edits | integration.cl_spec | CONFIRM |
| 19 | recommended_next returns to implementation_gap on MLP | LEARNINGLOOPCLOSE | PROMOTE (state) |
| 20 | Write log + ci-check | LEARNINGLOOPCLOSE | FIX/CLOSE |

## Scoreboard

| Label | Count |
|-------|-------|
| CONFIRM | 8 |
| PROMOTE | 4 |
| BACKLOG | 3 |
| FIX | 4 |
| CONFLICT | 1 (resolved: drop MoS runtime) |
| META-ONLY | 1 |

## Promoted artifacts

- `cartridge/mappings/source-correlation.json` — PDF canonicals + gaps
- `docs/specs/smart-value-paint/cartridge-insights.md` — PT + AR sections
- `docs/planning-rosetta-stone.md` — source4s in `context.document_sourcing`
- `cartridge/micro/learning-loop.md` — source4s table
- `cartridge/source-context.md` / `manifest.json` — force emit
- `.hive/continual-learning/learning-loop-state.json` — this loop

## Locked laws (unchanged)

tasks 1–4 sink · deterministic stub · ribbon accept · Decimacon/SDXL train OOS v1 · T5–T8 need user confirm
