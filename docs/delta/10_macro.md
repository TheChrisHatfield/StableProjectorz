<!-- PROMOTED: multipass meta learning-loop 2026-07-15 — review before handoff -->

# Macro — StableProjectorz

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

Load [`docs/planning-rosetta-stone.md`](../planning-rosetta-stone.md) (`planning.rosetta`) before using this Delta layer.

- **Hook:** `context.delta`
- **Unlocks:** micro briefs and `spec.flow`
- **Next:** [`20_micro/smart-value-paint.md`](./20_micro/smart-value-paint.md)

## Architecture

| Module area | Role for smart-value-paint | Decimacon / literature map |
|-------------|----------------------------|----------------------------|
| Paint stack (`Assets/_gm/Features/Paint/`) | Stroke apply, layers, brush UI — proposal sink | Execution plane (MASTER-like commit) |
| Inpaint / mask painter | Live UV paint targets and mode routing | Execution plane |
| `SmartValuePaint/` assist | Propose bins/params via `IValuePaintAssist` | Fast MLP / routing-head analogue (Repo/Tonal cortex lite) |
| SD / Forge hub | Optional targets / value maps later | Dataset / variation (not runtime Decimacon) |
| Spec Kit (`docs/specs/`) | Behavioral requirements | Truth plane |
| Hive cartridge | Research ingest → emit → promote | Tertiary plane (`compiler.pipeline`) |
| LAVD / Adaptive Routing (literature) | Resource/expert-router ideas | Sensor + allocator only — not paint reasoner |

## Multipass planes (combined)

| Pass | Sources | Macro takeaway |
|------|---------|----------------|
| A — Paint assist | SMART_VALUE_PAINT_DEV_1, Paint Transformer | Decision heads + optional later stroke-set; feed existing UV sink |
| B — Resource router | ADAPTIVE_ROUTING, LAVD_* | Learned router / Thompson sampling over compute — keep separate from paint policy |
| C — Decimacon family | MLP_DECIMACON_DEV_1, EXTRA, ORIENT | Staged hybrid + shared latent + selective attention — long-term family; v1 out of scope |

**CONVERGE:** All passes agree proposals must not fork a parallel painter.  
**STACK:** Decimacon explains *future* decomposition (Base/Router/Gate/Cortex); SVP ships the smallest useful cortex head first.  
**CONFLICT (resolved):** Runtime Decimacon/MoS before MLP weights → drop (locked OOS).

## Workflows

1. **Research** → context-library ingest → `hive_planner emit` / `source4s --force`
2. **Learning loop** → assess → mine → promote (`learning.loop`) when sources/cartridge drift
3. **Delta** → holistic / macro / micro alignment (`context.delta`)
4. **Spec Kit** → `spec.md` → `plan.md` → `tasks.md` (`spec.flow`)
5. **Implement** one task at a time + integration wiring audit
6. **CL** → operational bullets only via `agents-propose` (`integration.cl_spec`)

## Dependencies

| Component | Role |
|-----------|------|
| Spec Kit | Behavioral spec/plan/tasks |
| Cursor | Implementation + `.cursor/rules/` |
| Hive CLI | `emit`, `source4s`, `cartridge-promote`, `ci-check`, impact |
| Existing paint engine | Execution surface; do not fork a parallel painter |
| MLP Decimacon docs | Orientation / future family — not a Unity package in v1 |
