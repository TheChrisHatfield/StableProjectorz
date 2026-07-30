#!/usr/bin/env python3
"""Headless smoke: Value Assist MLP (T9.2 MultiHead) decision path.

Mirrors Assets/_gm/Features/Paint/SmartValuePaint/ValuePaintMlpRuntime.cs
+ ValuePaintFeatureBuilder.FromLuminance so we can validate without Unity.

Exit 0 = PASS, 1 = FAIL.
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WEIGHTS = ROOT / "Assets/_gm/Features/Paint/SmartValuePaint/Resources/SmartValuePaint/multihead_weights.json"

BINS = ["Highlight", "Light", "Midtone", "Shadow", "AccentDark"]
ROLES = ["BlockIn", "ReinforcePlane", "BridgePlanes", "SoftenTransition", "AccentDark"]

HIGHLIGHT_MIN, LIGHT_MIN, MIDTONE_MIN, SHADOW_MIN = 0.85, 0.65, 0.40, 0.20


def band_from_lum(l: float) -> int:
    l = max(0.0, min(1.0, l))
    if l >= HIGHLIGHT_MIN:
        return 0
    if l >= LIGHT_MIN:
        return 1
    if l >= MIDTONE_MIN:
        return 2
    if l >= SHADOW_MIN:
        return 3
    return 4


def features_from_lum(lum: float, edge: float = 0.15) -> list[float]:
    lum = 0.5 if not math.isfinite(lum) else max(0.0, min(1.0, lum))
    band = band_from_lum(lum)
    hist = [0.85 if i == band else 0.0375 for i in range(5)]
    s = sum(hist)
    hist = [h / s for h in hist]
    return [lum, *hist, max(0.0, min(1.0, edge))]


class Mlp:
    def __init__(self, w: dict):
        self.in_dim = int(w.get("feature_dim") or 7)
        self.h1 = int(w.get("hidden") or 64)
        self.h2 = int(w.get("hidden2") or 32)
        self.t0w = w["trunk0_weight"]
        self.t0b = w["trunk0_bias"]
        self.t1w = w["trunk1_weight"]
        self.t1b = w["trunk1_bias"]
        self.curW, self.curB = w["cur_weight"], w["cur_bias"]
        self.desW, self.desB = w["des_weight"], w["des_bias"]
        self.roleW, self.roleB = w["role_weight"], w["role_bias"]
        self.contW, self.contB = w["cont_weight"], w["cont_bias"]
        self._validate()

    def _expect(self, a, n, name):
        if a is None or len(a) != n:
            raise RuntimeError(f"{name} len={-1 if a is None else len(a)} expected {n}")

    def _validate(self):
        self._expect(self.t0w, self.h1 * self.in_dim, "trunk0_weight")
        self._expect(self.t0b, self.h1, "trunk0_bias")
        self._expect(self.t1w, self.h2 * self.h1, "trunk1_weight")
        self._expect(self.t1b, self.h2, "trunk1_bias")
        self._expect(self.curW, 5 * self.h2, "cur_weight")
        self._expect(self.curB, 5, "cur_bias")
        self._expect(self.desW, 5 * self.h2, "des_weight")
        self._expect(self.desB, 5, "des_bias")
        self._expect(self.roleW, 5 * self.h2, "role_weight")
        self._expect(self.roleB, 5, "role_bias")
        self._expect(self.contW, 4 * self.h2, "cont_weight")
        self._expect(self.contB, 4, "cont_bias")

    @staticmethod
    def _dot_row(w, row, in_dim, x):
        off = row * in_dim
        return sum(w[off + i] * x[i] for i in range(in_dim))

    def _linear_relu(self, w, b, x, in_dim, out_dim):
        y = []
        for o in range(out_dim):
            s = b[o] + self._dot_row(w, o, in_dim, x)
            y.append(s if s > 0 else 0.0)
        return y

    def _linear(self, w, b, x, in_dim, out_dim):
        return [b[o] + self._dot_row(w, o, in_dim, x) for o in range(out_dim)]

    @staticmethod
    def _argmax(v):
        best, bv = 0, v[0]
        for i in range(1, len(v)):
            if v[i] > bv:
                best, bv = i, v[i]
        return best

    @staticmethod
    def _sigmoid(z):
        if z > 20:
            return 1.0
        if z < -20:
            return 0.0
        return 1.0 / (1.0 + math.exp(-z))

    def forward(self, feat: list[float]) -> dict:
        if len(feat) < self.in_dim:
            raise ValueError("features")
        hA = self._linear_relu(self.t0w, self.t0b, feat, self.in_dim, self.h1)
        hB = self._linear_relu(self.t1w, self.t1b, hA, self.h1, self.h2)
        cur = self._argmax(self._linear(self.curW, self.curB, hB, self.h2, 5))
        des_logits = self._linear(self.desW, self.desB, hB, self.h2, 5)
        des = self._argmax(des_logits)
        # Mirror ValuePaintMlpRuntime: desired head peaks on current — take next-best.
        if des == cur:
            des = self._argmax_excluding(des_logits, cur)
        role = self._argmax(self._linear(self.roleW, self.roleB, hB, self.h2, 5))
        blend = self._sigmoid(self._dot_row(self.contW, 0, self.h2, hB) + self.contB[0])
        edge = self._sigmoid(self._dot_row(self.contW, 1, self.h2, hB) + self.contB[1])
        width = self._sigmoid(self._dot_row(self.contW, 2, self.h2, hB) + self.contB[2])
        op = self._sigmoid(self._dot_row(self.contW, 3, self.h2, hB) + self.contB[3])
        return {
            "cur": cur,
            "des": des,
            "role": role,
            "blend": blend,
            "edge": edge,
            "width": width,
            "op": op,
        }

    @staticmethod
    def _argmax_excluding(v, exclude):
        best = 1 if exclude == 0 else 0
        bv = v[best]
        for i in range(len(v)):
            if i == exclude:
                continue
            if v[i] > bv:
                best, bv = i, v[i]
        return best


def finite01(x: float) -> bool:
    return math.isfinite(x) and 0.0 <= x <= 1.0


def main() -> int:
    fails: list[str] = []
    print("=== Value Assist MLP decision smoke ===")
    print(f"weights: {WEIGHTS}")

    if not WEIGHTS.is_file():
        print("FAIL: weights JSON missing")
        return 1

    w = json.loads(WEIGHTS.read_text(encoding="utf-8"))
    print(f"meta: version={w.get('version')} arch={w.get('arch')} "
          f"feature_dim={w.get('feature_dim')} hidden={w.get('hidden')}/{w.get('hidden2')}")

    try:
        net = Mlp(w)
    except Exception as e:
        print(f"FAIL: weight shape validate — {e}")
        return 1
    print("PASS: weight tensors match MultiHead layout (7->64->32->heads)")

    # Luminance ladder — MLP current-bin should track value plane (allow ±1 neighbor).
    ladder = [0.95, 0.75, 0.50, 0.30, 0.08]
    results = []
    for lum in ladder:
        feat = features_from_lum(lum)
        o = net.forward(feat)
        results.append((lum, o))
        expected = band_from_lum(lum)
        print(
            f"  lum={lum:.2f} expect={BINS[expected]:12s} "
            f"cur={BINS[o['cur']]:12s} des={BINS[o['des']]:12s} "
            f"role={ROLES[o['role']]:18s} "
            f"blend={o['blend']:.2f} edge={o['edge']:.2f} "
            f"w={o['width']:.2f} op={o['op']:.2f}"
        )
        for k in ("blend", "edge", "width", "op"):
            if not finite01(o[k]):
                fails.append(f"lum={lum}: {k}={o[k]} not finite in [0,1]")
        if abs(o["cur"] - expected) > 1:
            fails.append(
                f"lum={lum}: current bin {BINS[o['cur']]} far from expected {BINS[expected]}"
            )
        if not (0 <= o["cur"] <= 4 and 0 <= o["des"] <= 4 and 0 <= o["role"] <= 4):
            fails.append(f"lum={lum}: class index out of range")

    curs = {o["cur"] for _, o in results}
    dess = {o["des"] for _, o in results}
    if len(curs) < 3:
        fails.append(f"current-bin decisions look stuck (only {sorted(curs)})")
    if len(dess) < 2:
        fails.append(f"desired-bin decisions look stuck (only {sorted(dess)})")

    # Value Assist exists to step planes — desired==current everywhere is a decision fail.
    same_plane = sum(1 for _, o in results if o["cur"] == o["des"])
    if same_plane == len(results):
        fails.append(
            "desired-bin equals current-bin on every luminance sample "
            "(no value-plane step - Accept would recolor into the same band)"
        )
    else:
        mid = next(o for lum, o in results if abs(lum - 0.5) < 1e-6)
        if mid["cur"] == mid["des"] == 2:
            print("NOTE: midtone->midtone (no plane step) - check training intent")

    # Highlight should not propose AccentDark as current; AccentDark should not propose Highlight as current.
    hi = next(o for lum, o in results if lum >= 0.9)
    lo = next(o for lum, o in results if lum <= 0.1)
    if hi["cur"] >= 3:
        fails.append(f"highlight sample classified as dark current={BINS[hi['cur']]}")
    if lo["cur"] <= 1:
        fails.append(f"near-black sample classified as light current={BINS[lo['cur']]}")

    # Deterministic ladder endpoints should differ from each other.
    if hi["cur"] == lo["cur"]:
        fails.append("highlight and accent-dark share the same current bin - MLP not discriminating")

    # Factory preference: neural path would load these weights (file present + valid).
    print("PASS: Resources path payload is loadable for MlpValuePaintAssist.TryCreate")

    if fails:
        print("=== FAIL ===")
        for f in fails:
            print(" -", f)
        return 1

    print("=== PASS: MLP decision smoke OK ===")
    print("Wiring: weights JSON -> ValuePaintMlpRuntime.Forward -> MlpValuePaintAssist "
          "-> factory (Neural on) -> Propose/Accept / Live")
    return 0


if __name__ == "__main__":
    sys.exit(main())
