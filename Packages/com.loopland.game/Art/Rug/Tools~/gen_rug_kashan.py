"""Dense traditional Persian (Kashan-style) round rug: ivory field with an allover lattice of rust and
brown arabesques, dark border with cartouches, aged wool. Rendered at 2x and box-filtered.
Usage: python3 -I gen_rug_kashan.py <outdir> [size]"""
import sys, os, math
import numpy as np
from PIL import Image, ImageFilter

OUT = sys.argv[1]; N = int(sys.argv[2]) if len(sys.argv) > 2 else 2048
NAME = 'Persian'
SS = 2; M = N * SS
os.makedirs(OUT, exist_ok=True)
f32 = np.float32
yy, xx = np.mgrid[0:M, 0:M].astype(f32)
X = (xx + 0.5) / M * 2 - 1; Y = 1 - (yy + 0.5) / M * 2
del xx, yy
R = np.hypot(X, Y) / f32(0.995); A = np.arctan2(Y, X)
PX = f32(2.0 / M)


def h2(ix, iy, s):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (s * 83492791); h = (h ^ (h >> 13)) * 1274126177; h ^= h >> 16
    return ((h & 0xFFFFFF) / 0xFFFFFF).astype(f32)
def vn(x, y, s=0):
    ix, iy = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64); fx, fy = x - ix, y - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    return (h2(ix, iy, s) * (1 - fx) + h2(ix + 1, iy, s) * fx) * (1 - fy) + (h2(ix, iy + 1, s) * (1 - fx) + h2(ix + 1, iy + 1, s) * fx) * fy
def fbm(x, y, o=4, s=0):
    t, a, w = 0, 1, 0
    for i in range(o): t = t + a * vn(x * 2 ** i, y * 2 ** i, s + i); w += a; a *= .5
    return (t / w).astype(f32)
def C(*c): return np.array(c, f32) / 255
def ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)
def fill(d):                      # anti-aliased inside mask for a signed distance (negative inside)
    return np.clip(0.5 - d / PX, 0, 1)
def stroke(d, w):
    return np.clip((w - np.abs(d)) / PX + 0.5, 0, 1)

IVORY, BEIGE, RUST, DRED, DBROWN, SLATE, TAN = (C(194, 178, 150), C(176, 156, 124), C(128, 56, 42), C(96, 36, 30),
                                                C(56, 38, 30), C(78, 70, 62), C(160, 118, 80))
col = np.empty((M, M, 3), f32); col[:] = IVORY
def paint(m, c):
    m = m[..., None]; col[:] = col * (1 - m) + c * m

# ------------------------------------------------------------ field: allover ogee lattice
P = f32(0.105)                                       # cell size in rug-radius units
w = (fbm(X * 9, Y * 9, 3, 1) - 0.5) * f32(0.022)       # hand-knotted wobble
w2 = (fbm(X * 7 + 3, Y * 7, 3, 4) - 0.5) * f32(0.022)
u = (X + w) / P; v = (Y + w2) / P
# rotate 45 degrees for a diamond (ogee) lattice
ru, rv = (u + v) * f32(0.7071), (v - u) * f32(0.7071)
cu, cv = np.floor(ru), np.floor(rv)
lu, lv = ru - cu - 0.5, rv - cv - 0.5                  # -0.5..0.5 in the cell
au, av = np.abs(lu), np.abs(lv)
lr = np.hypot(lu, lv); lt = np.arctan2(lv, lu)
# 8-fold folded angle for symmetric motifs
t8 = np.abs(((lt / (2 * np.pi) * 8) % 1) - 0.5) * 2    # 0 at petal centre .. 1 between petals
alt = ((cu + cv) % 2 == 0)

field = ss(0.80, 0.79, R).astype(f32)
# faint beige ground clouds (abrash-like tonal patches)
paint(field * np.clip((fbm(X * 6, Y * 6, 4, 2) - 0.45) * 2.5, 0, 0.55), BEIGE)
# ogee lattice: wavy diamond outline in dark brown
ogee = au + av - 0.5 + 0.035 * np.sin(lr * 30)
paint(field * stroke(ogee * P, 0.0035), DBROWN)
paint(field * stroke((ogee + 0.06) * P, 0.002), RUST)
# arabesque scroll vines: interleaved spirals in rust
spiral = np.sin(lr * 26 - t8 * 3.2 + alt * 1.5)
vine_zone = (lr > 0.15) & (lr < 0.45)
paint(field * vine_zone * stroke(spiral / 26 * P, 0.0007), RUST)
spiral2 = np.sin(lr * 26 + t8 * 3.2 + 1.3)
paint(field * vine_zone * stroke(spiral2 / 26 * P, 0.0005), DRED)
# palmettes on the eight axes, between the vines
pr = np.hypot((lr - 0.31) / 0.05, t8 / 0.2)
palm = (pr - 1) * 0.05
paint(field * fill(palm * P), np.where(alt[..., None], RUST, SLATE))
paint(field * stroke(palm * P, 0.0015), DBROWN)
paint(field * fill((pr - 0.45) * 0.05 * P), IVORY)
# fine curling tendrils over the open ivory ground
tend = np.sin(lr * 44 + t8 * 7 + alt * 2.0)
paint(field * ((lr > 0.4) & (lr < 0.66)) * stroke(tend / 44 * P, 0.00045), DBROWN)
tend2 = np.sin(lr * 44 - t8 * 7 + 0.8)
paint(field * ((lr > 0.42) & (lr < 0.62)) * stroke(tend2 / 44 * P, 0.0004), RUST)
# central rosette with lobed edge, rings and a dark core
ros_r = 0.115 * (0.78 + 0.22 * np.abs(np.cos(lt * 4)) ** 0.7)
paint(field * fill((lr - ros_r) * P), np.where(alt[..., None], DRED, RUST))
paint(field * stroke((lr - ros_r) * P, 0.002), DBROWN)
paint(field * fill((lr - 0.07) * P), IVORY)
paint(field * stroke((lr - 0.052) * P, 0.0014), RUST)
paint(field * fill((lr - 0.026) * P), DBROWN)
for k in range(8):
    a = k * np.pi / 4
    paint(field * fill((np.hypot(lu - 0.088 * np.cos(a), lv - 0.088 * np.sin(a)) - 0.012) * P), TAN)
# small flower heads at the lattice corners
cr = np.hypot(np.minimum(au, 0.5 - au), np.minimum(av, 0.5 - av))
corner = np.hypot(0.5 - au, 0.5 - av)
paint(field * fill((corner - 0.055 * (0.8 + 0.2 * np.cos(np.arctan2(0.5 - av, 0.5 - au) * 10))) * P), RUST)
paint(field * fill((corner - 0.02) * P), IVORY)
# scattered tiny buds in the remaining ivory: the "busy" speckle of a hand-knotted field
bud = fbm(X * 300, Y * 300, 2, 5)
paint(field * np.clip((bud - 0.75) * 10, 0, 1) * (lr > 0.42), RUST)
paint(field * np.clip((0.22 - bud) * 10, 0, 1) * (lr > 0.42), SLATE)
del ru, rv, cu, cv, lu, lv, au, av, lr, lt, t8, spiral, spiral2, pr, palm, ros_r, cr, corner, bud, tend, tend2

# ------------------------------------------------------------ borders
def band(r0, r1):
    return (ss(r0 - 0.0015, r0 + 0.0015, R) * (1 - ss(r1 - 0.0015, r1 + 0.0015, R))).astype(f32)
paint(band(0.80, 0.815), RUST)                                 # inner guard
gu = (A / (2 * np.pi) * 360) % 1
paint(band(0.803, 0.812) * (np.abs(gu - 0.5) < 0.22), IVORY)  # dotted
paint(band(0.815, 0.93), DBROWN)                               # main border ground
na = 64
ba = ((A / (2 * np.pi) * na) % 1 - 0.5) * (2 * np.pi * 0.872 / na)   # arc distance to cartouche centre
br = R - 0.8725
cart = np.hypot(ba / 0.022, br / 0.034) * (0.85 + 0.15 * np.abs(np.cos(4 * np.arctan2(br, ba)))) - 1
paint(band(0.815, 0.93) * fill(cart * 0.022), RUST)
paint(band(0.815, 0.93) * stroke(cart * 0.022, 0.0016), TAN)
inner = np.hypot(ba / 0.011, br / 0.016) - 1
paint(band(0.815, 0.93) * fill(inner * 0.011), IVORY)
paint(band(0.815, 0.93) * fill((np.hypot(ba, br) - 0.004)), DRED)
ba2 = ((A / (2 * np.pi) * na + 0.5) % 1 - 0.5) * (2 * np.pi * 0.872 / na)
vine = br - 0.028 * np.sin(ba2 / 0.022 * np.pi)
paint(band(0.82, 0.925) * stroke(vine, 0.0016) * (np.abs(ba2) < 0.022), TAN)
leafy = np.hypot(ba2 / 0.007, (br - 0.03) / 0.012) - 1
paint(band(0.82, 0.925) * fill(leafy * 0.007), IVORY)
leafy = np.hypot(ba2 / 0.007, (br + 0.03) / 0.012) - 1
paint(band(0.82, 0.925) * fill(leafy * 0.007), RUST)
paint(band(0.93, 0.945), RUST)                                 # outer guard
paint(band(0.932, 0.943) * (np.abs(((A / (2 * np.pi) * 300) % 1) - 0.5) < 0.18), IVORY)
paint(band(0.945, 0.975), DRED)
paint(band(0.95, 0.97) * (np.abs(((A / (2 * np.pi) * 160) % 1) - 0.5) < 0.12), TAN)
paint(ss(0.973, 0.977, R).astype(f32), DBROWN)                  # binding

# ------------------------------------------------------------ age and wool
abrash = fbm(X * 3, Y * 3, 4, 9)
wear = np.clip((fbm(X * 5, Y * 5, 4, 13) - 0.62) * 3, 0, 0.6)  # worn, lighter patches
col = col * (0.9 + 0.16 * abrash)[..., None]
col = col * (1 - wear[..., None] * 0.35) + C(170, 150, 120) * wear[..., None] * 0.35
grey = col.mean(-1, keepdims=True)
col = col * 0.85 + grey * 0.15
col = np.clip(col, 0, 1)
img = Image.fromarray((col * 255).astype(np.uint8)).resize((N, N), Image.BOX)
del col
small_X = X[::SS, ::SS]; small_Y = Y[::SS, ::SS]
pile = fbm(small_X * 420, small_Y * 420, 2, 21)
fibre = fbm(small_X * 1100, small_Y * 1100, 1, 23)
a = np.asarray(img, f32) / 255
a = a * (0.88 + 0.18 * pile + 0.08 * fibre)[..., None]
img = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.55))
img.save(os.path.join(OUT, f'Rug_{NAME}_Albedo.png'), optimize=True)

lum = np.asarray(img.convert('L'), f32) / 255
h = 0.55 * pile + 0.35 * fibre + 0.25 * lum                   # pattern reads faintly in the pile
gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 2.2; gy = (np.roll(h, 1, 0) - np.roll(h, -1, 0)) * 2.2
ln = np.sqrt(gx ** 2 + gy ** 2 + 1)
Image.fromarray(((np.stack([-gx / ln, -gy / ln, 1 / ln], -1) + 1) * 127.5).astype(np.uint8)).save(
    os.path.join(OUT, f'Rug_{NAME}_Normal.png'), optimize=True)
ms = np.zeros((N, N, 4), np.uint8); ms[..., 3] = (np.clip(0.1 + 0.06 * pile, 0, 1) * 255).astype(np.uint8)
Image.fromarray(ms, 'RGBA').save(os.path.join(OUT, f'Rug_{NAME}_MetallicSmoothness.png'), optimize=True)
print('ok')
