import math, os
from PIL import Image, ImageDraw

S = 1024
OUT = 256
W = (255, 255, 255, 255)
C = (0, 0, 0, 0)
out_dir = r"C:\Repos\Bros-Overwatch\OverwatchBros\Assets\Resources\Icons"

def new():
    img = Image.new("RGBA", (S, S), C)
    return img, ImageDraw.Draw(img)

def save(img, name):
    img.resize((OUT, OUT), Image.LANCZOS).save(os.path.join(out_dir, name))

def line(d, a, b, width, fill=W):
    d.line([(a[0] * S, a[1] * S), (b[0] * S, b[1] * S)], fill=fill, width=int(width * S))
    r = width * S / 2
    for x, y in (a, b):
        d.ellipse([x * S - r, y * S - r, x * S + r, y * S + r], fill=fill)

def circle(d, cx, cy, r, fill=W):
    d.ellipse([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], fill=fill)

# ---------- Naloz (Shift): kulata mina s rozbuskou a paprsky vybuchu
img, d = new()
for i in range(8):
    a = math.radians(i * 45 + 22.5)
    line(d, (0.5 + math.cos(a) * 0.36, 0.52 + math.sin(a) * 0.36), (0.5 + math.cos(a) * 0.45, 0.52 + math.sin(a) * 0.45), 0.05)
circle(d, 0.5, 0.52, 0.29)
circle(d, 0.5, 0.52, 0.20, C)
circle(d, 0.5, 0.52, 0.11)
save(img, "honza_mine.png")

# ---------- Past na medvedy (prave tlacitko): dve celisti se zuby
img, d = new()
d.pieslice([0.10 * S, 0.22 * S, 0.90 * S, 1.02 * S], 180, 360, fill=W)
d.pieslice([0.19 * S, 0.31 * S, 0.81 * S, 0.93 * S], 180, 360, fill=C)
for i in range(6):
    x = 0.24 + i * 0.104
    d.polygon([((x - 0.045) * S, 0.62 * S), ((x + 0.045) * S, 0.62 * S), (x * S, 0.44 * S)], fill=W)
d.rounded_rectangle([0.08 * S, 0.62 * S, 0.92 * S, 0.72 * S], radius=0.03 * S, fill=W)
circle(d, 0.5, 0.82, 0.07)
line(d, (0.5, 0.72), (0.5, 0.82), 0.04)
save(img, "honza_trap.png")

# ---------- Balvan (Q): valici se kamen s rychlostnimi carami a puklinou
img, d = new()
circle(d, 0.60, 0.50, 0.31)
line(d, (0.50, 0.21), (0.60, 0.34), 0.03, C)
line(d, (0.60, 0.34), (0.53, 0.44), 0.03, C)
line(d, (0.53, 0.44), (0.64, 0.56), 0.03, C)
circle(d, 0.76, 0.40, 0.045, C)
circle(d, 0.48, 0.64, 0.035, C)
line(d, (0.06, 0.34), (0.24, 0.34), 0.045)
line(d, (0.03, 0.52), (0.20, 0.52), 0.045)
line(d, (0.06, 0.70), (0.26, 0.70), 0.045)
line(d, (0.12, 0.88), (0.92, 0.88), 0.04)
save(img, "honza_boulder.png")
print("ok")
