from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract a green-screen LightPet frame and place it on a transparent square canvas."
    )
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--canvas", type=int, default=1000)
    parser.add_argument("--max-width", type=int, default=680)
    parser.add_argument("--max-height", type=int, default=600)
    parser.add_argument("--center-x", type=int, default=500)
    parser.add_argument("--center-y", type=int, default=500)
    return parser.parse_args()


def extract_green_screen(source: Image.Image) -> Image.Image:
    image = source.convert("RGBA")
    output = Image.new("RGBA", image.size, (0, 0, 0, 0))
    source_pixels = image.load()
    output_pixels = output.load()

    for y in range(image.height):
        for x in range(image.width):
            red, green, blue, _ = source_pixels[x, y]
            excess = green - max(red, blue)

            # The generated backdrop varies slightly across the canvas. Green
            # dominance, rather than distance from one exact RGB value, keeps
            # the extraction deterministic while preserving purple/black art.
            if green >= 24 and excess >= 28:
                alpha = 0
            elif green >= 18 and excess > 6:
                alpha = round(255 * (28 - excess) / 22)
            else:
                alpha = 255

            if alpha <= 0:
                continue

            if alpha < 255:
                # Remove green spill from antialiased outline pixels. Fully
                # transparent pixels remain normalized to transparent black.
                green = min(green, max(red, blue))
            output_pixels[x, y] = (red, green, blue, alpha)

    return output


def fit_to_canvas(
    sprite: Image.Image,
    canvas_size: int,
    max_width: int,
    max_height: int,
    center_x: int,
    center_y: int,
) -> Image.Image:
    alpha = sprite.getchannel("A")
    bbox = alpha.getbbox()
    if bbox is None:
        raise ValueError("Chroma extraction produced an empty sprite.")

    cropped = sprite.crop(bbox)
    scale = min(max_width / cropped.width, max_height / cropped.height)
    target_size = (
        max(1, round(cropped.width * scale)),
        max(1, round(cropped.height * scale)),
    )
    resized = cropped.resize(target_size, Image.Resampling.LANCZOS)

    canvas = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    left = round(center_x - resized.width / 2)
    top = round(center_y - resized.height / 2)
    if left < 0 or top < 0 or left + resized.width > canvas_size or top + resized.height > canvas_size:
        raise ValueError("Fitted sprite exceeds the requested canvas.")
    canvas.alpha_composite(resized, (left, top))

    pixels = canvas.load()
    for y in range(canvas.height):
        for x in range(canvas.width):
            red, green, blue, alpha_value = pixels[x, y]
            excess = green - max(red, blue)
            if alpha_value and excess > 6:
                edge_factor = max(0.0, min(1.0, (28 - excess) / 22))
                alpha_value = round(alpha_value * edge_factor)
                green = min(green, max(red, blue))
                pixels[x, y] = (red, green, blue, alpha_value)
            if alpha_value == 0 and (red or green or blue):
                pixels[x, y] = (0, 0, 0, 0)
    return canvas


def main() -> int:
    args = parse_args()
    source = Image.open(args.source)
    extracted = extract_green_screen(source)
    canvas = fit_to_canvas(
        extracted,
        args.canvas,
        args.max_width,
        args.max_height,
        args.center_x,
        args.center_y,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(args.output, optimize=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
