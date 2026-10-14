"""Round Art Deco rug: charcoal wool, antique-gold sunburst, fan arches and double border.
Usage: python3 -I gen_rug_deco.py <outdir> [size]"""
import sys, os, math
import numpy as np
from PIL import Image

OUT = sys.argv[1]; N = int(sys.argv[2]) if len(sys.argv) > 2 else 2048
NAME = 'Deco'
os.makedirs(OUT, exist_ok=True)
yy, xx = np.mgrid[0:N, 0:N]
X = (xx + 0.5) / N * 2 - 1; Y = 1 - (yy + 0.5) / N * 2
R = np.hypot(X, Y) / 0.995; A = np.arctan2(Y, X)
PX = 2.0 / N / 0.995                       # one pixel in R units


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
def line(d, w):
    """Anti-aliased line mask from a distance field d (R units) with half-width w."""
    return np.clip((w - np.abs(d)) / PX + 0.5, 0, 1)

CHAR, CHAR2, GOLD, WINE, EDGE = C(34, 29, 27), C(46, 39, 35), C(184, 146, 82), C(70, 22, 24), C(20, 17, 16)
col = np.zeros((N, N, 3)) + CHAR
gold = np.zeros((N, N))
def paint(m, c):
    global col
    m = np.clip(m, 0, 1)[..., None]; col = col * (1 - m) + c * m

# centre: tone-on-tone disc with gold rings and a small wine core
paint(R < 0.2, CHAR2)
for r in (0.2, 0.185, 0.07):
    gold = np.maximum(gold, line(R - r, 0.0022))
paint(R < 0.065, WINE)
gold = np.maximum(gold, line(R - 0.035, 0.0018))

# sunburst: 24 rays from the centre ring out to the fan ring, alternating tone
na = 24
seg = (A / (2 * math.pi) * na) % 1
ray_band = (R > 0.2) & (R < 0.6)
paint(ray_band & (np.floor(A / (2 * math.pi) * na) % 2 == 0), CHAR2)
ray_d = (np.minimum(seg, 1 - seg) / na * 2 * math.pi) * R    # arc distance to the nearest ray line
gold = np.maximum(gold, line(ray_d, 0.0016) * ray_band)
gold = np.maximum(gold, line(R - 0.6, 0.0022))

# fan arches: 16 Deco fans between r 0.6 and 0.84, each with three nested arcs
nf = 16
fa = (A / (2 * math.pi) * nf) % 1 - 0.5                      # -0.5..0.5 across each fan
fx = fa * 2 * math.pi / nf * 0.72                             # local x (R units, approx)
fy = R - 0.6                                                  # local y from the fan base
fan_band = (R > 0.6) & (R < 0.84)
fr = np.hypot(fx, fy)
for k, rad in enumerate((0.21, 0.155, 0.10)):
    gold = np.maximum(gold, line(fr - rad, 0.0018) * fan_band)
paint(fan_band & (fr < 0.10), WINE)
paint(fan_band & (fr >= 0.10) & (fr < 0.155), CHAR2)
fan_ray = np.abs(np.arctan2(fx, fy)) * fr
for t in (-0.6, -0.3, 0.0, 0.3, 0.6):
    gold = np.maximum(gold, line(np.arctan2(fx, fy) * fr - t * fr, 0.0012) * fan_band * (fr < 0.21) * (fr > 0.10))
gold = np.maximum(gold, line(R - 0.84, 0.0022))

# double gold border + dark binding
for r in (0.9, 0.915, 0.965):
    gold = np.maximum(gold, line(R - r, 0.0024))
paint((R > 0.915) & (R < 0.965), CHAR2)
paint(R > 0.975, EDGE)

# gold is a woven yarn, not metal: blend it in and keep a little wool variation in it
paint(gold, GOLD)

# wool: soft abrash clouds, pile speckle, gentle wear
abrash = fbm(X * 4, Y * 4, 4, 3)
pile = fbm(X * 300, Y * 300, 3, 5)
fibre = fbm(X * 900, Y * 900, 2, 7)
col = col * (0.88 + 0.22 * abrash)[..., None] * (0.86 + 0.2 * pile + 0.08 * fibre)[..., None]
col = np.clip(col, 0, 1)
Image.fromarray((col * 255).astype(np.uint8)).save(os.path.join(OUT, f'Rug_{NAME}_Albedo.png'), optimize=True)

# pile normal: fibre noise, gold yarn sits slightly proud
h = 0.6 * pile + 0.4 * fibre + 0.6 * gold
gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 2.5; gy = (np.roll(h, 1, 0) - np.roll(h, -1, 0)) * 2.5
ln = np.sqrt(gx ** 2 + gy ** 2 + 1)
Image.fromarray(((np.stack([-gx / ln, -gy / ln, 1 / ln], -1) + 1) * 127.5).astype(np.uint8)).save(
    os.path.join(OUT, f'Rug_{NAME}_Normal.png'), optimize=True)
ms = np.zeros((N, N, 4), np.uint8)
ms[..., 3] = (np.clip(0.1 + 0.05 * pile + 0.12 * gold, 0, 1) * 255).astype(np.uint8)   # gold yarn has a slight sheen
Image.fromarray(ms, 'RGBA').save(os.path.join(OUT, f'Rug_{NAME}_MetallicSmoothness.png'), optimize=True)
print('ok')
