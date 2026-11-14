"""Regenerate the ICO and PNG from the same geometric design as Assets/app.svg."""
from pathlib import Path
from PIL import Image, ImageDraw

assets = Path(__file__).resolve().parents[1] / "Assets"
scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale))
draw = ImageDraw.Draw(image)

def box(coords):
    return tuple(value * scale for value in coords)

draw.rounded_rectangle(box((0, 0, 255, 255)), radius=54 * scale, fill="#0f1113")
draw.rounded_rectangle(box((28, 67, 228, 163)), radius=17 * scale, outline="#f2f2f2", width=5 * scale)
for x in (42, 85, 128):
    draw.rounded_rectangle(box((x, 91, x + 32, 133)), radius=5 * scale, fill="#f2f2f2")
draw.ellipse(box((175, 94, 211, 130)), outline="#f2f2f2", width=5 * scale)
for line in ((193, 94, 193, 106), (42, 178, 214, 178), (52, 194, 204, 194)):
    draw.line(box(line), fill="#f2f2f2", width=5 * scale)
image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(assets / "app.png")
image.save(assets / "app.ico", sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
