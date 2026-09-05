from pathlib import Path

from PIL import Image, ImageDraw


VIOLET = (128, 85, 217, 255)
CHARCOAL = (37, 37, 42, 255)
OFF_WHITE = (250, 250, 252, 255)
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def render() -> Image.Image:
    image = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    # MU-family application tile.
    draw.rounded_rectangle((62, 62, 962, 962), radius=205, fill=CHARCOAL)
    draw.rounded_rectangle((84, 84, 940, 940), radius=183, fill=VIOLET)

    # A broad note remains legible in a 16 px title-bar slot.
    draw.rounded_rectangle((214, 214, 690, 820), radius=76, fill=CHARCOAL)
    draw.rounded_rectangle((238, 238, 666, 796), radius=56, fill=OFF_WHITE)
    for y, width in ((386, 300), (492, 260), (598, 218)):
        draw.rounded_rectangle((316, y, 316 + width, y + 34), radius=17, fill=VIOLET)

    # Clock badge: large, simple geometry instead of miniature UI detail.
    draw.ellipse((564, 178, 862, 476), fill=CHARCOAL)
    draw.ellipse((590, 204, 836, 450), fill=OFF_WHITE)
    draw.rounded_rectangle((700, 254, 724, 340), radius=12, fill=VIOLET)
    draw.polygon(((712, 326), (786, 372), (768, 400), (694, 350)), fill=VIOLET)
    draw.ellipse((690, 314, 734, 358), fill=CHARCOAL)
    return image


def main() -> None:
    project = Path(__file__).resolve().parents[1] / "src" / "PersonalTools.App"
    assets = project / "Assets"
    preview = assets / "ReminderNotes-icon-source.png"
    icon_path = assets / "ReminderNotes.ico"
    assets.mkdir(parents=True, exist_ok=True)

    master = render()
    master.save(preview)
    frames = [master.resize((size, size), Image.Resampling.LANCZOS) for size in SIZES]
    frames[-1].save(
        icon_path,
        format="ICO",
        sizes=[(size, size) for size in SIZES],
        append_images=frames[:-1],
    )


if __name__ == "__main__":
    main()
