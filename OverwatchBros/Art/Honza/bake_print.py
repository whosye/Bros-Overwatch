import sys
import numpy as np
from PIL import Image

S = 2048
BASE = "C:/Users/budha/AppData/Local/Temp/claude/c--Repos-Bros-Overwatch/07763411-ebd4-43c1-adc8-e611f5222526"
TEX_DIR = "C:/Users/budha/ows/Assets/Models/Honza/Textures"
SRC = BASE + "/scratchpad/KARI_Color_orig.jpg"
OUT = TEX_DIR + "/KARI_Color_1K.jpg"

# stred potisku (x, y v metrech), vyska potisku
CX, CY, H = float(sys.argv[1]), float(sys.argv[2]), float(sys.argv[3])

data = np.fromfile("C:/Users/budha/ows_dump/outfit.bin", dtype=np.float32).reshape(S, S, 6)
data = data[::-1]  # UV v=0 je dole, obrazek ma radek 0 nahore
pos, nrm = data[..., :3], data[..., 3:]
valid = ~np.isnan(pos[..., 0])

tex = Image.open(SRC).convert("RGB").resize((S, S), Image.LANCZOS)
tex = np.asarray(tex).astype(np.float32)

pr = Image.open(BASE + "/images/12.png").convert("RGB")
pw, ph = pr.size
W = H * pw / ph
pr = np.asarray(pr).astype(np.float32)

# Unity: postava se diva do +Z, jeji prava ruka je na -X -> pri pohledu zepredu je +X vlevo.
u = 0.5 - (pos[..., 0] - CX) / W
v = 0.5 - (pos[..., 1] - CY) / H
front = valid & (np.abs(nrm[..., 2]) > 0.15) & (pos[..., 2] > 0.0)  # vcetne naprsni kapsy (ma opacne normaly nez tricko)
inside = front & (u >= 0) & (u <= 1) & (v >= 0) & (v <= 1)

uu = np.clip(np.nan_to_num(u), 0, 1) * (pw - 1)
vv = np.clip(np.nan_to_num(v), 0, 1) * (ph - 1)
x0 = np.floor(uu).astype(int); y0 = np.floor(vv).astype(int)
x1 = np.minimum(x0 + 1, pw - 1); y1 = np.minimum(y0 + 1, ph - 1)
fx = (uu - x0)[..., None]; fy = (vv - y0)[..., None]
sample = (pr[y0, x0] * (1 - fx) + pr[y0, x1] * fx) * (1 - fy) + (pr[y1, x0] * (1 - fx) + pr[y1, x1] * fx) * fy

# mekky okraj potisku (cca 4 mm) a trochu latkoveho stinovani z puvodni textury
edge = np.nan_to_num(np.minimum(np.minimum(u, 1 - u) * W, np.minimum(v, 1 - v) * H))
alpha = np.clip(edge / 0.004, 0, 1) * inside
lum = tex.mean(axis=2, keepdims=True)
ref = np.median(lum[inside]) if inside.any() else 128.0
shade = np.clip(lum / max(ref, 1.0), 0.75, 1.15)
printed = np.clip(sample * (0.35 + 0.65 * shade), 0, 255)

a = alpha[..., None]
out = tex * (1 - a) + printed * a
Image.fromarray(out.astype(np.uint8)).save(OUT, quality=95)
print("texels with print:", int(inside.sum()), "W=%.3f H=%.3f" % (W, H))

# nahled zepredu (ortograficky, body = texely)
P = 900
img = np.zeros((P, P // 2, 3), dtype=np.float32)
zbuf = np.full((P, P // 2), -9.0, dtype=np.float32)
ys, xs = np.nonzero(valid)
px = ((0.45 - pos[ys, xs, 0]) / 0.9 * (P // 2)).astype(int)
py = ((1.6 - pos[ys, xs, 1]) / 1.8 * P).astype(int)
ok = (px >= 0) & (px < P // 2) & (py >= 0) & (py < P)
order = np.argsort(pos[ys, xs, 2][ok])
pxo, pyo = px[ok][order], py[ok][order]
img[pyo, pxo] = out[ys[ok][order], xs[ok][order]]
Image.fromarray(img.astype(np.uint8)).save(BASE + "/scratchpad/honza_front.png")
Image.fromarray(out.astype(np.uint8)).resize((1024, 1024)).save(BASE + "/scratchpad/honza_tex.png")
