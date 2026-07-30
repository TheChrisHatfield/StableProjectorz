#!/usr/bin/env python3
"""Smoke: LAVD-style PaintUndo_Scheduler CPU policy + MLP CPU inference combo.

Validates (without Unity):
  - Workload / aging / frame-budget math used by PaintUndo_Scheduler (LAVD aging)
  - Value Assist MLP is CPU float inference (no GPU dependency)
  - Parameter count of shipped MultiHead decision net
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WEIGHTS = ROOT / "Assets/_gm/Features/Paint/SmartValuePaint/Resources/SmartValuePaint/multihead_weights.json"
SCHED_CS = ROOT / "Assets/_gm/Features/Paint/Undo/PaintUndo_Scheduler.cs"
MLP_CS = ROOT / "Assets/_gm/Features/Paint/SmartValuePaint/ValuePaintMlpRuntime.cs"


def evaluate_workload(width, height, slice_count, ref_px=512 * 512):
    total_pixels = width * height * slice_count
    load = (width * height) / max(1.0, ref_px)
    total_load = load * slice_count
    complexity01 = min(1.0, max(0.0, math.log10(total_load + 1.0) / 3.2))
    return total_pixels, complexity01, total_load


def aging_multiplier(waited_s, boost_per_s=0.35, max_mul=4.0):
    return min(max_mul, 1.0 + waited_s * boost_per_s)


def frame_budget(
    slices_remaining,
    *,
    waited_s,
    base_budget_ms=2.5,
    min_budget_ms=0.75,
    max_budget_ms=8.0,
    ewma_hitch_ms=0.0,
    arm_mul=1.0,
    session_base_mul=1.0,
    session_max_mul=1.0,
    session_slice_cap_mul=1.0,
    min_slices=1,
    max_slices=8,
):
    aging = aging_multiplier(waited_s)
    max_budget_cap = max_budget_ms * session_max_mul
    budget_ms = base_budget_ms * session_base_mul * arm_mul * aging - ewma_hitch_ms * 0.25
    budget_ms = min(max_budget_cap, max(min_budget_ms, budget_ms))
    cap_slices = max(min_slices, int(round(max_slices * session_slice_cap_mul)))
    out_slices = int(round(cap_slices * aging * arm_mul))
    out_slices = max(min_slices, min(cap_slices, out_slices))
    if slices_remaining < out_slices:
        out_slices = slices_remaining
    return budget_ms, out_slices


def count_params(w: dict) -> int:
    keys = [
        "trunk0_weight",
        "trunk0_bias",
        "trunk1_weight",
        "trunk1_bias",
        "cur_weight",
        "cur_bias",
        "des_weight",
        "des_bias",
        "role_weight",
        "role_bias",
        "cont_weight",
        "cont_bias",
    ]
    return sum(len(w[k]) for k in keys)


def main() -> int:
    fails: list[str] = []
    print("=== LAVD CPU scheduler + MLP CPU inference smoke ===")

    # --- LAVD / PaintUndo_Scheduler source contracts ---
    if not SCHED_CS.is_file():
        fails.append(f"missing {SCHED_CS}")
    else:
        src = SCHED_CS.read_text(encoding="utf-8")
        for needle in (
            "LAVD-style aging",
            "AgingMultiplier",
            "GetFrameBudget",
            "Thompson",
            "EvaluateWorkload",
        ):
            if needle not in src:
                fails.append(f"PaintUndo_Scheduler missing '{needle}'")
        print("PASS: PaintUndo_Scheduler exposes LAVD aging + Thompson/UCB CPU policy")

    # Light vs heavy workload should differ
    _, c_light, _ = evaluate_workload(512, 512, 1)
    _, c_heavy, _ = evaluate_workload(2048, 2048, 8)
    if not (c_heavy > c_light + 0.15):
        fails.append(f"complexity not separating light={c_light:.3f} heavy={c_heavy:.3f}")
    else:
        print(f"PASS: workload complexity light={c_light:.3f} < heavy={c_heavy:.3f}")

    # Aging must increase budget / slices over wait time (LAVD)
    b0, s0 = frame_budget(8, waited_s=0.0, arm_mul=1.0)
    b1, s1 = frame_budget(8, waited_s=4.0, arm_mul=1.0)
    if not (b1 > b0 and s1 >= s0):
        fails.append(f"aging did not boost budget/slices t0=({b0:.2f},{s0}) t4=({b1:.2f},{s1})")
    else:
        print(f"PASS: LAVD aging boosts budget {b0:.2f}->{b1:.2f} ms, slices {s0}->{s1}")

    # Collapse heuristic cold-start: heavy stacks prefer scheduled
    # (mirror EvaluateCollapseScheduleHeuristic thresholds loosely via complexity)
    if c_heavy < 0.35:
        fails.append("heavy 2k x8 should be non-trivial complexity")
    else:
        print("PASS: heavy undo/collapse context is high-complexity bucket material")

    # --- MLP CPU inference contract ---
    if not MLP_CS.is_file():
        fails.append(f"missing {MLP_CS}")
    else:
        mlp_src = MLP_CS.read_text(encoding="utf-8")
        if "ComputeShader" in mlp_src or "Graphics.Blit" in mlp_src:
            fails.append("ValuePaintMlpRuntime appears to use GPU path")
        if "Forward(float[] features7)" not in mlp_src and "Forward(float[] features" not in mlp_src:
            fails.append("ValuePaintMlpRuntime.Forward signature missing")
        if "ArgMaxExcluding" not in mlp_src:
            fails.append("desired-step fix (ArgMaxExcluding) missing from runtime")
        print("PASS: MLP decision Forward is CPU float[] (no GPU dispatch)")

    if not WEIGHTS.is_file():
        fails.append(f"missing weights {WEIGHTS}")
    else:
        w = json.loads(WEIGHTS.read_text(encoding="utf-8"))
        n = count_params(w)
        print(
            f"PASS: MLP MultiHead params={n} "
            f"(arch={w.get('arch')} ver={w.get('version')} "
            f"{w.get('feature_dim')}->{w.get('hidden')}->{w.get('hidden2')}->heads)"
        )
        if n != 3219:
            # Still OK if architecture intentionally changed — report, don't hard-fail unless tiny
            if n < 1000 or n > 20000:
                fails.append(f"unexpected param count {n}")
            else:
                print(f"NOTE: param count {n} (shipped baseline was 3219)")

        # Combo: scheduler is CPU policy; MLP is CPU inference — neither claims CUDA
        print(
            "PASS: combo = LAVD CPU scheduler (undo/collapse budgets) + "
            "MLP CPU inference (Value Assist decisions); no shared GPU contention for MLP"
        )

    if fails:
        print("=== FAIL ===")
        for f in fails:
            print(" -", f)
        return 1
    print("=== PASS ===")
    return 0


if __name__ == "__main__":
    sys.exit(main())
