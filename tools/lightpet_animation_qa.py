#!/usr/bin/env python3
"""Deterministic visual QA for LightPet data-only character packs."""

from __future__ import annotations

import argparse
from collections import deque
import hashlib
import json
import math
import re
import statistics
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


DURATION_RE = re.compile(r"_(\d+)\.png$", re.IGNORECASE)
REQUIRED_ACTIONS = [
    "idle", "idle-random", "walk-left", "walk-right", "raise", "fall-land",
    "touch-head", "touch-body", "pinch", "startup", "shutdown", "sleep",
    "think", "say", "happy", "sad", "surprised", "focus", "wave",
    "reminder-alert", "note-write",
    "edge-climb-left", "edge-climb-right", "edge-top-left", "edge-top-right",
    "edge-hold-left", "edge-hold-right", "edge-hold-top-left", "edge-hold-top-right",
    "observe-cursor", "head-rub", "cheek-poke", "annoyed-dodge",
    "stretch", "hair-fix", "yawn", "sit-rest",
]
MAX_TRANSITION_HEIGHT_RATIO = 0.12
MAX_TRANSITION_ANCHOR_SHIFT = 50
PALETTE_BINS = 32
COMPONENT_SAMPLE_SIZE = 250
SIGNIFICANT_COMPONENT_PIXELS = 4


@dataclass
class FrameMetrics:
    file: str
    duration_ms: int
    width: int
    height: int
    blank: bool
    bbox: list[int] | None
    margins: list[int] | None
    support_x: float | None
    support_y: int | None
    torso_x: float | None
    torso_y: float | None
    opaque_pixels: int
    transparent_rgb_pixels: int
    alpha_component_count: int
    significant_component_count: int
    detached_area_ratio: float
    stray_alpha_pixels: int
    sha256: str
    palette_similarity: float | None = None
    palette_coverage: float | None = None
    minimum_display_margins: list[float] | None = None


def alpha_bbox(image: Image.Image, threshold: int = 8) -> tuple[int, int, int, int] | None:
    alpha = image.getchannel("A").point(lambda p: 255 if p > threshold else 0)
    return alpha.getbbox()


def weighted_centroid(image: Image.Image, box: tuple[int, int, int, int]) -> tuple[float, float] | None:
    alpha = np.asarray(image.getchannel("A").crop(box), dtype=np.float64)
    total = float(alpha.sum())
    if total == 0:
        return None
    cx = box[0] + float((alpha.sum(axis=0) * np.arange(alpha.shape[1])).sum() / total)
    cy = box[1] + float((alpha.sum(axis=1) * np.arange(alpha.shape[0])).sum() / total)
    return cx, cy


def alpha_component_analysis(
    image: Image.Image,
    sample_size: int = COMPONENT_SAMPLE_SIZE,
    threshold: int = 8,
) -> tuple[list[int], np.ndarray, int | None]:
    """Return 8-connected component sizes for a downsampled alpha mask.

    BOX sampling retains small high-alpha marks while keeping the deterministic
    scan cheap enough to run across the complete animation pack.
    """
    sampled = image.getchannel("A").resize(
        (sample_size, sample_size),
        Image.Resampling.BOX,
    )
    mask = np.asarray(sampled, dtype=np.uint8) > threshold
    labels = np.full(mask.shape, -1, dtype=np.int32)
    component_sizes: list[int] = []
    height, width = mask.shape

    for start_y, start_x in np.argwhere(mask):
        if labels[start_y, start_x] >= 0:
            continue
        label = len(component_sizes)
        labels[start_y, start_x] = label
        pending: deque[tuple[int, int]] = deque([(int(start_y), int(start_x))])
        size = 0
        while pending:
            y, x = pending.popleft()
            size += 1
            for yy in range(max(0, y - 1), min(height, y + 2)):
                for xx in range(max(0, x - 1), min(width, x + 2)):
                    if mask[yy, xx] and labels[yy, xx] < 0:
                        labels[yy, xx] = label
                        pending.append((yy, xx))
        component_sizes.append(size)

    largest_label = int(np.argmax(component_sizes)) if component_sizes else None
    return component_sizes, labels, largest_label


def stray_alpha_mask(alpha: np.ndarray, threshold: int = 8) -> np.ndarray:
    """Return truly detached single pixels, excluding ordinary soft-edge dots."""
    mask = alpha > threshold
    nearby = np.zeros(mask.shape, dtype=bool)
    for dy in range(-4, 5):
        for dx in range(-4, 5):
            if dx == 0 and dy == 0:
                continue
            source_y = slice(max(0, -dy), mask.shape[0] - max(0, dy))
            source_x = slice(max(0, -dx), mask.shape[1] - max(0, dx))
            target_y = slice(max(0, dy), mask.shape[0] - max(0, -dy))
            target_x = slice(max(0, dx), mask.shape[1] - max(0, -dx))
            nearby[target_y, target_x] |= mask[source_y, source_x]
    return mask & ~nearby


def count_stray_alpha_pixels(alpha: np.ndarray, threshold: int = 8) -> int:
    return int(np.count_nonzero(stray_alpha_mask(alpha, threshold)))


def inspect_frame(path: Path, root: Path) -> FrameMetrics:
    match = DURATION_RE.search(path.name)
    duration = int(match.group(1)) if match else 0
    raw = path.read_bytes()
    with Image.open(path) as opened:
        image = opened.convert("RGBA")
    bbox = alpha_bbox(image)
    rgba = np.asarray(image)
    opaque = int(np.count_nonzero(rgba[:, :, 3] > 8))
    residue = int(np.count_nonzero((rgba[:, :, 3] == 0) & np.any(rgba[:, :, :3] != 0, axis=2)))
    component_sizes, _, _ = alpha_component_analysis(image)
    significant_components = sum(size >= SIGNIFICANT_COMPONENT_PIXELS for size in component_sizes)
    total_component_area = sum(component_sizes)
    detached_area = total_component_area - max(component_sizes, default=0)
    detached_area_ratio = round(detached_area / total_component_area, 6) if total_component_area else 0.0
    stray_alpha_pixels = count_stray_alpha_pixels(rgba[:, :, 3])

    margins = None
    support_x = None
    support_y = None
    torso_x = None
    torso_y = None
    if bbox:
        left, top, right, bottom = bbox
        margins = [left, top, image.width - right, image.height - bottom]
        support_y = bottom - 1
        support_height = max(12, round((bottom - top) * 0.08))
        support_box = (left, max(top, bottom - support_height), right, bottom)
        support = weighted_centroid(image, support_box)
        if support:
            support_x = round(support[0], 2)

        torso_top = top + round((bottom - top) * 0.32)
        torso_bottom = top + round((bottom - top) * 0.72)
        torso = weighted_centroid(image, (left, torso_top, right, max(torso_top + 1, torso_bottom)))
        if torso:
            torso_x, torso_y = round(torso[0], 2), round(torso[1], 2)

    return FrameMetrics(
        file=path.relative_to(root).as_posix(),
        duration_ms=duration,
        width=image.width,
        height=image.height,
        blank=bbox is None,
        bbox=list(bbox) if bbox else None,
        margins=margins,
        support_x=support_x,
        support_y=support_y,
        torso_x=torso_x,
        torso_y=torso_y,
        opaque_pixels=opaque,
        transparent_rgb_pixels=residue,
        alpha_component_count=len(component_sizes),
        significant_component_count=significant_components,
        detached_area_ratio=detached_area_ratio,
        stray_alpha_pixels=stray_alpha_pixels,
        sha256=hashlib.sha256(raw).hexdigest(),
    )


def spread(values: list[float | int | None]) -> float:
    usable = [float(value) for value in values if value is not None]
    return round(max(usable) - min(usable), 2) if usable else 0.0


def stdev(values: list[float | int | None]) -> float:
    usable = [float(value) for value in values if value is not None]
    return round(statistics.pstdev(usable), 2) if len(usable) > 1 else 0.0


def motion_profile(
    paths: list[Path],
    durations: list[int],
    close_loop: bool,
) -> list[dict[str, float | int]]:
    if len(paths) < 2:
        return []
    samples = []
    for path in paths:
        with Image.open(path) as opened:
            sample = opened.convert("RGBA").resize((64, 64), Image.Resampling.LANCZOS)
        samples.append(np.asarray(sample, dtype=np.float32))
    pair_count = len(samples) if close_loop else len(samples) - 1
    profile = []
    for index in range(pair_count):
        next_index = (index + 1) % len(samples)
        delta = round(float(np.abs(samples[next_index] - samples[index]).mean()), 3)
        interval_ms = max(1, durations[index] if index < len(durations) else 1)
        profile.append({
            "fromFrame": index,
            "toFrame": next_index,
            "intervalMs": interval_ms,
            "visualDelta": delta,
            "motionRate": round(delta * 1000.0 / interval_ms, 3),
        })
    return profile


def isolated_motion_spikes(profile: list[dict[str, float | int]]) -> list[int]:
    """Find a lone fast transition surrounded by substantially slower motion."""
    rates = [float(item["motionRate"]) for item in profile]
    usable = [rate for item, rate in zip(profile, rates) if float(item["visualDelta"]) >= 0.05]
    if len(rates) < 3 or not usable:
        return []
    median_rate = statistics.median(usable)
    threshold = max(40.0, median_rate * 2.5)
    return [
        index
        for index in range(1, len(rates) - 1)
        if rates[index] > threshold
        and rates[index - 1] < rates[index] * 0.55
        and rates[index + 1] < rates[index] * 0.55
    ]


def visual_delta(left: Path, right: Path) -> float:
    samples = []
    for path in (left, right):
        with Image.open(path) as opened:
            sample = opened.convert("RGBA").resize((64, 64), Image.Resampling.LANCZOS)
        samples.append(np.asarray(sample, dtype=np.float32))
    return round(float(np.abs(samples[1] - samples[0]).mean()), 3)


def foreground_palette(path: Path, bins: int = PALETTE_BINS) -> np.ndarray:
    with Image.open(path) as opened:
        rgba = np.asarray(opened.convert("RGBA"), dtype=np.uint8)
    mask = rgba[:, :, 3] > 32
    if not np.any(mask):
        return np.zeros(bins ** 3, dtype=np.float64)
    rgb = rgba[:, :, :3][mask]
    weights = rgba[:, :, 3][mask].astype(np.float64) / 255.0
    quantized = np.minimum(bins - 1, rgb.astype(np.int32) * bins // 256)
    indices = quantized[:, 0] * bins * bins + quantized[:, 1] * bins + quantized[:, 2]
    histogram = np.bincount(indices, weights=weights, minlength=bins ** 3).astype(np.float64)
    total = float(histogram.sum())
    return histogram / total if total else histogram


def palette_similarity(histogram: np.ndarray, reference: np.ndarray) -> float:
    return round(float(np.minimum(histogram, reference).sum()), 4)


def allowed_palette_bins(reference: np.ndarray, bins: int = PALETTE_BINS) -> np.ndarray:
    allowed = np.zeros(bins ** 3, dtype=bool)
    occupied = np.flatnonzero(reference >= 0.0005)
    for index in occupied:
        red = index // (bins * bins)
        green = (index // bins) % bins
        blue = index % bins
        for rr in range(max(0, red - 1), min(bins, red + 2)):
            for gg in range(max(0, green - 1), min(bins, green + 2)):
                for bb in range(max(0, blue - 1), min(bins, blue + 2)):
                    allowed[rr * bins * bins + gg * bins + bb] = True
    return allowed


def make_contact_sheet(frames: list[Path], output: Path, title: str) -> None:
    thumb = 220
    label_height = 34
    columns = min(6, max(1, len(frames)))
    rows = math.ceil(len(frames) / columns)
    sheet = Image.new("RGBA", (columns * thumb, rows * (thumb + label_height) + 34), (232, 235, 242, 255))
    draw = ImageDraw.Draw(sheet)
    draw.text((10, 9), title, fill=(25, 28, 36, 255))
    for index, path in enumerate(frames):
        with Image.open(path) as opened:
            frame = opened.convert("RGBA")
        frame.thumbnail((thumb - 12, thumb - 12), Image.Resampling.LANCZOS)
        x = (index % columns) * thumb + (thumb - frame.width) // 2
        y = 34 + (index // columns) * (thumb + label_height) + (thumb - frame.height) // 2
        sheet.alpha_composite(frame, (x, y))
        draw.text(((index % columns) * thumb + 6, 34 + (index // columns) * (thumb + label_height) + thumb + 4), path.name, fill=(45, 48, 60, 255))
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(output, quality=92)


def make_gif(frames: list[Path], durations: list[int], output: Path) -> None:
    rendered: list[Image.Image] = []
    for path in frames:
        with Image.open(path) as opened:
            frame = opened.convert("RGBA")
        preview = Image.new("RGBA", (420, 420), (232, 235, 242, 255))
        frame.thumbnail((410, 410), Image.Resampling.LANCZOS)
        preview.alpha_composite(frame, ((420 - frame.width) // 2, (420 - frame.height) // 2))
        rendered.append(preview.convert("P", palette=Image.Palette.ADAPTIVE))
    if rendered:
        output.parent.mkdir(parents=True, exist_ok=True)
        rendered[0].save(output, save_all=True, append_images=rendered[1:], duration=[max(20, d) for d in durations], loop=0, disposal=2)


def make_full_action_gif(
    action_id: str,
    phases: list[tuple[str, list[Path], list[int]]],
    output: Path,
    contact_output: Path | None = None,
) -> None:
    """Render phase boundaries in one real-timing preview instead of isolated GIFs."""
    frames = [path for _, paths, _ in phases for path in paths]
    durations = [duration for _, _, durations in phases for duration in durations]
    if not frames:
        return

    rendered: list[Image.Image] = []
    elapsed = 0
    total = sum(durations)
    for path, duration in zip(frames, durations):
        with Image.open(path) as opened:
            frame = opened.convert("RGBA").resize((420, 420), Image.Resampling.LANCZOS)
        preview = Image.new("RGBA", (520, 470), (232, 235, 242, 255))
        preview.alpha_composite(frame, (50, 32))
        draw = ImageDraw.Draw(preview)
        draw.text((12, 10), f"{action_id}  {elapsed / 1000:.2f}s / {total / 1000:.2f}s", fill=(25, 28, 36, 255))
        rendered.append(preview.convert("P", palette=Image.Palette.ADAPTIVE))
        elapsed += duration

    output.parent.mkdir(parents=True, exist_ok=True)
    rendered[0].save(
        output,
        save_all=True,
        append_images=rendered[1:],
        duration=[max(20, duration) for duration in durations],
        loop=0,
        disposal=2,
    )
    if contact_output is not None:
        thumb_size = (260, 235)
        columns = 5
        rows = math.ceil(len(rendered) / columns)
        sheet = Image.new("RGB", (thumb_size[0] * columns, thumb_size[1] * rows), (232, 235, 242))
        for index, frame in enumerate(rendered):
            thumb = frame.convert("RGB").resize(thumb_size, Image.Resampling.LANCZOS)
            sheet.paste(thumb, ((index % columns) * thumb_size[0], (index // columns) * thumb_size[1]))
        contact_output.parent.mkdir(parents=True, exist_ok=True)
        sheet.save(contact_output, optimize=True)


def make_runtime_walk_gif(
    action_id: str,
    phases: list[tuple[str, list[Path], list[int]]],
    output: Path,
    contact_output: Path,
    direction: int,
    terminal_idle: Path | None = None,
    window_size: int = 260,
    distance: int = 180,
    loop_cycles: int = 2,
) -> None:
    """Simulate the WPF window's smoothstep travel together with the exact frame timing."""
    frames: list[Path] = []
    durations: list[int] = []
    for phase_kind, phase_frames, phase_durations in phases:
        repeats = loop_cycles if phase_kind == "loop" else 1
        for _ in range(repeats):
            frames.extend(phase_frames)
            durations.extend(phase_durations)
    if not frames:
        return

    total = sum(durations)
    scene_width = window_size + distance + 100
    scene_height = window_size + 72
    start_x = 50 + (distance if direction < 0 else 0)
    rendered: list[Image.Image] = []
    elapsed = 0
    for path, duration in zip(frames, durations):
        progress = min(1.0, elapsed / max(1, total))
        eased = progress * progress * (3 - 2 * progress)
        window_x = round(start_x + direction * distance * eased)
        with Image.open(path) as opened:
            frame = opened.convert("RGBA").resize((window_size, window_size), Image.Resampling.LANCZOS)

        preview = Image.new("RGBA", (scene_width, scene_height), (238, 240, 245, 255))
        draw = ImageDraw.Draw(preview)
        ground_y = 38 + round(window_size * 0.945)
        draw.line((18, ground_y, scene_width - 18, ground_y), fill=(128, 134, 146, 255), width=1)
        draw.rectangle(
            (window_x, 38, window_x + window_size - 1, 38 + window_size - 1),
            outline=(177, 183, 194, 255),
            width=1,
        )
        preview.alpha_composite(frame, (window_x, 38))
        draw.text(
            (12, 10),
            f"{action_id}  {elapsed / 1000:.2f}s  window x={window_x}",
            fill=(25, 28, 36, 255),
        )
        rendered.append(preview.convert("P", palette=Image.Palette.ADAPTIVE))
        elapsed += duration

    if terminal_idle is not None:
        window_x = round(start_x + direction * distance)
        with Image.open(terminal_idle) as opened:
            frame = opened.convert("RGBA").resize((window_size, window_size), Image.Resampling.LANCZOS)
        preview = Image.new("RGBA", (scene_width, scene_height), (238, 240, 245, 255))
        draw = ImageDraw.Draw(preview)
        ground_y = 38 + round(window_size * 0.945)
        draw.line((18, ground_y, scene_width - 18, ground_y), fill=(128, 134, 146, 255), width=1)
        draw.rectangle(
            (window_x, 38, window_x + window_size - 1, 38 + window_size - 1),
            outline=(177, 183, 194, 255),
            width=1,
        )
        preview.alpha_composite(frame, (window_x, 38))
        draw.text(
            (12, 10),
            f"{action_id} -> idle  {total / 1000:.2f}s  window x={window_x}",
            fill=(25, 28, 36, 255),
        )
        rendered.append(preview.convert("P", palette=Image.Palette.ADAPTIVE))
        durations.append(500)

    output.parent.mkdir(parents=True, exist_ok=True)
    rendered[0].save(
        output,
        save_all=True,
        append_images=rendered[1:],
        duration=[max(20, duration) for duration in durations],
        loop=0,
        disposal=2,
    )
    columns = 4
    rows = math.ceil(len(rendered) / columns)
    sheet = Image.new(
        "RGB",
        (scene_width * columns, scene_height * rows),
        (238, 240, 245),
    )
    for index, frame in enumerate(rendered):
        sheet.paste(frame.convert("RGB"), ((index % columns) * scene_width, (index // columns) * scene_height))
    contact_output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(contact_output, optimize=True)


def make_overview(rows: list[tuple[str, str, list[Path], int]], output: Path) -> None:
    tile = 112
    label_width = 190
    maximum_frames = max((len(paths) for _, _, paths, _ in rows), default=1)
    sheet = Image.new("RGB", (label_width + maximum_frames * tile, max(1, len(rows)) * tile), "#e1e5ec")
    draw = ImageDraw.Draw(sheet)
    for row, (action, phase, paths, duration) in enumerate(rows):
        y = row * tile
        draw.text((8, y + 34), f"{action}\n{phase} · {duration}ms", fill="#20242d")
        for column, path in enumerate(paths):
            with Image.open(path) as opened:
                frame = opened.convert("RGBA").resize((tile, tile), Image.Resampling.LANCZOS)
            cell = Image.new("RGBA", (tile, tile), "#f7f8fa")
            cell.alpha_composite(frame)
            sheet.paste(cell.convert("RGB"), (label_width + column * tile, y))
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output, optimize=True)


def make_palette_outlier_sheet(
    items: list[tuple[float, Path, str, str]],
    allowed: np.ndarray,
    output: Path,
    bins: int = PALETTE_BINS,
) -> None:
    selected = sorted(items, key=lambda item: item[0])[:12]
    tile = 250
    label_height = 44
    columns = 4
    rows = math.ceil(len(selected) / columns)
    sheet = Image.new("RGB", (tile * columns, (tile + label_height) * rows), "#e8ebf1")
    draw = ImageDraw.Draw(sheet)
    for index, (coverage, path, action, phase) in enumerate(selected):
        with Image.open(path) as opened:
            rgba = np.asarray(opened.convert("RGBA"), dtype=np.uint8).copy()
        foreground = rgba[:, :, 3] > 32
        quantized = np.minimum(bins - 1, rgba[:, :, :3].astype(np.int32) * bins // 256)
        indices = quantized[:, :, 0] * bins * bins + quantized[:, :, 1] * bins + quantized[:, :, 2]
        outside = foreground & ~allowed[indices]
        overlay = rgba.copy()
        overlay[outside, :3] = np.array([255, 38, 72], dtype=np.uint8)
        preview = Image.fromarray(overlay, "RGBA").resize((tile, tile), Image.Resampling.LANCZOS)
        background = Image.new("RGBA", (tile, tile), "#f7f8fa")
        background.alpha_composite(preview)
        x = (index % columns) * tile
        y = (index // columns) * (tile + label_height)
        sheet.paste(background.convert("RGB"), (x, y))
        draw.text((x + 6, y + tile + 4), f"{action}/{phase}  coverage={coverage:.3f}\n{path.name}", fill="#262a33")
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output, optimize=True)


def make_component_outlier_sheet(
    items: list[tuple[float, int, int, int, Path, str, str]],
    output: Path,
) -> None:
    """Mark every component except the largest body component in red."""
    selected = sorted(items, key=lambda item: (item[0], item[1], item[2], item[3]), reverse=True)[:12]
    tile = 250
    label_height = 66
    columns = 4
    rows = max(1, math.ceil(len(selected) / columns))
    sheet = Image.new("RGB", (tile * columns, (tile + label_height) * rows), "#e8ebf1")
    draw = ImageDraw.Draw(sheet)
    for index, (detached_ratio, significant_count, component_count, stray_count, path, action, phase) in enumerate(selected):
        with Image.open(path) as opened:
            image = opened.convert("RGBA")
        _, labels, largest_label = alpha_component_analysis(image)
        rgba = np.asarray(image, dtype=np.uint8).copy()
        detached_sample = (labels >= 0) if largest_label is None else ((labels >= 0) & (labels != largest_label))
        detached_mask = Image.fromarray((detached_sample.astype(np.uint8) * 255), "L").resize(
            image.size,
            Image.Resampling.NEAREST,
        )
        detached = np.asarray(detached_mask, dtype=np.uint8) > 0
        overlay = rgba.copy()
        overlay[detached & (rgba[:, :, 3] > 0), :3] = np.array([255, 38, 72], dtype=np.uint8)
        stray = stray_alpha_mask(rgba[:, :, 3])
        stray_preview = Image.fromarray((stray.astype(np.uint8) * 255), "L").filter(
            ImageFilter.MaxFilter(9),
        )
        stray_highlight = np.asarray(stray_preview, dtype=np.uint8) > 0
        overlay[stray_highlight, :3] = np.array([255, 210, 0], dtype=np.uint8)
        overlay[stray_highlight, 3] = 255
        preview = Image.fromarray(overlay, "RGBA").resize((tile, tile), Image.Resampling.LANCZOS)
        background = Image.new("RGBA", (tile, tile), "#f7f8fa")
        background.alpha_composite(preview)
        x = (index % columns) * tile
        y = (index // columns) * (tile + label_height)
        sheet.paste(background.convert("RGB"), (x, y))
        draw.text(
            (x + 6, y + tile + 4),
            f"{action}/{phase}  {path.name}\n"
            f"detached={detached_ratio:.4f}, components={component_count}\n"
            f"significant={significant_count}, stray={stray_count}",
            fill="#262a33",
        )
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output, optimize=True)


def make_motion_rhythm_sheet(
    items: list[tuple[float, str, str, list[Path], list[dict[str, float | int]]]],
    output: Path,
) -> None:
    selected = sorted(items, key=lambda item: item[0], reverse=True)[:12]
    label_width = 190
    slot_width = 125
    frame_size = 96
    row_height = 152
    maximum_frames = max((len(paths) for _, _, _, paths, _ in selected), default=1)
    sheet = Image.new(
        "RGB",
        (label_width + maximum_frames * slot_width, max(1, len(selected)) * row_height),
        "#e8ebf1",
    )
    draw = ImageDraw.Draw(sheet)
    for row, (ratio, action, phase, paths, profile) in enumerate(selected):
        y = row * row_height
        rates = [float(item["motionRate"]) for item in profile if float(item["visualDelta"]) >= 0.05]
        median_rate = statistics.median(rates) if rates else 0.0
        draw.text(
            (8, y + 12),
            f"{action} / {phase}\nrate max/median={ratio:.2f}\nmedian={median_rate:.2f}",
            fill="#20242d",
        )
        for column, path in enumerate(paths):
            with Image.open(path) as opened:
                frame = opened.convert("RGBA").resize((frame_size, frame_size), Image.Resampling.LANCZOS)
            cell = Image.new("RGBA", (frame_size, frame_size), "#f7f8fa")
            cell.alpha_composite(frame)
            x = label_width + column * slot_width
            sheet.paste(cell.convert("RGB"), (x, y))
            draw.text((x + 2, y + frame_size + 2), f"F{column}  {path.stem.rsplit('_', 1)[-1]}ms", fill="#30343d")
            if column < len(profile):
                item = profile[column]
                rate = float(item["motionRate"])
                color = "#d42f4d" if median_rate and rate > median_rate * 2.5 else "#364b73"
                draw.text(
                    (x + 2, y + frame_size + 19),
                    f"d={float(item['visualDelta']):.2f}\nrate={rate:.1f}",
                    fill=color,
                )
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output, optimize=True)


def make_minimum_display_margin_sheet(
    items: list[tuple[float, Path, str, str, list[float]]],
    output: Path,
    display_size: int,
) -> None:
    selected = sorted(items, key=lambda item: item[0])[:12]
    tile = 250
    label_height = 50
    columns = 4
    rows = max(1, math.ceil(len(selected) / columns))
    sheet = Image.new("RGB", (tile * columns, (tile + label_height) * rows), "#e8ebf1")
    draw = ImageDraw.Draw(sheet)
    for index, (minimum_margin, path, action, phase, margins) in enumerate(selected):
        with Image.open(path) as opened:
            frame = opened.convert("RGBA").resize((tile, tile), Image.Resampling.LANCZOS)
        background = Image.new("RGBA", (tile, tile), "#f7f8fa")
        background.alpha_composite(frame)
        x = (index % columns) * tile
        y = (index // columns) * (tile + label_height)
        sheet.paste(background.convert("RGB"), (x, y))
        draw.rectangle((x, y, x + tile - 1, y + tile - 1), outline="#d42f4d", width=1)
        draw.text(
            (x + 6, y + tile + 3),
            f"{action}/{phase}  {path.name}\n"
            f"{display_size}px margins={margins}; min={minimum_margin:.2f}px",
            fill="#262a33",
        )
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output, optimize=True)


def load_actions(pack_root: Path, manifest: dict[str, Any]) -> list[tuple[str, str, list[Path]]]:
    result = []
    for action in manifest.get("actions", []):
        action_id = action["id"]
        for phase in action.get("phases", []):
            folder = (pack_root / phase["folder"]).resolve()
            if pack_root.resolve() not in folder.parents:
                raise ValueError(f"Path escapes pack root: {phase['folder']}")
            frames = sorted(folder.glob("*.png"))
            result.append((action_id, phase["kind"], frames))
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("pack", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    pack_root = args.pack.resolve()
    output = (args.output or pack_root / "qa").resolve()
    manifest = json.loads((pack_root / "pet.json").read_text(encoding="utf-8-sig"))
    canvas = manifest["canvas"]
    expected_size = (canvas["width"], canvas["height"])
    source_margin_error = min(expected_size) * 0.012
    source_margin_target = min(expected_size) * 0.04
    minimum_display_size = int(manifest.get("display", {}).get("minimumSize", min(expected_size)))
    declared_actions = [action["id"] for action in manifest.get("actions", [])]
    missing_actions = [action for action in REQUIRED_ACTIONS if action not in declared_actions]

    loaded_phases = load_actions(pack_root, manifest)
    idle_paths = next(
        (paths for action, phase, paths in loaded_phases if action == "idle" and phase == "loop"),
        [],
    )
    idle_palettes = [foreground_palette(path) for path in idle_paths]
    palette_reference = np.mean(idle_palettes, axis=0) if idle_palettes else np.zeros(PALETTE_BINS ** 3)
    palette_reference /= max(float(palette_reference.sum()), 1e-12)
    palette_allowed = allowed_palette_bins(palette_reference)

    report: dict[str, Any] = {
        "pack": manifest.get("id"),
        "canvas": canvas,
        "declaredActions": declared_actions,
        "requiredLogicalActions": REQUIRED_ACTIONS,
        "missingActions": missing_actions,
        "phases": [],
        "phaseTransitions": [],
        "idleEntries": [],
        "idleReturns": [],
        "interactionTransitions": [],
        "paletteReference": "mean foreground RGB histogram of idle loop",
        "componentAnalysis": {
            "sampleSize": COMPONENT_SAMPLE_SIZE,
            "connectivity": 8,
            "alphaThreshold": 8,
            "significantComponentPixels": SIGNIFICANT_COMPONENT_PIXELS,
            "strayPixelSeparationRadius": 4,
        },
        "minimumDisplaySize": minimum_display_size,
        "minimumRenderedMarginTargetPx": 5.0,
        "sourceMarginErrorRatio": 0.012,
        "sourceMarginTargetRatio": 0.04,
        "issues": [],
    }
    all_frames = 0
    errors = 0
    overview_rows: list[tuple[str, str, list[Path], int]] = []
    action_previews: dict[str, list[tuple[str, list[Path], list[int]]]] = {}
    palette_frames: list[tuple[float, Path, str, str]] = []
    component_frames: list[tuple[float, int, int, int, Path, str, str]] = []
    rhythm_items: list[tuple[float, str, str, list[Path], list[dict[str, float | int]]]] = []
    margin_frames: list[tuple[float, Path, str, str, list[float]]] = []
    for action_id, phase_kind, paths in loaded_phases:
        metrics = [inspect_frame(path, pack_root) for path in paths]
        similarities = []
        coverages = []
        for path, metric in zip(paths, metrics):
            if metric.margins:
                metric.minimum_display_margins = [
                    round(metric.margins[0] * minimum_display_size / expected_size[0], 2),
                    round(metric.margins[1] * minimum_display_size / expected_size[1], 2),
                    round(metric.margins[2] * minimum_display_size / expected_size[0], 2),
                    round(metric.margins[3] * minimum_display_size / expected_size[1], 2),
                ]
                margin_frames.append((
                    min(metric.minimum_display_margins),
                    path,
                    action_id,
                    phase_kind,
                    metric.minimum_display_margins,
                ))
            histogram = foreground_palette(path)
            similarity = palette_similarity(histogram, palette_reference)
            coverage = round(float(histogram[palette_allowed].sum()), 4)
            metric.palette_similarity = similarity
            metric.palette_coverage = coverage
            similarities.append(similarity)
            coverages.append(coverage)
            palette_frames.append((coverage, path, action_id, phase_kind))
            component_frames.append((
                metric.detached_area_ratio,
                metric.significant_component_count,
                metric.alpha_component_count,
                metric.stray_alpha_pixels,
                path,
                action_id,
                phase_kind,
            ))
        action_previews.setdefault(action_id, []).append(
            (phase_kind, paths, [metric.duration_ms for metric in metrics])
        )
        all_frames += len(metrics)
        widths = [(m.bbox[2] - m.bbox[0]) if m.bbox else None for m in metrics]
        heights = [(m.bbox[3] - m.bbox[1]) if m.bbox else None for m in metrics]
        profile = motion_profile(
            paths,
            [m.duration_ms for m in metrics],
            close_loop=phase_kind == "loop",
        )
        deltas = [float(item["visualDelta"]) for item in profile]
        rates = [float(item["motionRate"]) for item in profile if float(item["visualDelta"]) >= 0.05]
        median_rate = statistics.median(rates) if rates else 0.0
        motion_spikes = isolated_motion_spikes(profile)
        phase = {
            "action": action_id,
            "phase": phase_kind,
            "frameCount": len(metrics),
            "totalDurationMs": sum(m.duration_ms for m in metrics),
            "supportXSpread": spread([m.support_x for m in metrics]),
            "supportYSpread": spread([m.support_y for m in metrics]),
            "torsoXSpread": spread([m.torso_x for m in metrics]),
            "torsoXStdev": stdev([m.torso_x for m in metrics]),
            "bboxWidthSpread": spread(widths),
            "bboxHeightSpread": spread(heights),
            "meanFrameDelta": round(statistics.mean(deltas), 3) if deltas else 0.0,
            "maximumFrameDelta": max(deltas, default=0.0),
            "loopClosureDelta": deltas[-1] if phase_kind == "loop" and deltas else 0.0,
            "medianMotionRate": round(median_rate, 3),
            "maximumMotionRate": max(rates, default=0.0),
            "motionRateOutlierRatio": round(max(rates, default=0.0) / median_rate, 3) if median_rate else 0.0,
            "isolatedMotionSpikeCount": len(motion_spikes),
            "isolatedMotionSpikeTransitions": motion_spikes,
            "motionProfile": profile,
            "minimumPaletteSimilarity": min(similarities, default=0.0),
            "meanPaletteSimilarity": round(statistics.mean(similarities), 4) if similarities else 0.0,
            "minimumPaletteCoverage": min(coverages, default=0.0),
            "meanPaletteCoverage": round(statistics.mean(coverages), 4) if coverages else 0.0,
            "maximumAlphaComponentCount": max((m.alpha_component_count for m in metrics), default=0),
            "maximumSignificantComponentCount": max((m.significant_component_count for m in metrics), default=0),
            "maximumDetachedAreaRatio": max((m.detached_area_ratio for m in metrics), default=0.0),
            "maximumStrayAlphaPixels": max((m.stray_alpha_pixels for m in metrics), default=0),
            "minimumRenderedMarginAtMinimumSize": min(
                (min(m.minimum_display_margins) for m in metrics if m.minimum_display_margins),
                default=0.0,
            ),
            "frames": [asdict(m) for m in metrics],
        }
        overview_rows.append((action_id, phase_kind, paths, phase["totalDurationMs"]))
        rhythm_items.append((phase["motionRateOutlierRatio"], action_id, phase_kind, paths, profile))
        report["phases"].append(phase)

        if not paths:
            report["issues"].append({"severity": "error", "action": action_id, "phase": phase_kind, "message": "phase has no PNG frames"})
            errors += 1
            continue
        hashes: dict[str, list[str]] = {}
        for metric in metrics:
            hashes.setdefault(metric.sha256, []).append(metric.file)
            if (metric.width, metric.height) != expected_size:
                report["issues"].append({"severity": "error", "file": metric.file, "message": f"canvas is {metric.width}x{metric.height}, expected {expected_size[0]}x{expected_size[1]}"})
                errors += 1
            if metric.blank:
                report["issues"].append({"severity": "error", "file": metric.file, "message": "frame is blank"})
                errors += 1
            elif min(metric.margins or [0]) < source_margin_error:
                report["issues"].append({
                    "severity": "error",
                    "file": metric.file,
                    "message": f"foreground safety margin below 1.2% of canvas: {metric.margins}",
                })
                errors += 1
            elif min(metric.margins or [0]) < source_margin_target:
                report["issues"].append({
                    "severity": "warning",
                    "file": metric.file,
                    "message": f"foreground margin below 4% canvas product target: {metric.margins}",
                })
            if not 16 <= metric.duration_ms <= 5000:
                report["issues"].append({"severity": "error", "file": metric.file, "message": f"invalid or missing duration: {metric.duration_ms}ms"})
                errors += 1
            if metric.transparent_rgb_pixels:
                report["issues"].append({"severity": "info", "file": metric.file, "message": f"{metric.transparent_rgb_pixels} fully-transparent pixels retain RGB data"})
            rendered_margin = min(metric.minimum_display_margins or [0.0])
            if not metric.blank and rendered_margin < 4.0:
                report["issues"].append({
                    "severity": "error",
                    "file": metric.file,
                    "message": f"foreground margin falls below 4px at {minimum_display_size}px display size ({rendered_margin:.2f}px)",
                })
                errors += 1
            elif not metric.blank and rendered_margin < 5.0:
                report["issues"].append({
                    "severity": "warning",
                    "file": metric.file,
                    "message": f"foreground margin falls below 5px product target at {minimum_display_size}px ({rendered_margin:.2f}px)",
                })
            if metric.significant_component_count > 1 or metric.detached_area_ratio >= 0.001:
                report["issues"].append({
                    "severity": "error",
                    "file": metric.file,
                    "message": (
                        "foreground contains a detached component "
                        f"(components={metric.alpha_component_count}, "
                        f"significant={metric.significant_component_count}, "
                        f"detached={metric.detached_area_ratio:.4f})"
                    ),
                })
                errors += 1
            elif metric.alpha_component_count > 1:
                report["issues"].append({
                    "severity": "warning",
                    "file": metric.file,
                    "message": (
                        "foreground contains a tiny detached component "
                        f"(components={metric.alpha_component_count}, "
                        f"detached={metric.detached_area_ratio:.4f})"
                    ),
                })
            if metric.stray_alpha_pixels:
                report["issues"].append({
                    "severity": "error",
                    "file": metric.file,
                    "message": f"foreground contains {metric.stray_alpha_pixels} isolated alpha pixels",
                })
                errors += 1
            if (metric.palette_coverage or 0) < 0.90:
                report["issues"].append({
                    "severity": "error",
                    "file": metric.file,
                    "message": f"foreground palette coverage is too low ({metric.palette_coverage:.3f})",
                })
                errors += 1
            elif (metric.palette_coverage or 0) < 0.93:
                report["issues"].append({
                    "severity": "warning",
                    "file": metric.file,
                    "message": f"foreground palette coverage is below product target ({metric.palette_coverage:.3f})",
                })
        duplicate_groups = [files for files in hashes.values() if len(files) > 1]
        if duplicate_groups:
            report["issues"].append({"severity": "warning", "action": action_id, "phase": phase_kind, "message": "exact duplicate frames", "groups": duplicate_groups})
        if motion_spikes:
            report["issues"].append({
                "severity": "warning",
                "action": action_id,
                "phase": phase_kind,
                "message": f"isolated motion-rate spike at transitions {motion_spikes}",
            })
        if action_id == "idle" and phase_kind == "loop":
            if phase["totalDurationMs"] < 1800:
                report["issues"].append({"severity": "warning", "action": action_id, "phase": phase_kind, "message": f"idle loop is fast ({phase['totalDurationMs']}ms); target 2000-4500ms"})
            if phase["supportYSpread"] > 8 or phase["torsoXSpread"] > 10:
                report["issues"].append({"severity": "error", "action": action_id, "phase": phase_kind, "message": f"idle anchor drift exceeds limit (foot Y {phase['supportYSpread']}px, torso X {phase['torsoXSpread']}px)"})
                errors += 1
        if phase_kind == "loop" and len(paths) > 1:
            if phase["maximumFrameDelta"] < 0.20:
                report["issues"].append({"severity": "warning", "action": action_id, "phase": phase_kind, "message": "loop is visually inert at 64px motion sampling"})
            median_delta = statistics.median(deltas)
            if phase["loopClosureDelta"] > 5 and phase["loopClosureDelta"] > median_delta * 3:
                report["issues"].append({"severity": "warning", "action": action_id, "phase": phase_kind, "message": f"loop closure delta is unusually high ({phase['loopClosureDelta']:.2f})"})
        make_contact_sheet(paths, output / "contact-sheets" / f"{action_id}-{phase_kind}.png", f"{action_id} / {phase_kind}")
        make_gif(paths, [m.duration_ms for m in metrics], output / "gifs" / f"{action_id}-{phase_kind}.gif")

    make_overview(overview_rows, output / "all-frames.png")
    make_palette_outlier_sheet(
        palette_frames,
        palette_allowed,
        output / "palette-outliers.png",
    )
    make_component_outlier_sheet(
        component_frames,
        output / "component-outliers.png",
    )
    make_motion_rhythm_sheet(
        rhythm_items,
        output / "motion-rhythm-outliers.png",
    )
    make_minimum_display_margin_sheet(
        margin_frames,
        output / "minimum-display-margin-outliers.png",
        minimum_display_size,
    )
    for action_id, phases in action_previews.items():
        make_full_action_gif(action_id, phases, output / "full-gifs" / f"{action_id}.gif")
    if "walk-left" in action_previews:
        idle_terminal = action_previews.get("idle", [("loop", [], [])])[0][1]
        make_runtime_walk_gif(
            "walk-left",
            action_previews["walk-left"],
            output / "runtime-gifs" / "walk-left-window-motion.gif",
            output / "runtime-gifs" / "walk-left-window-motion.png",
            direction=-1,
            terminal_idle=idle_terminal[0] if idle_terminal else None,
        )
    if "walk-right" in action_previews:
        make_runtime_walk_gif(
            "walk-right",
            action_previews["walk-right"],
            output / "runtime-gifs" / "walk-right-window-motion.gif",
            output / "runtime-gifs" / "walk-right-window-motion.png",
            direction=1,
            terminal_idle=idle_terminal[0] if idle_terminal else None,
        )
    if "raise" in action_previews and "fall-land" in action_previews and idle_terminal:
        raise_phases = action_previews["raise"]
        raise_start = [phase for phase in raise_phases if phase[0] == "start"]
        raise_loop = [phase for phase in raise_phases if phase[0] == "loop"]
        drag_release = raise_start + raise_loop + raise_loop + action_previews["fall-land"] + [
            ("idle", [idle_terminal[0]], [500])
        ]
        make_full_action_gif(
            "drag-release",
            drag_release,
            output / "interaction-gifs" / "drag-release.gif",
            output / "interaction-gifs" / "drag-release.png",
        )
    by_action: dict[str, list[dict[str, Any]]] = {}
    for phase in report["phases"]:
        by_action.setdefault(phase["action"], []).append(phase)
    for action_id, phases in by_action.items():
        for before, after in zip(phases, phases[1:]):
            if not before["frames"] or not after["frames"]:
                continue
            left = before["frames"][-1]
            right = after["frames"][0]
            if not left["bbox"] or not right["bbox"]:
                continue
            left_height = left["bbox"][3] - left["bbox"][1]
            right_height = right["bbox"][3] - right["bbox"][1]
            height_change = abs(left_height - right_height) / max(left_height, right_height)
            torso_change = abs((left["torso_x"] or 0) - (right["torso_x"] or 0))
            support_change = abs((left["support_y"] or 0) - (right["support_y"] or 0))
            boundary_delta = visual_delta(pack_root / left["file"], pack_root / right["file"])
            neighboring_deltas = [
                before["meanFrameDelta"],
                after["meanFrameDelta"],
            ]
            neighboring_mean = max(0.001, statistics.mean(neighboring_deltas))
            report["phaseTransitions"].append({
                "action": action_id,
                "from": before["phase"],
                "to": after["phase"],
                "visualDelta": boundary_delta,
                "relativeToNeighborMean": round(boundary_delta / neighboring_mean, 2),
                "heightChangeRatio": round(height_change, 4),
                "torsoXChange": round(torso_change, 2),
                "supportYChange": round(support_change, 2),
            })
            if (
                height_change > MAX_TRANSITION_HEIGHT_RATIO
                or torso_change > MAX_TRANSITION_ANCHOR_SHIFT
                or support_change > MAX_TRANSITION_ANCHOR_SHIFT
            ):
                report["issues"].append({
                    "severity": "error",
                    "action": action_id,
                    "message": (
                        f"phase transition {before['phase']}->{after['phase']} pops "
                        f"(height {height_change:.1%}, torso X {torso_change:.1f}px, support Y {support_change:.1f}px)"
                    ),
                })
                errors += 1

    idle_phases = by_action.get("idle", [])
    idle_frame = idle_phases[0]["frames"][0] if idle_phases and idle_phases[0]["frames"] else None
    edge_actions = {action_id for action_id in by_action if action_id.startswith("edge-")}
    no_idle_return = {"idle", "raise", "shutdown"} | edge_actions
    if idle_frame:
        idle_path = pack_root / idle_frame["file"]
        # A raised pet deliberately switches from the upright idle silhouette
        # to a face-down horizontal hold pose, matching VPet's Raised_Static
        # contract. Its semantic orientation is validated explicitly below.
        no_idle_entry = {"idle", "startup", "fall-land", "raise"} | edge_actions
        for action_id, phases in by_action.items():
            if action_id in no_idle_entry or not phases or not phases[0]["frames"]:
                continue
            first = phases[0]["frames"][0]
            if not first["bbox"]:
                continue
            first_height = first["bbox"][3] - first["bbox"][1]
            idle_height = idle_frame["bbox"][3] - idle_frame["bbox"][1]
            height_change = abs(first_height - idle_height) / max(first_height, idle_height)
            torso_change = abs((first["torso_x"] or 0) - (idle_frame["torso_x"] or 0))
            support_change = abs((first["support_y"] or 0) - (idle_frame["support_y"] or 0))
            delta = visual_delta(idle_path, pack_root / first["file"])
            report["idleEntries"].append({
                "action": action_id,
                "visualDelta": delta,
                "heightChangeRatio": round(height_change, 4),
                "torsoXChange": round(torso_change, 2),
                "supportYChange": round(support_change, 2),
            })
            if (
                height_change > MAX_TRANSITION_HEIGHT_RATIO
                or torso_change > MAX_TRANSITION_ANCHOR_SHIFT
                or support_change > MAX_TRANSITION_ANCHOR_SHIFT
            ):
                report["issues"].append({
                    "severity": "error",
                    "action": action_id,
                    "message": (
                        f"idle->entry pops (height {height_change:.1%}, "
                        f"torso X {torso_change:.1f}px, support Y {support_change:.1f}px)"
                    ),
                })
                errors += 1

        for action_id, phases in by_action.items():
            if action_id in no_idle_return or not phases or not phases[-1]["frames"]:
                continue
            terminal = phases[-1]["frames"][-1]
            if not terminal["bbox"]:
                continue
            terminal_height = terminal["bbox"][3] - terminal["bbox"][1]
            idle_height = idle_frame["bbox"][3] - idle_frame["bbox"][1]
            height_change = abs(terminal_height - idle_height) / max(terminal_height, idle_height)
            torso_change = abs((terminal["torso_x"] or 0) - (idle_frame["torso_x"] or 0))
            support_change = abs((terminal["support_y"] or 0) - (idle_frame["support_y"] or 0))
            delta = visual_delta(pack_root / terminal["file"], idle_path)
            item = {
                "action": action_id,
                "visualDelta": delta,
                "heightChangeRatio": round(height_change, 4),
                "torsoXChange": round(torso_change, 2),
                "supportYChange": round(support_change, 2),
            }
            report["idleReturns"].append(item)
            if (
                height_change > MAX_TRANSITION_HEIGHT_RATIO
                or torso_change > MAX_TRANSITION_ANCHOR_SHIFT
                or support_change > MAX_TRANSITION_ANCHOR_SHIFT
            ):
                report["issues"].append({
                    "severity": "error",
                    "action": action_id,
                    "message": (
                        f"terminal->idle pops (height {height_change:.1%}, "
                        f"torso X {torso_change:.1f}px, support Y {support_change:.1f}px)"
                    ),
                })
                errors += 1

    if by_action.get("raise") and by_action.get("fall-land"):
        raised = by_action["raise"][-1]["frames"][-1]
        falling = by_action["fall-land"][0]["frames"][0]
        raised_height = raised["bbox"][3] - raised["bbox"][1]
        raised_width = raised["bbox"][2] - raised["bbox"][0]
        falling_height = falling["bbox"][3] - falling["bbox"][1]
        falling_width = falling["bbox"][2] - falling["bbox"][0]
        raised_aspect = raised_width / max(1, raised_height)
        falling_aspect = falling_width / max(1, falling_height)
        semantic_orientation_change = raised_aspect >= 1.05 and falling_aspect <= 0.8
        height_change = abs(raised_height - falling_height) / max(raised_height, falling_height)
        torso_change = abs((raised["torso_x"] or 0) - (falling["torso_x"] or 0))
        support_change = abs((raised["support_y"] or 0) - (falling["support_y"] or 0))
        transition = {
            "from": "raise",
            "to": "fall-land",
            "visualDelta": visual_delta(pack_root / raised["file"], pack_root / falling["file"]),
            "heightChangeRatio": round(height_change, 4),
            "torsoXChange": round(torso_change, 2),
            "supportYChange": round(support_change, 2),
            "raisedAspectRatio": round(raised_aspect, 4),
            "fallingAspectRatio": round(falling_aspect, 4),
            "semanticOrientationChange": semantic_orientation_change,
        }
        report["interactionTransitions"].append(transition)
        if not semantic_orientation_change:
            report["issues"].append({
                "severity": "error",
                "action": "raise->fall-land",
                "message": (
                    "raise must read as horizontal and fall-land as vertical "
                    f"(aspect {raised_aspect:.2f}->{falling_aspect:.2f})"
                ),
            })
            errors += 1
        elif (
            height_change > MAX_TRANSITION_HEIGHT_RATIO
            or torso_change > MAX_TRANSITION_ANCHOR_SHIFT
            or support_change > MAX_TRANSITION_ANCHOR_SHIFT
        ):
            # The orientation switch is intentional: on release, a horizontal
            # held body rotates into the vertical fall/land chain. Other action
            # transitions continue to use the strict generic pop thresholds.
            transition["orientationTransitionAccepted"] = True
    report["frameCount"] = all_frames
    report["errorCount"] = errors
    output.mkdir(parents=True, exist_ok=True)
    (output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    warning_count = sum(issue["severity"] == "warning" for issue in report["issues"])
    summary = [
        f"# {manifest.get('displayName', manifest.get('id'))} 动画 QA",
        "",
        f"- 动作声明：{len(declared_actions)}",
        f"- 帧数：{all_frames}",
        f"- 错误：{errors}",
        f"- 警告：{warning_count}",
        f"- 未覆盖目标动作：{', '.join(missing_actions) if missing_actions else '无'}",
        "",
        "## 高优先级问题",
        "",
    ]
    important = [issue for issue in report["issues"] if issue["severity"] in {"error", "warning"}]
    summary.extend(f"- [{issue['severity']}] {issue.get('file', issue.get('action', 'pack'))}: {issue['message']}" for issue in important[:80])
    if not important:
        summary.append("- 无")
    (output / "summary.md").write_text("\n".join(summary) + "\n", encoding="utf-8")
    print(f"QA complete: {all_frames} frames, {errors} errors, {warning_count} warnings -> {output}")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
