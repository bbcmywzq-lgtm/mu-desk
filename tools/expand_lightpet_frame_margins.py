"""Translate transparent PNG frames just enough to satisfy a safe margin.

The character is never rescaled. This keeps apparent size and line weight stable
while giving actions that approach a canvas edge a little more breathing room.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


def nearest_shift(start: int, end: int, extent: int, margin: int) -> int:
    minimum = margin - start
    maximum = extent - margin - end
    if minimum > maximum:
        raise ValueError(
            f"content span {end - start}px cannot fit inside {extent}px "
            f"with {margin}px margins without scaling"
        )
    if minimum <= 0 <= maximum:
        return 0
    return minimum if minimum > 0 else maximum


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("frames", type=Path)
    parser.add_argument("--margin", type=int, default=70)
    args = parser.parse_args()

    paths = sorted(args.frames.rglob("*.png"))
    if not paths:
        raise SystemExit(f"No PNG frames found under {args.frames}")

    for path in paths:
        image = Image.open(path).convert("RGBA")
        alpha = image.getchannel("A")
        bbox = alpha.getbbox()
        if bbox is None:
            raise ValueError(f"blank frame: {path}")

        dx = nearest_shift(bbox[0], bbox[2], image.width, args.margin)
        dy = nearest_shift(bbox[1], bbox[3], image.height, args.margin)
        if dx or dy:
            translated = Image.new("RGBA", image.size, (0, 0, 0, 0))
            translated.alpha_composite(image, (dx, dy))
            image = translated

        # Fully transparent pixels must not retain hidden RGB residue.
        pixels = image.load()
        for y in range(image.height):
            for x in range(image.width):
                if pixels[x, y][3] == 0:
                    pixels[x, y] = (0, 0, 0, 0)

        image.save(path, optimize=True)
        final_bbox = image.getchannel("A").getbbox()
        assert final_bbox is not None
        margins = (
            final_bbox[0],
            final_bbox[1],
            image.width - final_bbox[2],
            image.height - final_bbox[3],
        )
        print(f"{path.name}: shift=({dx:+d},{dy:+d}) margins={margins}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
