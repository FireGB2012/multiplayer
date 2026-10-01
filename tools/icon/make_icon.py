# Draws the launcher icon (PDA emblem: glowing hexagon with "MP" over deep ocean) -> icon.png (256) + icon.ico.
# python3 tools/icon/make_icon.py   (needs Pillow and DejaVu Sans Bold)
import math, os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

S = 1024  # draw big, scale down for smooth edges
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "src", "SubnauticaMP.Launcher", "Assets")
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
# rounded-square body with a deep-ocean vertical gradient
body = Image.new("RGBA", (S, S))
top, bottom = (0x11, 0x60, 0x7F), (0x06, 0x11, 0x1B)
px = body.load()
for y in range(S):
    c = lerp(top, bottom, (y / S) ** 0.8)
    for x in range(S):
        px[x, y] = c + (255,)
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=210, fill=255)
img.paste(body, (0, 0), mask)

# light rays
rays = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(rays)
for x0, w, a in [(520, 70, 60), (640, 40, 45), (760, 90, 35)]:
    d.polygon([(x0, 0), (x0 + w, 0), (x0 - 120 + w, S), (x0 - 260, S)], fill=(63, 224, 232, a))
rays = rays.filter(ImageFilter.GaussianBlur(18))
img = Image.alpha_composite(img, Image.composite(rays, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask))

# hexagon with glow
cx, cy, r = S / 2, S / 2 + 10, 330
hexpts = [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a))) for a in range(-90, 270, 60)]
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).line(hexpts + [hexpts[0]], fill=(63, 224, 232, 255), width=60, joint="curve")
glow = glow.filter(ImageFilter.GaussianBlur(40))
img = Image.alpha_composite(img, glow)
d = ImageDraw.Draw(img)
d.polygon(hexpts, fill=(6, 30, 44, 235))
d.line(hexpts + [hexpts[0]], fill=(63, 224, 232, 255), width=34, joint="curve")

# MP
font = ImageFont.truetype(FONT, 250)
text = "MP"
bbox = d.textbbox((0, 0), text, font=font)
tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
d.text((cx - tw / 2 - bbox[0], cy - th / 2 - bbox[1]), text, font=font, fill=(95, 240, 245, 255))

os.makedirs(OUT, exist_ok=True)
png = img.resize((256, 256), Image.LANCZOS)
png.save(os.path.join(OUT, "icon.png"))
img.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "icon.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("wrote icon.png + icon.ico")
