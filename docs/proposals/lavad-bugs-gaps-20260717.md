# LAVAD / LAVD portion — bugs & gaps (2026-07-17, round 2)

**Surface:** `tools/mlp-decimacon-soil/src/hive_code_dev_1/lavd/`  
**Law:** scheduler ≠ paint reasoner (Pass B; not Unity runtime)

---

## Round 1 fixes (still hold)

| # | Defect | Fix |
|---|--------|-----|
| 1 | Pre-dispatch `update_bandit` with budget 0 poisoned Beta | `no_dispatch_budget` |
| 2 | Invalid arm raised | `unknown_arm` skip |
| 3 | HT physical P/E vs logical cores | Normalize `p+e == core_count` |

## Round 2 fixes

| # | Defect | Fix |
|---|--------|-----|
| 4 | NaN util still `signals_stable=True` (NaN comparisons are False) and poisoned `CpuContext` / free_cores | Reject non-finite; sanitize utils to [0,1] |
| 5 | Empty `BanditState.arms` → silent fake `LATENCY_CRITICAL` / sample −1 | `ValueError` |
| 6 | Non-finite accuracy/latency in evidence / success | Finite guards in `bandit_success` / `compute_evidence_quality` / Beta params |

Smoke (`PYTHONPATH=tools/mlp-decimacon-soil/src`): prior skips · NaN unstable + finite ctx · empty arms raises · post-dispatch update OK.

---

## Gaps (product / wiring — not fixed)

| Gap | Status |
|-----|--------|
| No lavd unit test package under soil | Missing |
| EXTRALAVD 3-arm story vs 4 `BanditArm` | Map only |
| Bandit → paint DTO | Locked drop |
| Spec-AC into SVP | Backlog |
| Idle exploration / real OS hybrid topology | Backlog |
| Pip may load `E:\…\HIVE_CODE_DEV_1` over soil | Use soil PYTHONPATH / editable install |
| Unity does not call LAVAD | Intentional Pass B |
| `prior_builder` never seeds betas | Soft gap |
| `ingest_feedback_via_sigma` without `measured` returns zeros | By design (no fabricate) |

## Verdict

**Code bugs on lavd hot path addressed for rounds 1–2.** Remaining items are gaps / backlog, not silent correctness faults.
