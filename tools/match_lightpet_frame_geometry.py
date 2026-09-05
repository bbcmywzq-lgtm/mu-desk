"""Match repaired sprite scale and baseline to approved reference frames."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("repaired", type=Path)
    parser.add_argument("reference", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    repaired_paths = sorted(args.repaired.glob("*.png"))
    reference_paths = sorted(args.reference.glob("*.png"))
    if len(repaired_paths) != len(reference_paths) or not repaired_paths:
        raise ValueError("repaired and reference frame counts must match")

    args.output.mkdir(parents=True, exist_ok=True)
    for repaired_path, reference_path in zip(repaired_paths, reference_paths):
        repaired = Image.open(repaired_path).convert("RGBA")
        reference = Image.open(reference_path).convert("RGBA")
        repaired_bbox = repaired.getchannel("A").getbbox()
        reference_bbox = reference.getchannel("A").getbbox()
        if repaired_bbox is None or reference_bbox is None:
            raise ValueError("blank repaired or reference frame")

        crop = repaired.crop(repaired_bbox)
        target_height = reference_bbox[3] - reference_bbox[1]
        scale = target_height / crop.height
        target_size = (max(1, round(crop.width * scale)), target_height)
        crop = crop.resize(target_size, Image.Resampling.LANCZOS)

        canvas = Image.new("RGBA", reference.size, (0, 0, 0, 0))
        x = (canvas.width - crop.width) // 2
        y = reference_bbox[3] - crop.height
        if x < 0 or y < 0 or x + crop.width > canvas.width or y + crop.height > canvas.height:
            raise ValueError(f"matched frame would clip: {repaired_path.name}")
        canvas.alpha_composite(crop, (x, y))

        output_path = args.output / repaired_path.name
        canvas.save(output_path, optimize=True)
        bbox = canvas.getchannel("A").getbbox()
        assert bbox is not None
        margins = (bbox[0], bbox[1], canvas.width - bbox[2], canvas.height - bbox[3])
        print(
            f"{output_path.name}: scale={scale:.4f} bbox={bbox} "
            f"margins={margins}"
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
