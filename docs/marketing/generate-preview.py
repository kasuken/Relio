"""Regenerate the local social preview using Relio's tokens and self-hosted fonts.

Requires Pillow, fonttools and brotli in a development-only Python environment.
Run from any directory: python docs/marketing/generate-preview.py
"""

import io
import json
from pathlib import Path

from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
tokens = json.loads((root / "docs/design-system/tokens.json").read_text())
colors = {token["name"]: token["value"]["light"] for token in tokens["color"]["tokens"]
          if isinstance(token["value"], dict)}


def font(name, size):
    source = TTFont(root / "Relio.Web/wwwroot/fonts" / name)
    source.flavor = None
    buffer = io.BytesIO()
    source.save(buffer)
    buffer.seek(0)
    return ImageFont.truetype(buffer, size)


image = Image.new("RGB", (1200, 630), colors["paper"])
draw = ImageDraw.Draw(image)
draw.line((72, 480, 1128, 480), fill=colors["line-strong"], width=2)
draw.text((72, 60), "Relio", font=font("Alegreya-Roman-latin.woff2", 100), fill=colors["pen"])
sans = font("HankenGrotesk-Roman-latin.woff2", 48)
draw.text((72, 245), "A private notebook for", font=sans, fill=colors["text"])
draw.text((72, 307), "the people in your life.", font=sans, fill=colors["text"])
draw.text((72, 520), "People. Conversations. Follow-ups.", font=font("HankenGrotesk-Roman-latin.woff2", 30),
          fill=colors["text-muted"])
target = root / "Relio.Web/wwwroot/img/og-preview.png"
target.parent.mkdir(parents=True, exist_ok=True)
image.save(target, optimize=True)

# A fictional illustration, not a screenshot or a live account.
thread = Image.new("RGB", (960, 680), colors["surface"])
draw = ImageDraw.Draw(thread)
sans = font("HankenGrotesk-Roman-latin.woff2", 26)
serif = font("Alegreya-Roman-latin.woff2", 32)
draw.text((56, 38), "Morgan Silva", font=font("Alegreya-Roman-latin.woff2", 48), fill=colors["text"])
draw.text((56, 108), "Fictional example", font=sans, fill=colors["text-muted"])
draw.line((80, 200, 80, 586), fill=colors["line-strong"], width=2)
for y, label, lines, marker in [
    (205, "Log an interaction", ["Talked about the new garden.", "The first tomatoes are growing."], "pen"),
    (354, "Add a note", ["A favourite book:", "The Secret Garden."], "pencil"),
    (503, "Keep a follow-up", ["Ask how the garden", "is growing."], "pen"),
]:
    if marker == "pencil":
        draw.rectangle((70, y + 6, 90, y + 26), fill=colors["pencil-mark"])
    else:
        draw.ellipse((70, y + 6, 90, y + 26), fill=colors["pen"])
    draw.text((116, y), label, font=sans, fill=colors["text-muted"])
    for index, line in enumerate(lines):
        draw.text((116, y + 44 + index * 38), line, font=serif, fill=colors["text"])
thread.save(target.with_name("relationship-thread.png"), optimize=True)
source = root / "docs/marketing/screenshots/relationship-thread.png"
source.parent.mkdir(parents=True, exist_ok=True)
thread.save(source)
