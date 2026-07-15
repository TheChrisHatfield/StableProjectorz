<!--
meta:title: Learning Loop Log — beacon MLP dataset recipe
meta:hook_id: learning.close
meta:search: learning loop dataset recipe MLP train smart-value-paint
meta:purpose: Confirm and promote how to build (state→decision) training rows for SVP MLP
meta:audience: Coding agents, reviewers
meta:format: semantic-markdown-with-hook-tags
meta:status: active
meta:scope: smart-value-paint
meta:priority: high
meta:anchor_version: v1
-->

# Learning Loop Log — beacon MLP dataset recipe

**Feature:** `smart-value-paint`  
**Type:** `beacon` · **Cycles:** 15 condensed · **Revalidate every:** 5  
**Beacon:** SMART_VALUE_PAINT_DEV_1 (dataset sections) + Paint Transformer self-training + Decimacon ORIENT (decision-head role)  
**Cartridge:** active emit includes ORIENT + EXTRA + SMART_VALUE  
**Date:** 2026-07-15  
**Need score:** 10 — user asked confirm dataset creation knowledge + learning loop vs cartridge/sources (+4); T8 gap open (+3); prior multipass ready to STACK (+2); implementation transition (+1)

## Confirmed understanding (agent)

Train MLP on **manufactured `(state → value/stroke decision)` rows** aligned to `ValuePaintProposal`, using value-band maps + synthetic/forge/human teachers — **not** full-image generation and not Decimacon runtime.

## Cycle ledger (condensed)

| # | Insight | Outcome |
|---|---------|---------|
| 1 | Row shape = state features → R2 labels | CONFIRM |
| 2 | Manufacture dataset; don’t wait for public stroke sets | CONFIRM |
| 3 | SDXL optional as **targets**, not MLP output | CONFIRM |
| 4 | Stage value-structure then stroke-policy | CONFIRM |
| 5 | Paint Transformer self-train = in-progress canvases analogue | CONFIRM |
| 6 | Human `TryAccept` logs = golden fine-tune rows | PROMOTE (recipe) |
| 7 | JSONL schema should match DTO enums | PROMOTE |
| 8 | Medical/public stroke datasets wrong domain | CONFIRM |
| 9 | Decimacon does not change row schema; reinforces control-MLP role | CONFIRM |
| 10 | T8 harness stays BACKLOG until user confirm | BACKLOG |
| 11 | Narrow style/family first | CONFIRM |
| 12 | Feature vector: patches/hist/edges/history before CNN | CONFIRM |
| 13 | Promote `mlp-dataset-recipe.md` | PROMOTE |
| 14 | Update insights/correlation gap T8 note | FIX |
| 15 | Close + ci-check | CLOSE |

## Scoreboard

CONFIRM 10 · PROMOTE 3 · BACKLOG 1 · FIX 1

## Promoted

- `docs/specs/smart-value-paint/mlp-dataset-recipe.md`
- state + correlation gap note
