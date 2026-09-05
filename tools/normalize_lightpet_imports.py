#!/usr/bin/env python3
"""Uniformly scale imported action canvases around the LightPet foot anchor."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


def normalize(path: Path, scale: float, anchor_x: int, anchor_y: int) -> None:
    with Image.open(path) as opened:
        image = opened.convert("RGBA")
    scaled_size = (round(image.width * scale), round(image.height * scale))
    resized = image.resize(scaled_size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", image.size, (0, 0, 0, 0))
    x = round(anchor_x - anchor_x * scale)
    y = round(anchor_y - anchor_y * scale)
    canvas.alpha_composite(resized, (x, y))
    rgba = np.asarray(canvas, dtype=np.uint8).copy()
    rgba[rgba[:, :, 3] == 0, :3] = 0
    Image.fromarray(rgba, "RGBA").save(path, optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("--scale", type=float, default=0.948)
    parser.add_argument("--anchor-x", type=int, default=500)
    parser.add_argument("--anchor-y", type=int, default=945)
    parser.add_argument(
        "--scales",
        help="Optional comma-separated per-frame scales, applied in sorted filename order.",
    )
    args = parser.parse_args()
    files = sorted(args.root.rglob("*.png"))
    scales = [args.scale] * len(files)
    if args.scales:
        scales = [float(value.strip()) for value in args.scales.split(",") if value.strip()]
        if len(scales) != len(files):
            parser.error(f"--scales supplied {len(scales)} values for {len(files)} PNG files")
    for path, scale in zip(files, scales):
        normalize(path, scale, args.anchor_x, args.anchor_y)
    if args.scales:
        print(f"Normalized {len(files)} imported frames with per-frame scale ramp")
    else:
        print(f"Normalized {len(files)} imported frames at scale {args.scale:.3f}")


if __name__ == "__main__":
    main()
