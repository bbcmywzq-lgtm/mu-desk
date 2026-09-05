"""Re-extract a generated action strip by whole-character components.

This is intended for strips whose poses cross nominal equal-width slots. A
fixed grid can split hair or limbs into the neighbouring frame even though the
original strip contains the complete drawing.
"""

from __future__ import annotations

import argparse
from collections import defaultdict
from pathlib import Path

import numpy as np
from PIL import Image


def component_runs(mask: np.ndarray) -> list[tuple[int, list[tuple[int, int, int]]]]:
    runs: list[tuple[int, int, int]] = []
    parent: list[int] = []
    area: list[int] = []
    previous: list[int] = []

    def find(index: int) -> int:
        while parent[index] != index:
            parent[index] = parent[parent[index]]
            index = parent[index]
        return index

    def union(first: int, second: int) -> None:
        root_first = find(first)
        root_second = find(second)
        if root_first == root_second:
            return
        if area[root_first] < area[root_second]:
            root_first, root_second = root_second, root_first
        parent[root_second] = root_first
        area[root_first] += area[root_second]

    for y, row in enumerate(mask):
        padded = np.pad(row.astype(np.int8), (1, 1))
        changes = np.diff(padded)
        starts = np.flatnonzero(changes == 1)
        ends = np.flatnonzero(changes == -1)
        current: list[int] = []
        prior_index = 0
        for start_value, end_value in zip(starts, ends):
            start = int(start_value)
            end = int(end_value)
            run_index = len(runs)
            runs.append((y, start, end))
            parent.append(run_index)
            area.append(end - start)

            while prior_index < len(previous) and runs[previous[prior_index]][2] <= start:
                prior_index += 1
            overlap_index = prior_index
            while overlap_index < len(previous) and runs[previous[overlap_index]][1] < end:
                union(run_index, previous[overlap_index])
                overlap_index += 1
            current.append(run_index)
        previous = current

    grouped: defaultdict[int, list[tuple[int, int, int]]] = defaultdict(list)
    for index, run in enumerate(runs):
        grouped[find(index)].append(run)
    return sorted(
        ((sum(end - start for _, start, end in group), group) for group in grouped.values()),
        key=lambda item: item[0],
        reverse=True,
    )


def extract_components(strip: Image.Image, count: int) -> list[tuple[Image.Image, tuple[int, int, int, int]]]:
    rgba = np.asarray(strip.convert("RGBA"), dtype=np.uint8)
    red = rgba[:, :, 0].astype(np.int16)
    green = rgba[:, :, 1].astype(np.int16)
    blue = rgba[:, :, 2].astype(np.int16)
    chroma = (green > 70) & ((green - red) > 24) & ((green - blue) > 24)

    selected = component_runs(~chroma)[:count]
    if len(selected) != count:
        raise ValueError(f"expected {count} character components, found {len(selected)}")

    components: list[tuple[Image.Image, tuple[int, int, int, int]]] = []
    for component_area, runs in selected:
        left = min(start for _, start, _ in runs)
        top = min(y for y, _, _ in runs)
        right = max(end for _, _, end in runs)
        bottom = max(y + 1 for y, _, _ in runs)
        component_mask = np.zeros((bottom - top, right - left), dtype=bool)
        for y, start, end in runs:
            component_mask[y - top, start - left : end - left] = True

        crop = rgba[top:bottom, left:right].copy()
        crop_red = crop[:, :, 0].astype(np.int16)
        crop_green = crop[:, :, 1].astype(np.int16)
        crop_blue = crop[:, :, 2].astype(np.int16)
        spill = np.maximum(0, crop_green - np.maximum(crop_red, crop_blue))
        crop[:, :, 1] = np.clip(crop_green - spill, 0, 255).astype(np.uint8)
        crop[:, :, 3] = np.where(component_mask, 255, 0).astype(np.uint8)
        crop[~component_mask, :3] = 0
        components.append((Image.fromarray(crop, "RGBA"), (left, top, right, bottom)))
        print(f"component area={component_area} bbox={(left, top, right, bottom)}")

    components.sort(key=lambda item: item[1][0])
    return components


def normalize(
    components: list[tuple[Image.Image, tuple[int, int, int, int]]],
    target_height: int,
    align: str = "bottom",
    margin: int = 45,
) -> list[Image.Image]:
    boxes = [box for _, box in components]
    maximum_height = max(bottom - top for _, top, _, bottom in boxes)
    maximum_width = max(right - left for left, _, right, _ in boxes)
    baseline = max(bottom for _, _, _, bottom in boxes)
    vertical_span = baseline - min(top for _, top, _, _ in boxes)
    scale = min(target_height / maximum_height, 920 / maximum_width, 925 / vertical_span)

    frames: list[Image.Image] = []
    for crop, box in components:
        new_size = (
            max(1, round(crop.width * scale)),
            max(1, round(crop.height * scale)),
        )
        crop = crop.resize(new_size, Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (1000, 1000), (0, 0, 0, 0))
        x = (1000 - crop.width) // 2
        if align == "top":
            source_top = min(top for _, top, _, _ in boxes)
            y = round(margin + (box[1] - source_top) * scale)
        elif align == "center":
            source_center = (
                min(top for _, top, _, _ in boxes) +
                max(bottom for _, _, _, bottom in boxes)
            ) / 2
            component_center = (box[1] + box[3]) / 2
            y = round(500 + (component_center - source_center) * scale - crop.height / 2)
        else:
            bottom = round((1000 - margin) + (box[3] - baseline) * scale)
            y = bottom - crop.height
        if y < 0 or y + crop.height > 1000:
            raise ValueError(
                f"normalized component exceeds canvas for align={align}: "
                f"y={y}, height={crop.height}"
            )
        canvas.alpha_composite(crop, (x, y))
        frames.append(canvas)
    return frames


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("strip", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--durations", required=True)
    parser.add_argument("--target-height", type=int, default=890)
    parser.add_argument("--align", choices=("top", "center", "bottom"), default="bottom")
    parser.add_argument("--margin", type=int, default=45)
    args = parser.parse_args()

    durations = [int(value) for value in args.durations.split(",")]
    components = extract_components(Image.open(args.strip), len(durations))
    frames = normalize(components, args.target_height, args.align, args.margin)
    args.output.mkdir(parents=True, exist_ok=True)
    for index, (frame, duration) in enumerate(zip(frames, durations)):
        path = args.output / f"frame_{index:03d}_{duration}.png"
        frame.save(path, optimize=True)
        bbox = frame.getchannel("A").getbbox()
        print(f"saved {path.name} bbox={bbox}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
