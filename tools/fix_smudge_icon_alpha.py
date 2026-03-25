"""Rebuild icon_smudge.png: source is opaque black + dark gray lines — no real transparency."""
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ICONS = ROOT / "Assets/_gm/Art/Icons/icon_smudge.png"
RES = ROOT / "Assets/_gm/Art/Resources/icon_smudge.png"


def main():
    im = Image.open(ICONS).convert("RGBA")
    a = np.asarray(im, dtype=np.float32)
    rgb = a[:, :, :3]
    lum = rgb.mean(axis=2)
    # Background ~0; ink ~8–68. Soft band for anti-aliasing.
    t0, t1 = 6.0, 22.0
    alpha = np.clip((lum - t0) / (t1 - t0) * 255.0, 0.0, 255.0)
    out = np.zeros((im.height, im.width, 4), dtype=np.uint8)
    out[:, :, 0] = 255
    out[:, :, 1] = 255
    out[:, :, 2] = 255
    out[:, :, 3] = alpha.astype(np.uint8)
    out_im = Image.fromarray(out, "RGBA")
    out_im.save(ICONS, "PNG")
    out_im.save(RES, "PNG")
    al = out[:, :, 3]
    print(
        "Wrote",
        ICONS.name,
        "transparent%",
        round(100.0 * (al == 0).mean(), 1),
        "ink%",
        round(100.0 * (al > 10).mean(), 1),
    )


if __name__ == "__main__":
    main()
