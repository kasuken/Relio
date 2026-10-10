"""Derive Relio.Web's logo and favicon files from the supplied brand crops.

The source is relio/relio-logo-primary.png (see relio/crop-manifest.json). Only rectangular crops,
downscaling and padding are applied; nothing is recoloured or redrawn. Alpha values of 12 or less
(a faint halo left around the artwork) are cleared so it cannot show on a dark page.

Requires Pillow in a development-only Python environment. Run from any directory:
    uv run --with pillow python assets/brand/generate-web-assets.py
"""

import json
from pathlib import Path

from PIL import Image

root = Path(__file__).resolve().parents[2]
source = root / "assets/brand/relio/relio-logo-primary.png"
web = root / "Relio.Web/wwwroot"
brand = web / "img/brand"
brand.mkdir(parents=True, exist_ok=True)

tokens = json.loads((root / "docs/design-system/tokens.json").read_text())
surface = next(token["value"]["light"] for token in tokens["color"]["tokens"] if token["name"] == "surface")

# Column ranges measured on relio-logo-primary.png: the mark, a fully transparent gap, the wordmark.
MARK_COLUMNS = (17, 258)
WORDMARK_COLUMNS = (301, 801)
ROWS = (14, 269)
DISPLAY_HEIGHT = 128


def clean(image):
    alpha = image.getchannel("A").point(lambda value: 0 if value <= 12 else value)
    image.putalpha(alpha)
    return image


def resize(image, size):
    # Resample with premultiplied alpha so transparent pixels do not bleed their colour into edges.
    return image.convert("RGBa").resize(size, Image.Resampling.LANCZOS).convert("RGBA")


def to_height(image, height):
    return resize(image, (round(image.width * height / image.height), height))


def square(image, padding):
    side = round(max(image.size) * (1 + 2 * padding))
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.alpha_composite(image, ((side - image.width) // 2, (side - image.height) // 2))
    return canvas


logo = clean(Image.open(source).convert("RGBA"))

# Mark and wordmark share the same rows, so set side by side at one height they line up exactly as
# in the original logo. The wordmark is used as a CSS mask, which lets it take the `text` token.
mark = logo.crop((MARK_COLUMNS[0], ROWS[0], MARK_COLUMNS[1], ROWS[1]))
wordmark = logo.crop((WORDMARK_COLUMNS[0], ROWS[0], WORDMARK_COLUMNS[1], ROWS[1]))
to_height(mark, DISPLAY_HEIGHT).save(brand / "relio-mark.png", optimize=True)
to_height(wordmark, DISPLAY_HEIGHT).save(brand / "relio-wordmark.png", optimize=True)

gap = WORDMARK_COLUMNS[0] - MARK_COLUMNS[1]
print(f"mark {mark.width}x{mark.height}, wordmark {wordmark.width}x{wordmark.height}, gap {gap}")

# Icons: the mark alone, trimmed to its own bounds and centred on a square.
tight = mark.crop(mark.getchannel("A").point(lambda value: 255 if value > 16 else 0).getbbox())
icon = square(tight, padding=0.03)
icon.save(web / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)])
resize(icon, (192, 192)).save(brand / "icon-192.png", optimize=True)
resize(icon, (512, 512)).save(brand / "icon-512.png", optimize=True)

# iOS fills transparency with black and rounds the corners itself, so the touch icon is a full-bleed
# square on the `surface` colour (the light app icon's tile) with the mark inset.
touch = Image.new("RGBA", (180, 180), surface)
inset = to_height(tight, 120)
touch.alpha_composite(inset, ((180 - inset.width) // 2, (180 - inset.height) // 2))
touch.convert("RGB").save(brand / "apple-touch-icon.png", optimize=True)
