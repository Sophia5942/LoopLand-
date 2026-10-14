"""Build canvas atlases (two paintings each, placed in the two picture-panel UV islands).

Usage: python3 -I gen_canvas.py <reference_image> <outdir> <size>
"""
import sys, os
import numpy as np
from PIL import Image, ImageFilter

REF, OUT, N = sys.argv[1], sys.argv[2], int(sys.argv[3])
os.makedirs(OUT, exist_ok=True)
ref = Image.open(REF).convert('RGB')

# picture-panel UV islands (u0, v0, u1, v1) from Frame_.fbx: top frame, bottom frame
PANELS = {'top': (0.4291, 0.7110, 0.7812, 0.9924), 'bottom': (0.0012, 0.7172, 0.3532, 0.9986)}
ASPECT = 1.25

CROPS = {  # source boxes in the reference board + vertical crop bias for portrait pieces
    'Venice': ((66, 462, 458, 640), 0.5), 'Sunset': ((766, 752, 1012, 968), 0.5),
    'Peonies': ((380, 76, 612, 346), 0.5), 'Roses': ((842, 448, 1002, 662), 0.5),
    'Wine': ((575, 446, 762, 660), 0.55), 'Gallery': ((1333, 446, 1490, 664), 0.6),
    'Vase': ((1275, 60, 1492, 352), 0.30), 'Olive': ((1098, 738, 1250, 968), 0.4),
    'Laurel': ((538, 738, 705, 970), 0.4),
}
SETS = [('Venice', 'Sunset'), ('Peonies', 'Roses'), ('Wine', 'Gallery'), ('Vase', 'Laurel'), ('Olive', 'GoldMarble')]


def hash2(ix, iy, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (seed * 83492791)
    h = (h ^ (h >> 13)) * 1274126177
    h = h ^ (h >> 16)
    return (h & 0xFFFFFF).astype(np.float64) / 0xFFFFFF


def vnoise(x, y, seed=0):
    ix, iy = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    fx, fy = x - ix, y - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b = hash2(ix, iy, seed), hash2(ix + 1, iy, seed)
    c, d = hash2(ix, iy + 1, seed), hash2(ix + 1, iy + 1, seed)
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(x, y, oct_=5, seed=0):
    s, a, t = 0, 1, 0
    for o in range(oct_):
        s = s + a * vnoise(x * 2 ** o, y * 2 ** o, seed + o); t += a; a *= 0.5
    return s / t


def painting(name, w, h):
    if name == 'GoldMarble':
        return gold_marble(w, h)
    box, bias = CROPS[name]
    c = ref.crop(box); cw, ch = c.size
    if cw / ch > ASPECT:
        nw = int(ch * ASPECT); x = (cw - nw) // 2; c = c.crop((x, 0, x + nw, ch))
    else:
        nh = int(cw / ASPECT); y = int((ch - nh) * bias); c = c.crop((0, y, cw, y + nh))
    c = c.resize((w, h), Image.LANCZOS)
    c = c.filter(ImageFilter.UnsharpMask(radius=2.0, percent=60, threshold=2))
    return np.asarray(c).astype(np.float64) / 255


def gold_marble(w, h):
    yy, xx = np.mgrid[0:h, 0:w] / float(w) * 6.0
    wx = fbm(xx + 3.1, yy, 5, 3) * 2.2; wy = fbm(xx, yy + 7.7, 5, 9) * 2.2
    f = fbm(xx + wx, yy + wy, 6, 17)
    vein = np.exp(-((f - 0.5) / 0.012) ** 2)
    f2 = fbm(xx * 1.7 + wy, yy * 1.7 + wx, 6, 33)
    vein2 = np.exp(-((f2 - 0.47) / 0.015) ** 2) * 0.35
    tone = fbm(xx * 0.6 + wx * 0.5, yy * 0.6, 4, 51)
    white = np.clip((tone - 0.47) * 6, 0, 1)
    base = np.array([0.03, 0.028, 0.026]) * (1 - white[..., None]) + np.array([0.86, 0.83, 0.77]) * white[..., None]
    base = base * (0.85 + 0.3 * fbm(xx * 4, yy * 4, 4, 77))[..., None]
    gold = np.array([0.88, 0.66, 0.28])
    g = np.clip(vein + vein2, 0, 1)[..., None]
    return np.clip(base * (1 - g) + gold * g * (0.8 + 0.4 * fbm(xx * 9, yy * 9, 3, 91))[..., None], 0, 1)


yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
# canvas weave + craquelure (shared by all sets)
weave = 0.5 + 0.25 * np.sin(xx * np.pi / 1.6) * np.sign(np.sin(yy * np.pi / 6.4)) \
            + 0.25 * np.sin(yy * np.pi / 1.6) * np.sign(np.sin(xx * np.pi / 6.4))
crk = fbm(xx / 38.0, yy / 38.0, 4, 5)
cracks = np.exp(-((crk - 0.5) / 0.006) ** 2) * (fbm(xx / 120.0, yy / 120.0, 3, 8) > 0.45)
height = weave * 0.6 - cracks * 1.2
gx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 0.5
gy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 0.5
s = 0.9
nx, ny = -gx * s, gy * s                      # row axis points down (−v)
ln = np.sqrt(nx ** 2 + ny ** 2 + 1)
nmap = np.stack([nx / ln, ny / ln, 1 / ln], -1)
Image.fromarray(((nmap + 1) * 127.5).astype(np.uint8)).save(os.path.join(OUT, 'Canvas_Normal.png'), optimize=True)
smooth = np.clip(0.42 + 0.08 * (fbm(xx / 60, yy / 60, 3, 12) - 0.5) - 0.25 * cracks, 0, 1)
ms = np.zeros((N, N, 4), np.uint8); ms[..., 3] = (smooth * 255).astype(np.uint8)
Image.fromarray(ms, 'RGBA').save(os.path.join(OUT, 'Canvas_MetallicSmoothness.png'), optimize=True)

PAD = 12
for si, (a, b_) in enumerate(SETS, 1):
    atlas = np.zeros((N, N, 3)) + np.array([0.06, 0.05, 0.04])
    for (slot, name) in (('top', a), ('bottom', b_)):
        u0, v0, u1, v1 = PANELS[slot]
        x0, x1 = int(u0 * N) - PAD, int(np.ceil(u1 * N)) + PAD
        y0, y1 = int((1 - v1) * N) - PAD, int(np.ceil((1 - v0) * N)) + PAD
        x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, N), min(y1, N)
        atlas[y0:y1, x0:x1] = painting(name, x1 - x0, y1 - y0)
    # varnish / weave modulation
    atlas = atlas * (0.94 + 0.08 * weave[..., None]) * (1 - 0.18 * cracks[..., None])
    atlas = atlas * np.array([1.0, 0.985, 0.95])  # aged varnish
    Image.fromarray((np.clip(atlas, 0, 1) * 255).astype(np.uint8)).save(
        os.path.join(OUT, f'Canvas_Set{si}_{a}_{b_}_Albedo.png'), optimize=True)
    print('set', si, a, b_)
