# LAVAD / LAVD portion — bugs & gaps (2026-07-17)

**Surface:** `tools/mlp-decimacon-soil/src/hive_code_dev_1/lavd/`  
**Law:** scheduler ≠ paint reasoner (Pass B; not Unity runtime)  
**Beacon:** `cartridge/micro/lavd-cpu-scheduler.md` · EXTRALAVD source4s

---

## Bugs fixed this pass

| # | Defect | Fix |
|---|--------|-----|
| 1 | `update_bandit` before first `dispatch` used default `latency_budget=0` → every measured outcome failed (`latency < 0` impossible) and **poisoned Beta** | Skip with `no_dispatch_budget` |
| 2 | Invalid `selected_arm` raised `ValueError` instead of gated skip | `unknown_arm` `BanditUpdateResult` |
| 3 | HT hosts: physical P/E + logical `core_count` → **8 domains vs 16 cpu_contexts** | Normalize so `p+e == core_count` in probe + `dispatch` |

Smoke (soil `PYTHONPATH`): pre-dispatch skip · invalid arm skip · domains==cpu_ctx==16 · post-dispatch success.

**Note:** system `pip` may still resolve `E:\…\HIVE_CODE_DEV_1\src` first — run soil with `PYTHONPATH=tools/mlp-decimacon-soil/src` or `pip install -e` the soil.

---

## Gaps (not bugs — product / wiring)

| Gap | Status |
|-----|--------|
| No dedicated `tests/` under soil for lavd | Missing regression suite |
| EXTRALAVD narrative **3 arms** vs soil **4** `BanditArm` | Documented map; not auto-synced |
| Bandit → `ValuePaintProposal` | **CONFLICT → drop** (locked) |
| Spec-AC LAVD into SVP tasks | BACKLOG until Pass B opened |
| Idle pen-up exploration phase | BACKLOG catalyst |
| Real OS hybrid P/E topology (Windows) | Heuristic split only |
| `__init__.py` does not export catalyst / prior / probe / intent | Import via submodule |
| Unity paint path does not call LAVAD | Intentional (Pass B soil only) |

---

## Architecture wire (soil only)

```
HostTelemetryProbe / TelemetrySnapshot
  → LavadSmartScheduler.dispatch
  → SchedulerSignalPacket (+ Σ encode)
  → build_input_bundle → Decimacon forward
  → PerformanceFeedback (measured)
  → update_bandit → Beta posterior
```

Catalyst hints bias Thompson arm; router consumes `scheduler_intent` + sparsity — separate from paint DTO.
