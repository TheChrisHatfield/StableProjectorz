<!--
meta:title: Planning Rosetta Stone — StableProjectorz
meta:hook_id: planning.rosetta
meta:search: Planning Rosetta Stone
meta:purpose: Legend and navigation key for hooks, Delta, Spec Kit, and learning loops
meta:audience: Agentic coding agents, human maintainers
meta:usage: Search for Planning Rosetta Stone or hook planning.rosetta before planning or coding
meta:format: semantic-markdown-with-hook-tags
meta:status: active
meta:scope: repo-wide
meta:priority: critical
meta:anchor_version: v1
-->

# Planning Rosetta Stone — StableProjectorz

Search phrase: **Planning Rosetta Stone** · Root hook: `planning.rosetta`

## Purpose

Legend mapping concepts → files. Coding agents load this first.

## How to use

1. Find the concept or hook below.
2. Open PRIMARY FILES.
3. Follow UNLOCKS only when deeper detail is needed.

## Legend conventions

- **HOOK_ID** — stable anchor (`domain.name`)
- **PRIMARY FILES** — first files to open
- **UNLOCKS** — related hooks next
- **THREAD COVERAGE** — what this hook summarizes

***

## [HOOK:planning.rosetta]

**HOOK_ID:** `planning.rosetta`

**Meaning:** Top-level legend for this project.

**PRIMARY FILES:**
- [`START_HERE.md`](../START_HERE.md)
- [`AGENTS.md`](../AGENTS.md)
- This file

**UNLOCKS:**
- `context.delta`
- `spec.flow`
- `change.impact`
- `learning.loop`
- `integration.cl_spec`

**THREAD COVERAGE:** Full project navigation including Spec Kit + adaptive learning loops.

***

## [HOOK:context.delta]

**HOOK_ID:** `context.delta`

**Meaning:** Holistic / macro / micro documentation layers.

**PRIMARY FILES:**
- [`delta/00_holistic.md`](delta/00_holistic.md)
- [`delta/10_macro.md`](delta/10_macro.md)
- [`delta/20_micro/smart-value-paint.md`](delta/20_micro/smart-value-paint.md)

**UNLOCKS:**
- `spec.flow`
- `compiler.pipeline`

**THREAD COVERAGE:** Mission, architecture, feature briefs.

***

## [HOOK:spec.flow]

**HOOK_ID:** `spec.flow`

**Meaning:** Spec Kit behavioral trio for the active feature.

**PRIMARY FILES:**
- [`specs/smart-value-paint/spec.md`](specs/smart-value-paint/spec.md)
- [`specs/smart-value-paint/plan.md`](specs/smart-value-paint/plan.md)
- [`specs/smart-value-paint/tasks.md`](specs/smart-value-paint/tasks.md)

**UNLOCKS:**
- `change.impact`
- `integration.cl_spec`
- `integration.wiring_audit` (via `.cursor/rules/integration-wiring-audit.mdc`)

**THREAD COVERAGE:** Requirements, discovery map, Tasks 1–4 (complete); T5–T8 backlog in cartridge-insights only.

***

## [HOOK:cursor.rules]

**HOOK_ID:** `cursor.rules`

**Meaning:** Agent behavior rules under `.cursor/rules/`.

**PRIMARY FILES:**
- `.cursor/rules/*.mdc`
- Especially `integration-wiring-audit.mdc`, `build-stability.mdc`, `cl-spec-integration.mdc`

**UNLOCKS:**
- `change.impact`
- `integration.cl_spec`

**THREAD COVERAGE:** Runtime UI, wiring audit, validation gates.

***

## [HOOK:change.impact]

**HOOK_ID:** `change.impact`

**Meaning:** Impact packets before non-trivial edits.

**PRIMARY FILES:**
- [`change-impact/policy.md`](change-impact/policy.md)
- `change-impact/impact-packets/`

**UNLOCKS:**
- `change.bugfix_mode`
- `change.validation`

**THREAD COVERAGE:** Pre-edit risk for `_gm` paint/SD paths.

***

## [HOOK:change.bugfix_mode]

**HOOK_ID:** `change.bugfix_mode`

**Meaning:** Causal-chain impact mode for defects.

**PRIMARY FILES:**
- [`change-impact/policy.md`](change-impact/policy.md)

**UNLOCKS:**
- `change.validation`

**THREAD COVERAGE:** Bug-fix packets vs feature work packets.

***

## [HOOK:integration.cl_spec]

**HOOK_ID:** `integration.cl_spec`

**Meaning:** Spec Kit (behavioral) + Continual Learning (operational) truth split.

**PRIMARY FILES:**
- [`cl-spec-integration.md`](cl-spec-integration.md)
- [`../spec-kit-agent-integration.md`](../spec-kit-agent-integration.md)
- [`AGENTS.md`](../AGENTS.md)

**UNLOCKS:**
- `memory.agents_md`
- `learning.loop`

**THREAD COVERAGE:** What may enter AGENTS.md vs spec.md.

***

## [HOOK:compiler.pipeline]

**HOOK_ID:** `compiler.pipeline`

**Meaning:** Hive cartridge ingest → emit → promote pipeline.

**PRIMARY FILES:**
- `cartridge/manifest.json`
- `cartridge/source-context.md`
- `context-library/sources/imported/`

**UNLOCKS:**
- `context.document_sourcing`
- `context.delta`
- `spec.flow`

**THREAD COVERAGE:** Tertiary research → Delta/Spec promotion.

***

## [HOOK:context.document_sourcing]

**HOOK_ID:** `context.document_sourcing`

**Meaning:** Indexed sources feeding cartridge retrieval.

**PRIMARY FILES:**
- `context-library/sources/imported/SMART_VALUE_PAINT_DEV_1.md`
- `context-library/sources/imported/learning-loop-rosetta.md`
- `context-library/sources/source4s/PAINT_Transformer.pdf`
- `context-library/sources/source4s/ADAPTIVE_ROUTING.pdf`
- [`mappings` via](../cartridge/mappings/source-correlation.json) `cartridge/mappings/source-correlation.json`

**UNLOCKS:**
- `compiler.pipeline`
- `learning.mine`

**THREAD COVERAGE:** Research dumps and operational loop cartridge.

***

## [HOOK:learning.loop]

**HOOK_ID:** `learning.loop`

**Meaning:** Adaptive learning-loop operational Rosetta (assess → execute → promote → close).

**PRIMARY FILES:**
- [`../context-library/sources/imported/learning-loop-rosetta.md`](../context-library/sources/imported/learning-loop-rosetta.md)
- [`../cartridge/micro/learning-loop.md`](../cartridge/micro/learning-loop.md)
- [`.hive/continual-learning/learning-loop-state.json`](../.hive/continual-learning/learning-loop-state.json)

**UNLOCKS:**
- `learning.assess`
- `learning.beacon`
- `learning.promote`
- `learning.close`
- `planning.rosetta`

**THREAD COVERAGE:** Loop methodology for smart-value-paint; not stored in AGENTS.md.

***

## [HOOK:learning.assess]

**HOOK_ID:** `learning.assess`

**Meaning:** Need score and skip/run decision.

**PRIMARY FILES:**
- Learning Loop Rosetta § `learning.assess`
- `docs/specs/smart-value-paint/tasks.md`

**UNLOCKS:**
- `learning.beacon`

**THREAD COVERAGE:** When to run beacon / mine_apply / implementation_gap.

***

## [HOOK:learning.beacon]

**HOOK_ID:** `learning.beacon`

**Meaning:** Primary source for cycle themes.

**PRIMARY FILES:**
- Learning Loop Rosetta beacon table
- `docs/specs/smart-value-paint/cartridge-insights.md`

**UNLOCKS:**
- `learning.mine`
- `learning.cycle`

**THREAD COVERAGE:** SVP research vs paint-stack beacons.

***

## [HOOK:learning.promote]

**HOOK_ID:** `learning.promote`

**Meaning:** Outcome taxonomy → Spec Kit / Rosetta / cartridge patches.

**PRIMARY FILES:**
- Learning Loop Rosetta promote targets
- `docs/proposals/learning-loop-*.md`

**UNLOCKS:**
- `spec.flow`
- `learning.close`

**THREAD COVERAGE:** CONFIRM / PROMOTE / BACKLOG / FIX / CONFLICT / META-ONLY.

***

## [HOOK:learning.close]

**HOOK_ID:** `learning.close`

**Meaning:** Log + state + `ci-check` handoff.

**PRIMARY FILES:**
- `.hive/continual-learning/learning-loop-state.json`
- `docs/proposals/learning-loop-*.md`

**UNLOCKS:**
- `change.validation`
- `workflow.git_handoff`

**THREAD COVERAGE:** Mandatory close for every run loop.

***

## Slim hook table (bootstrap keep)

| HOOK_ID | Meaning |
|---------|---------|
| `planning.rosetta` | This legend |
| `context.delta` | Holistic / macro / micro docs |
| `cursor.rules` | `.cursor/rules/*.mdc` |
| `change.impact` | Impact packets before edits |
| `change.bugfix_mode` | Bugfix impact mode |
| `spec.flow` | Spec Kit spec / plan / tasks |
| `integration.cl_spec` | Spec Kit (behavioral) + Continual Learning (operational) |
| `compiler.pipeline` | Cartridge emit / promote |
| `context.document_sourcing` | Indexed tertiary sources |
| `learning.loop` | Adaptive learning-loop Rosetta |

## Primary files (quick)

- [`START_HERE.md`](../START_HERE.md)
- [`AGENTS.md`](../AGENTS.md)
- [`delta/00_holistic.md`](delta/00_holistic.md)
- [`delta/10_macro.md`](delta/10_macro.md)
- [`change-impact/policy.md`](change-impact/policy.md)
- [`specs/smart-value-paint/spec.md`](specs/smart-value-paint/spec.md)
- [`spec-kit-agent-integration.md`](../spec-kit-agent-integration.md)
- [`cl-spec-integration.md`](cl-spec-integration.md)
- [`../context-library/sources/imported/learning-loop-rosetta.md`](../context-library/sources/imported/learning-loop-rosetta.md)

## Coverage check

- [x] Planning legend → `planning.rosetta`
- [x] Delta layers → `context.delta`
- [x] Spec Kit trio → `spec.flow`
- [x] Impact packets → `change.impact`
- [x] Spec/CL split → `integration.cl_spec`
- [x] Cartridge pipeline → `compiler.pipeline`
- [x] Learning loops → `learning.loop` (+ assess / beacon / promote / close)
- [x] Source ingest → `context.document_sourcing`
