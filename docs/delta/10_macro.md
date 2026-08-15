<!-- PROMOTED: multipass meta learning-loop 2026-07-15 — review before handoff -->

# Macro — StableProjectorz

<!-- ROSETTA-NAV -->

## Agent navigation (Rosetta Stone)

Load [`docs/planning-rosetta-stone.md`](../planning-rosetta-stone.md) (`planning.rosetta`) before using this Delta layer.

- **Hook:** `context.delta`
- **Unlocks:** micro briefs and `spec.flow`
- **Next:** [`20_micro/pbr-generation.md`](./20_micro/pbr-generation.md) (active); prior [`smart-value-paint.md`](./20_micro/smart-value-paint.md)

## Architecture

| Module area | Role | Decimacon / literature map |
|-------------|------|----------------------------|
| Paint stack (`Assets/_gm/Features/Paint/`) | Stroke apply, layers, brush UI — SVP proposal sink | Execution plane |
| Inpaint / mask painter | Live UV paint targets and mode routing | Execution plane |
| `SmartValuePaint/` assist | Propose bins/params via `IValuePaintAssist` | Fast MLP / routing-head analogue |
| **ComfyUI (dataset factory)** | Fast training-data workflows — tileable / multi-map export (`:8188`) | Feeds Track B train; **not** Hub product path |
| SD / **Forge Neo** hub | Gen Art + **in-SPZ PBR generate** after trained companion (`:7860` / `--api`) | Product generator — see `forge-neo-swap` |
| GenData / `Save_MGR` | **PBR pack sink** — inventoriable channels + labeled export | Product store for `pbr-generation` |
| SPZ GO mesh stream (`spz.go.mesh_stream`) | Versioned loopback binary geometry transport to Blender; FBX remains compatibility/texture path | DCC interoperability plane |
| Spec Kit (`docs/specs/`) | Behavioral requirements | Truth plane |
| Hive cartridge | Research ingest → emit → promote | Tertiary plane (`compiler.pipeline`) |
| LAVD / Adaptive Routing (literature) | Resource/expert-router ideas | Sensor + allocator only |
| VideoNeuMat / NeuMIP (literature) | **Process GT** for training-data replication (traj, captions, novel-view supervise, bake maps) | Comfy executes GT; bake classical maps — not Unity runtime |
| ArmorLab / Materialize (interim) | Flat albedo→MR extract bridge | BACKLOG only until native Neo+mesh path |

## Multipass planes (combined)

| Pass | Sources | Macro takeaway |
|------|---------|----------------|
| A — Paint assist | SMART_VALUE_PAINT_DEV_1, Paint Transformer | Decision heads + optional later stroke-set; feed existing UV sink |
| B — Resource router | ADAPTIVE_ROUTING, LAVD_* | Learned router / Thompson sampling — keep separate from paint policy |
| C — Decimacon family | MLP_DECIMACON_DEV_1, EXTRA, ORIENT | Staged hybrid — long-term; v1 OOS for SVP/PBR runtime |
| D — PBR material | Synthesis paper, VideoNueMat.pdf, arXiv extract | Host≠generator; inventoriable MR kinds; NeuMIP intermediate only |
| D2 — PBR stages (2026-07-31) | Synthesis workflow.tools + VideoNeuMat §§3–4 + Comfy ops | Staged: dataset lab → companion train → Neo generate → SPZ pack |

**CONVERGE:** No parallel painter; no parallel PBR texture store outside GenData.  
**STACK:** Decimacon / VideoNeuMat explain *future* quality/reconstruction heads; ship pack inventory + export first.  
**CONFLICT (resolved):** Runtime Decimacon/MoS/Wan LRM before inventoriable pack → drop (locked OOS).  
**CONFLICT (resolved):** Comfy as SPZ product SD brain → drop; Comfy = Track B / lab only (Forge Neo = product).

## PBR stage map (macro — burner)

| Stage | Name | Tools / process | Lands in |
|-------|------|-----------------|----------|
| **S1** | **Comfy executes VideoNeuMat GT** | Checklist + [`comfy-training-paradigm.md`](../../specs/pbr-generation/comfy-training-paradigm.md) (T10 caps) | Track B offline |
| **S2** | Companion train | LoRA/decoder on Comfy-built data; Neo-loadable weights | Track B offline |
| **S3** | **Forge Neo in SPZ** | Hub → Neo `:7860` generates MR maps (trained weights) | Spec R4 / T5 |
| **S0/S4** | Pack host | GenData inventory / import / export / bind | Spec T3–T4, T6–T7 |

VideoNeuMat = **process GT** (what Comfy must replicate); Comfy = farm; Neo = in-SPZ generate after train.

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
