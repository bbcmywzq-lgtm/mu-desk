"""Map generated LightPet frames onto the established pack color palette.

The generated outline and alpha geometry are preserved. Only opaque RGB values
are quantized against a shared palette learned from approved reference frames,
which prevents cross-generation skin, white, black, purple and blue drift.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


def build_palette(reference_paths: list[Path], colors: int) -> Image.Image:
    samples: list[Image.Image] = []
    for path in reference_paths:
        with Image.open(path) as opened:
            rgba = opened.convert("RGBA")
            background = Image.new("RGB", rgba.size, "#ffffff")
            background.paste(rgba.convert("RGB"), mask=rgba.getchannel("A"))
            samples.append(background.resize((160, 160), Image.Resampling.BOX))

    strip = Image.new("RGB", (160 * len(samples), 160), "#ffffff")
    for index, sample in enumerate(samples):
        strip.paste(sample, (index * 160, 0))
    return strip.quantize(
        colors=colors,
        method=Image.Quantize.MEDIANCUT,
        dither=Image.Dither.NONE,
    )


def harmonize(path: Path, palette: Image.Image) -> None:
    with Image.open(path) as opened:
        rgba = opened.convert("RGBA")
    alpha = rgba.getchannel("A")
    mapped = rgba.convert("RGB").quantize(
        palette=palette,
        dither=Image.Dither.NONE,
    ).convert("RGB")
    result = mapped.convert("RGBA")
    result.putalpha(alpha)
    pixels = result.load()
    for y in range(result.height):
        for x in range(result.width):
            if pixels[x, y][3] == 0:
                pixels[x, y] = (0, 0, 0, 0)
    result.save(path, optimize=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("target", type=Path)
    parser.add_argument("--reference", type=Path, action="append", required=True)
    parser.add_argument("--colors", type=int, default=256)
    args = parser.parse_args()
    if not 32 <= args.colors <= 256:
        raise ValueError("--colors must be between 32 and 256")

    references = [path.resolve() for path in args.reference]
    palette = build_palette(references, args.colors)
    targets = sorted(args.target.rglob("*.png")) if args.target.is_dir() else [args.target]
    for path in targets:
        harmonize(path, palette)
        print(path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
