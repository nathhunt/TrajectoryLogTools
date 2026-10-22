# make_icons.py - draws the two app icons as multi-size .ico files (and PNG previews):
#   python3 scripts/make_icons.py <out folder>   -> TrajectoryLogTools.ico, TrajectoryLogConverter.ico
# then copy them to src/TrajectoryLogTools.App/app.ico and src/TrajectoryLogTools.Converter/app.ico.
# Needs Pillow and the DejaVu Sans Bold font.
import sys, math
from PIL import Image, ImageDraw, ImageFont

OUT = sys.argv[1]
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

def rounded_bg(d, S, top, bottom):
    # vertical gradient in a rounded square
    r = int(S * 0.22)
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        gd.line([(0, y), (S, y)], fill=c)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], r, fill=255)
    return grad, mask

def tools(S, small):
    """Trajectory Log Tools: a gantry arc over a patient-plane line, with MLC leaves."""
    grad, mask = rounded_bg(None, S, (52, 132, 228), (24, 84, 170))
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    w = int(S * (0.085 if small else 0.06))
    cx, cy, R = S * 0.5, S * 0.52, S * 0.30
    # arc (the gantry trajectory), 200 -> 340 degrees, with an arrow head at the end
    d.arc([cx - R, cy - R, cx + R, cy + R], start=200, end=340, fill=(255, 255, 255, 255), width=w)
    a = math.radians(340)
    ex, ey = cx + R * math.cos(a), cy + R * math.sin(a)
    h = S * (0.12 if small else 0.10)
    d.polygon([(ex + h * 0.55, ey - h * 0.15), (ex - h * 0.55, ey - h * 0.35), (ex + h * 0.05, ey + h * 0.65)], fill=(255, 255, 255, 255))
    # MLC leaves: pairs of bars meeting at an uneven aperture
    leaves = 3 if small else 5
    top = S * 0.55
    lh = S * (0.075 if small else 0.052)
    gap = S * (0.03 if small else 0.022)
    ap = [0.08, -0.04, 0.10, 0.0, -0.06]
    for i in range(leaves):
        y0 = top + i * (lh + gap)
        mid = cx + S * ap[i] * (0.6 if small else 1)
        d.rounded_rectangle([S * 0.20, y0, mid - S * 0.05, y0 + lh], radius=lh * 0.3, fill=(255, 255, 255, 235))
        d.rounded_rectangle([mid + S * 0.05, y0, S * 0.80, y0 + lh], radius=lh * 0.3, fill=(255, 196, 120, 255))
    return img

def converter(S, small):
    """Trajectory Log Converter: a log page and an arrow into "4"."""
    grad, mask = rounded_bg(None, S, (240, 128, 64), (205, 82, 30))
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    white = (255, 255, 255, 255)
    # page with a folded corner
    px0, py0, px1, py1 = S * 0.16, S * 0.16, S * 0.50, S * 0.70
    fold = S * 0.10
    d.polygon([(px0, py0), (px1 - fold, py0), (px1, py0 + fold), (px1, py1), (px0, py1)], fill=white)
    d.polygon([(px1 - fold, py0), (px1 - fold, py0 + fold), (px1, py0 + fold)], fill=(255, 210, 180, 255))
    if not small:
        for i in range(3):   # text lines on the page
            y = py0 + S * (0.20 + 0.10 * i)
            d.rounded_rectangle([px0 + S * 0.05, y, px1 - S * 0.06 - (S * 0.06 if i == 2 else 0), y + S * 0.035], radius=S * 0.015, fill=(235, 120, 60, 255))
    # arrow
    ay = S * 0.62
    w = S * (0.08 if small else 0.06)
    d.rectangle([S * 0.40, ay - w / 2, S * 0.62, ay + w / 2], fill=white)
    d.polygon([(S * 0.60, ay - w * 1.6), (S * 0.74, ay), (S * 0.60, ay + w * 1.6)], fill=white)
    # "4"
    f = ImageFont.truetype(BOLD, int(S * (0.42 if small else 0.38)))
    d.text((S * 0.80, S * 0.66), "4", font=f, fill=white, anchor="mm")
    return img

def build(fn, name):
    big = {}
    for s in SIZES:
        SS = s * 8   # supersample
        im = fn(SS, s <= 32).resize((s, s), Image.LANCZOS)
        big[s] = im
    big[256].save(f"{OUT}/{name}.png")
    big[256].save(f"{OUT}/{name}.ico", format="ICO", sizes=[(s, s) for s in SIZES], append_images=[big[s] for s in SIZES if s != 256])
    # preview sheet: all sizes
    sheet = Image.new("RGBA", (sum(SIZES) + 10 * len(SIZES), 260), (245, 245, 245, 255))
    x = 5
    for s in SIZES:
        sheet.paste(big[s], (x, 260 - s - 2), big[s]); x += s + 10
    sheet.save(f"{OUT}/{name}_sizes.png")

build(tools, "TrajectoryLogTools")
build(converter, "TrajectoryLogConverter")
