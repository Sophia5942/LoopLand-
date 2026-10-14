"""Tileable PBR textures for the bar set: mahogany wood, brass, leather, frosted glass.

Outputs Albedo / MetallicSmoothness / Normal per surface (Unity Standard, OpenGL normals).
Usage: python3 -I gen_bar_tex.py <outdir> [size]
"""
import sys, os, math
import numpy as np
from PIL import Image

OUT = sys.argv[1]
N = int(sys.argv[2]) if len(sys.argv) > 2 else 2048
os.makedirs(OUT, exist_ok=True)


# ---------------------------------------------------------------- periodic noise
def hash2(ix, iy, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (seed * 83492791)
    h = (h ^ (h >> 13)) * 1274126177
    h = h ^ (h >> 16)
    return (h & 0xFFFFFF).astype(np.float64) / 0xFFFFFF


def pnoise(x, y, px, py, seed=0):
    """Value noise that tiles with period (px, py) in lattice units."""
    ix, iy = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    fx, fy = x - ix, y - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    x0, x1 = ix % px, (ix + 1) % px
    y0, y1 = iy % py, (iy + 1) % py
    a, b = hash2(x0, y0, seed), hash2(x1, y0, seed)
    c, d = hash2(x0, y1, seed), hash2(x1, y1, seed)
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def pfbm(u, v, fx, fy, oct_=5, seed=0):
    """u, v in [0,1); fx, fy integer base frequencies -> seamless fbm."""
    s, a, t = 0, 1, 0
    for o in range(oct_):
        k = 2 ** o
        s = s + a * pnoise(u * fx * k, v * fy * k, fx * k, fy * k, seed + o); t += a; a *= 0.5
    return s / t


def cells(u, v, n, seed=0):
    """Tileable Worley F1 distance (cells of size 1/n)."""
    x, y = u * n, v * n
    ix, iy = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    best = np.full(x.shape, 9.0)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            cx, cy = ix + dx, iy + dy
            jx = hash2(cx % n, cy % n, seed); jy = hash2(cx % n, cy % n, seed + 7)
            d = np.hypot(cx + jx - x, cy + jy - y)
            best = np.minimum(best, d)
    return best


def to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def lin(rgb):
    c = np.asarray(rgb, np.float64) / 255
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def normal_from_height(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    gy = (np.roll(h, 1, 0) - np.roll(h, -1, 0)) * 0.5 * strength
    ln = np.sqrt(gx ** 2 + gy ** 2 + 1)
    return np.stack([-gx / ln, -gy / ln, 1 / ln], -1)


def save(name, alb, metal, smooth, nrm):
    Image.fromarray((to_srgb(alb) * 255).round().astype(np.uint8)).save(os.path.join(OUT, f'{name}_Albedo.png'), optimize=True)
    ms = np.zeros(alb.shape[:2] + (4,), np.uint8)
    m = (np.clip(metal, 0, 1) * 255).astype(np.uint8)
    ms[..., 0] = ms[..., 1] = ms[..., 2] = m
    ms[..., 3] = (np.clip(smooth, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(ms, 'RGBA').save(os.path.join(OUT, f'{name}_MetallicSmoothness.png'), optimize=True)
    Image.fromarray(((nrm + 1) * 127.5).round().astype(np.uint8)).save(os.path.join(OUT, f'{name}_Normal.png'), optimize=True)
    print('wrote', name)


yy, xx = np.mgrid[0:N, 0:N]
U = (xx + 0.5) / N
V = (yy + 0.5) / N

# ---------------------------------------------------------------- mahogany (grain runs along U)
warp = pfbm(U, V, 2, 6, 4, 1) * 0.35 + pfbm(U, V, 1, 3, 3, 2) * 0.25
rings = np.sin((V * 22 + warp * 3.0) * 2 * math.pi)                 # flat-sawn ring lines
rings2 = np.sin((V * 9 + warp * 1.8 + 0.3) * 2 * math.pi)
streak = pfbm(U, V, 3, 120, 4, 3)                                   # fine longitudinal streaks
ribbon = 0.5 + 0.5 * np.sin((V * 7 + pfbm(U, V, 2, 2, 3, 4) * 0.6) * 2 * math.pi)
pores = pfbm(U, V, 40, 900, 2, 5)
pore = np.clip((0.32 - pores) * 6, 0, 1)                            # dark elongated pores
tone = 0.55 + 0.25 * rings * 0.5 + 0.18 * rings2 * 0.25 + 0.22 * (streak - 0.5) + 0.12 * ribbon
tone = np.clip(tone, 0, 1.2)
dark, light = lin((38, 12, 7)), lin((112, 40, 20))
wood = dark + (light - dark) * tone[..., None]
wood = wood * (1 - 0.45 * pore[..., None])
wood *= (0.92 + 0.16 * pfbm(U, V, 1, 1, 2, 9))[..., None]
h = 0.35 * (streak - 0.5) - 0.6 * pore + 0.05 * rings
save('Bar_Mahogany', wood, np.zeros((N, N)), 0.78 - 0.25 * pore + 0.04 * ribbon, normal_from_height(h, 2.2))

# ---------------------------------------------------------------- brass (brushed along U)
M = N // 2
yy2, xx2 = np.mgrid[0:M, 0:M]
U2, V2 = (xx2 + 0.5) / M, (yy2 + 0.5) / M
brush = pfbm(U2, V2, 2, 260, 3, 21)
tarn = pfbm(U2, V2, 3, 3, 5, 22)
tarnish = np.clip((tarn - 0.55) * 3.5, 0, 1)
brass = lin((222, 178, 92)) * (0.9 + 0.2 * brush)[..., None]
brass = brass * (1 - 0.45 * tarnish[..., None]) + lin((88, 70, 40)) * 0.45 * tarnish[..., None]
save('Bar_Brass', brass, np.ones((M, M)), 0.82 - 0.3 * tarnish - 0.08 * (brush - 0.5),
     normal_from_height(0.15 * brush, 1.0))

# ---------------------------------------------------------------- leather (pebbled, cognac)
c1 = cells(U2, V2, 70, 31)
c2 = cells(U2, V2, 160, 41)
pebble = np.clip(c1, 0, 1) * 0.7 + np.clip(c2, 0, 1) * 0.3
wear = pfbm(U2, V2, 3, 3, 5, 33)
leather = lin((92, 46, 26)) * (0.8 + 0.4 * wear)[..., None] * (1.06 - 0.22 * pebble)[..., None]
leather = leather + lin((150, 90, 52)) * np.clip((wear - 0.62) * 2.5, 0, 1)[..., None] * 0.6  # rubbed highlights
save('Bar_Leather', leather, np.zeros((M, M)), 0.48 - 0.18 * pebble + 0.1 * (wear - 0.5),
     normal_from_height(-pebble * 0.9, 3.0))

# ---------------------------------------------------------------- frosted glass (pendant shades) + mirror
Q = 256
yy3, xx3 = np.mgrid[0:Q, 0:Q]
U3, V3 = (xx3 + 0.5) / Q, (yy3 + 0.5) / Q
fr = pfbm(U3, V3, 6, 6, 3, 51)
save('Bar_FrostedGlass', lin((236, 228, 214)) * (0.95 + 0.1 * fr)[..., None], np.zeros((Q, Q)), 0.55 + 0.1 * fr,
     normal_from_height(fr * 0.3, 1.0))
sm = pfbm(U3, V3, 4, 4, 4, 61)
save('Bar_Mirror', lin((120, 112, 100)) * (0.85 + 0.15 * sm)[..., None], np.ones((Q, Q)), 0.9 - 0.25 * np.clip((sm - 0.6) * 3, 0, 1),
     normal_from_height(np.zeros((Q, Q)), 1.0))
