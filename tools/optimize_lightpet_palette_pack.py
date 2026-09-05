from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Quantize a published LightPet pack to transparent palette PNGs with quality gates."
    )
    parser.add_argument("pack", type=Path)
    parser.add_argument("--colors", type=int, default=256)
    parser.add_argument("--minimum-psnr", type=float, default=40.0)
    parser.add_argument("--maximum-frame-mae", type=float, default=1.0)
    parser.add_argument("--maximum-pixel-error", type=float, default=80.0)
    parser.add_argument("--report", type=Path)
    return parser.parse_args()


def premultiplied_rgba(image: Image.Image) -> np.ndarray:
    rgba = np.asarray(image.convert("RGBA"), dtype=np.float32)
    alpha = rgba[:, :, 3:4] / 255.0
    return np.concatenate((rgba[:, :, :3] * alpha, rgba[:, :, 3:4]), axis=2)


def main() -> None:
    args = parse_args()
    pack = args.pack.resolve()
    if not pack.is_dir() or not (pack / "pet.json").is_file():
        raise SystemExit(f"Not a LightPet pack: {pack}")
    if not 16 <= args.colors <= 256:
        raise SystemExit("--colors must be between 16 and 256")

    png_paths = sorted(pack.rglob("*.png"))
    if not png_paths:
        raise SystemExit(f"No PNG frames found: {pack}")

    rows: list[dict[str, object]] = []
    temporary_paths: list[tuple[Path, Path]] = []
    try:
        for path in png_paths:
            original = Image.open(path).convert("RGBA")
            quantized = original.quantize(
                colors=args.colors,
                method=Image.Quantize.FASTOCTREE,
                dither=Image.Dither.NONE,
            )
            temporary = path.with_name(path.name + ".palette.tmp.png")
            quantized.save(temporary, format="PNG", optimize=True)
            restored = Image.open(temporary).convert("RGBA")
            before = premultiplied_rgba(original)
            after = premultiplied_rgba(restored)
            difference = np.abs(before - after)
            mse = float(np.mean((before - after) ** 2))
            rows.append(
                {
                    "path": path.relative_to(pack).as_posix(),
                    "sourceBytes": path.stat().st_size,
                    "targetBytes": temporary.stat().st_size,
                    "meanPremultipliedRgbaMae": round(float(np.mean(difference)), 6),
                    "maximumPremultipliedRgbaError": round(float(np.max(difference)), 6),
                    "psnrDb": round(20 * math.log10(255.0 / math.sqrt(mse)), 6) if mse else None,
                }
            )
            temporary_paths.append((path, temporary))

        source_bytes = sum(int(row["sourceBytes"]) for row in rows)
        target_bytes = sum(int(row["targetBytes"]) for row in rows)
        maximum_frame_mae = float(np.max([row["meanPremultipliedRgbaMae"] for row in rows]))
        minimum_psnr = float(np.min([row["psnrDb"] for row in rows if row["psnrDb"] is not None]))
        maximum_pixel_error = float(np.max([row["maximumPremultipliedRgbaError"] for row in rows]))
        summary = {
            "colors": args.colors,
            "frames": len(rows),
            "sourceBytes": source_bytes,
            "targetBytes": target_bytes,
            "reductionPercent": round((1 - target_bytes / source_bytes) * 100, 3),
            "meanFrameMae": round(float(np.mean([row["meanPremultipliedRgbaMae"] for row in rows])), 6),
            "maximumFrameMae": round(maximum_frame_mae, 6),
            "minimumPsnrDb": round(minimum_psnr, 6),
            "maximumPixelError": round(maximum_pixel_error, 6),
            "passed": (
                minimum_psnr >= args.minimum_psnr
                and maximum_frame_mae <= args.maximum_frame_mae
                and maximum_pixel_error <= args.maximum_pixel_error
            ),
            "thresholds": {
                "minimumPsnrDb": args.minimum_psnr,
                "maximumFrameMae": args.maximum_frame_mae,
                "maximumPixelError": args.maximum_pixel_error,
            },
        }
        payload = {"summary": summary, "frames": rows}
        if args.report:
            report = args.report.resolve()
            report.parent.mkdir(parents=True, exist_ok=True)
            report.write_text(json.dumps(payload, indent=2), encoding="utf-8")
        if not summary["passed"]:
            raise SystemExit(f"Palette quality gate failed: {json.dumps(summary)}")

        for destination, temporary in temporary_paths:
            temporary.replace(destination)
        temporary_paths.clear()
        print(json.dumps(summary))
    finally:
        for _, temporary in temporary_paths:
            temporary.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
