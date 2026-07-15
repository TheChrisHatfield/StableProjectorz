<!-- PROMOTED: smart-value-paint — review before handoff -->

<!-- BOOTSTRAP: hive_planner init -->

# Macro — StableProjectorz

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

Load [`docs/planning-rosetta-stone.md`](../planning-rosetta-stone.md) (`planning.rosetta`) before using this Delta layer.

- **Hook:** `context.delta`
- **Unlocks:** micro briefs and `spec.flow`
- **Next:** [`20_micro/smart-value-paint.md`](./20_micro/smart-value-paint.md)

## Architecture

| Module area | Role for smart-value-paint |
|-------------|----------------------------|
| Paint stack (`Assets/_gm/Features/Paint/`) | Stroke apply, layers, brush UI — proposal sink |
| Inpaint / mask painter | Live UV paint targets and mode routing |
| SD / Forge hub | Optional curated targets / value maps for training or guidance |
| Spec Kit (`docs/specs/`) | Behavioral requirements and acceptance |
| Hive cartridge (`emit` / `cartridge-promote`) | Draft → Delta/Spec sync from research sources |

## Workflows

1. **Research** → context-library ingest → `hive_planner emit --feature smart-value-paint`
2. **Delta** → holistic / macro / micro alignment (`context.delta`)
3. **Spec Kit** → `spec.md` → `plan.md` → `tasks.md` (`spec.flow`)
4. **Implement** one task at a time + integration wiring audit
5. **CL** → operational bullets only via `agents-propose` (`integration.cl_spec`)

## Dependencies

| Component | Role |
|-----------|------|
| Spec Kit | Behavioral spec/plan/tasks |
| Cursor | Implementation + `.cursor/rules/` |
| Hive CLI | `emit`, `cartridge-promote`, `ci-check`, impact packets |
| Existing paint engine | Execution surface; do not fork a parallel painter |
