#!/usr/bin/env python3
"""Downsample a published LightPet pack while keeping source masters untouched."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image


def scaled(value: int | float, factor: float) -> int:
    return int(round(float(value) * factor))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("pack", type=Path)
    parser.add_argument("--canvas-size", type=int, default=600)
    args = parser.parse_args()

    pack = args.pack.resolve()
    manifest_path = pack / "pet.json"
    if not manifest_path.is_file():
        raise FileNotFoundError(f"Missing pack manifest: {manifest_path}")
    if args.canvas_size < 256:
        raise ValueError("Runtime canvas size must be at least 256px.")

    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    canvas = manifest["canvas"]
    source_width = int(canvas["width"])
    source_height = int(canvas["height"])
    if source_width != source_height:
        raise ValueError(f"Runtime optimizer requires a square canvas, got {source_width}x{source_height}.")
    if source_width < args.canvas_size:
        raise ValueError(f"Refusing to upscale {source_width}px pack to {args.canvas_size}px.")

    frames = sorted((pack / "actions").rglob("*.png"))
    if not frames:
        raise ValueError(f"Pack contains no PNG frames: {pack}")
    for frame in frames:
        with Image.open(frame) as opened:
            if opened.size != (source_width, source_height):
                raise ValueError(
                    f"Frame {frame} is {opened.width}x{opened.height}; expected "
                    f"{source_width}x{source_height}."
                )

    if source_width == args.canvas_size:
        print(f"Runtime pack already uses {args.canvas_size}px: {pack}")
        return 0

    for frame in frames:
        with Image.open(frame) as opened:
            image = opened.convert("RGBA").resize(
                (args.canvas_size, args.canvas_size),
                Image.Resampling.LANCZOS,
            )
        rgba = np.asarray(image, dtype=np.uint8).copy()
        rgba[rgba[:, :, 3] == 0, :3] = 0
        temporary = frame.with_suffix(".runtime.tmp.png")
        Image.fromarray(rgba, "RGBA").save(temporary, optimize=True, compress_level=9)
        temporary.replace(frame)

    factor = args.canvas_size / source_width
    for key in ("width", "height", "anchorX", "anchorY"):
        canvas[key] = scaled(canvas[key], factor)
    for region in manifest.get("hitRegions", []):
        for key in ("x", "y", "width", "height"):
            region[key] = scaled(region[key], factor)

    temporary_manifest = manifest_path.with_suffix(".runtime.tmp.json")
    temporary_manifest.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    temporary_manifest.replace(manifest_path)
    print(
        f"Optimized {len(frames)} runtime frames: "
        f"{source_width}px -> {args.canvas_size}px ({pack})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
