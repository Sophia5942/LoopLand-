"""Round Persian-style rug texture (ivory field, red/navy florals, medallion). Usage: gen_rug.py <outdir> [size]"""
import sys, os, math
import numpy as np
from PIL import Image, ImageFilter

OUT = sys.argv[1]; N = int(sys.argv[2]) if len(sys.argv) > 2 else 2048
os.makedirs(OUT, exist_ok=True)
yy, xx = np.mgrid[0:N, 0:N]
X = (xx + 0.5) / N * 2 - 1; Y = 1 - (yy + 0.5) / N * 2
R = np.hypot(X, Y) / 0.995; A = np.arctan2(Y, X)

def h2(ix, iy, s):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (s * 83492791); h = (h ^ (h >> 13)) * 1274126177; h ^= h >> 16
    return (h & 0xFFFFFF) / 0xFFFFFF
def vn(x, y, s=0):
    ix, iy = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64); fx, fy = x - ix, y - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    return (h2(ix, iy, s) * (1 - fx) + h2(ix + 1, iy, s) * fx) * (1 - fy) + (h2(ix, iy + 1, s) * (1 - fx) + h2(ix + 1, iy + 1, s) * fx) * fy
def fbm(x, y, o=4, s=0):
    t, a, w = 0, 1, 0
    for i in range(o): t = t + a * vn(x * 2 ** i, y * 2 ** i, s + i); w += a; a *= .5
    return t / w
def C(*c): return np.array(c, float) / 255

IVORY, CREAM, RED, DRED, NAVY, TAN, BROWN = C(214, 200, 172), C(196, 178, 146), C(138, 38, 32), C(92, 24, 22), C(38, 36, 48), C(168, 128, 78), C(70, 46, 32)
col = np.zeros((N, N, 3)) + IVORY
def paint(mask, c):
    global col
    m = np.clip(mask, 0, 1)[..., None]; col = col * (1 - m) + c * m
def ss(a, b, x): t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)
def band(r0, r1, aa=0.003): return ss(r0 - aa, r0 + aa, R) * (1 - ss(r1 - aa, r1 + aa, R))

# ---- field: dense allover floral lattice (Cartesian tiling)
P = 0.055
fx, fy = (X / P) % 1 - 0.5, (Y / P) % 1 - 0.5
cx, cy = np.floor(X / P), np.floor(Y / P)
alt = ((cx + cy) % 2 == 0)
rr = np.hypot(fx, fy); th = np.arctan2(fy, fx)
rosette = rr < 0.27 * (0.7 + 0.3 * np.abs(np.cos(4 * th)))
leaf = (np.abs(np.abs(fx) - np.abs(fy)) < 0.08) & (rr > 0.28) & (rr < 0.5)
# offset grid of small buds between rosettes
gx2, gy2 = ((X / P) + 0.5) % 1 - 0.5, ((Y / P) + 0.5) % 1 - 0.5
bud = np.hypot(gx2 * 1.6, gy2) < 0.16
field = band(0.31, 0.83)
paint(field * fbm(X * 14, Y * 14, 3, 2) * 0.35, CREAM * 0.8 + RED * 0.2)          # soft tonal clouds
vine = np.abs(np.sin((X + 0.25 * np.sin(Y * 23)) * 57)) < 0.12
paint(field * vine, DRED)
paint(field * leaf, NAVY)
paint(field * bud, RED * 0.6 + TAN * 0.4)
paint(field * rosette * alt, RED); paint(field * rosette * ~alt, DRED)
paint(field * (rr < 0.13), IVORY)
paint(field * (rr < 0.06), NAVY)
# ---- medallion
mr = 0.30 * (0.86 + 0.14 * np.abs(np.cos(4 * A)) ** 0.6)
paint(R < mr, RED)
paint(np.abs(R - mr + 0.012) < 0.006, NAVY)
inner = 0.17 * (0.85 + 0.15 * np.abs(np.cos(8 * A)))
paint(R < inner, IVORY)
paint((R < inner) & (np.abs(np.sin(8 * A)) * R < 0.03) & (R > 0.04), RED)
paint(R < 0.055, NAVY); paint(R < 0.025, TAN)
paint((R < mr - 0.03) & (R > inner + 0.02) & (np.abs(np.sin(16 * A)) > 0.92), IVORY)
# ---- borders
paint(band(0.83, 0.855), RED)
paint(band(0.855, 0.955), CREAM)
na = 40
sa = (A / (2 * math.pi) * na) % 1 - 0.5
br = (R - 0.905) / 0.05
ros = np.hypot(sa * 1.15, br) < 0.42 * (0.8 + 0.2 * np.abs(np.cos(5 * np.arctan2(br, sa))))
paint(band(0.86, 0.95) * ros, RED)
paint(band(0.86, 0.95) * (np.hypot(sa * 1.15, br) < 0.13), NAVY)
wave = np.abs(br - 0.55 * np.sin(sa * 2 * math.pi)) < 0.09
paint(band(0.86, 0.95) * wave * ~ros, NAVY)
paint(band(0.955, 0.975), RED)
paint(band(0.975, 1.02), NAVY)
paint(band(0.978, 0.995) * (np.abs(np.sin(A * 180)) > 0.7), TAN)
paint(R >= 1.0, NAVY)

# ---- wool pile, wear, slight colour drift
pile = fbm(X * 260, Y * 260, 3, 5)
wear = fbm(X * 3, Y * 3, 4, 9)
col = col * (0.86 + 0.24 * pile)[..., None] * (0.9 + 0.15 * wear)[..., None]
col = col * 0.82 + col.mean(-1, keepdims=True) * 0.18           # muted, aged
col = np.clip(col, 0, 1)
img = Image.fromarray((col * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))
img.save(os.path.join(OUT, 'Rug_Persian_Albedo.png'), optimize=True)

h = pile + 0.3 * fbm(X * 700, Y * 700, 2, 7)
gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 2; gy = (np.roll(h, 1, 0) - np.roll(h, -1, 0)) * 2
ln = np.sqrt(gx ** 2 + gy ** 2 + 1)
Image.fromarray((((np.stack([-gx / ln, -gy / ln, 1 / ln], -1)) + 1) * 127.5).astype(np.uint8)).save(os.path.join(OUT, 'Rug_Persian_Normal.png'), optimize=True)
ms = np.zeros((N, N, 4), np.uint8); ms[..., 3] = (np.clip(0.12 + 0.06 * pile, 0, 1) * 255).astype(np.uint8)
Image.fromarray(ms, 'RGBA').save(os.path.join(OUT, 'Rug_Persian_MetallicSmoothness.png'), optimize=True)
print('ok')
