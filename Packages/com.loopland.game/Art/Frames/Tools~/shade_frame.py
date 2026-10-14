"""Turn bake.npz into Unity Standard-shader maps for several frame finishes.

Usage: python3 -I shade_frame.py <bake.npz> <outdir>
"""
import sys, os
import numpy as np
from PIL import Image

b = np.load(sys.argv[1])
OUT = sys.argv[2]
os.makedirs(OUT, exist_ok=True)
N = int(b['N']); ry, rx = b['ry'], b['rx']
H, GX, GY, LAP, POS, front = b['H'], b['GX'], b['GY'], b['LAP'], b['POS'].astype(np.float64), b['front']


def hash3(ix, iy, iz, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
    h = (h ^ (h >> 13)) * 1274126177
    h = h ^ (h >> 16)
    return (h & 0xFFFFFF).astype(np.float64) / 0xFFFFFF


def vnoise(p, freq, seed=0):
    q = p * freq
    i = np.floor(q).astype(np.int64); f = q - i
    f = f * f * (3 - 2 * f)
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                out = out + w * hash3(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
    return out


def fbm(p, freq, oct_=4, seed=0):
    s, a, tot = 0, 1, 0
    for o in range(oct_):
        s = s + a * vnoise(p, freq * 2 ** o, seed + o); tot += a; a *= 0.5
    return s / tot


def sstep(a, b_, x):
    t = np.clip((x - a) / (b_ - a), 0, 1); return t * t * (3 - 2 * t)


# shared masks -------------------------------------------------------------
# relative height of ornament: 0 = deepest recess, 1 = top of carving
relh = np.clip((H + 0.0012) / 0.0055, 0, 1)
concave = np.clip(LAP / 60.0, 0, 1)          # crevices
convex = np.clip(-LAP / 60.0, 0, 1)          # crests / sharp edges
grime_n = fbm(POS, 18, 4, 1)
spot_n = fbm(POS, 70, 3, 11)
wear_n = fbm(POS, 140, 3, 21)

cavity = np.clip(0.75 * concave + 0.7 * (1 - sstep(0.15, 0.55, relh)) * front, 0, 1)
cavity = np.clip(cavity * (0.75 + 0.5 * grime_n), 0, 1)
crest = np.clip(convex * 1.4 + sstep(0.6, 0.95, relh) * 0.6 * front, 0, 1)
crest = crest * sstep(0.35, 0.65, wear_n + 0.25)
ao = np.clip(1 - 0.65 * cavity - 0.25 * (1 - relh) * front, 0.25, 1)
ao = np.where(front, ao, 0.85)               # walls/back sit slightly shadowed

strength = 1.0
nx = -GX * strength; ny = -GY * strength
nz = np.ones_like(nx)
ln = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
nrm = np.stack([nx / ln, ny / ln, nz / ln], 1)

FINISHES = {
    # name: base metal, polished highlight, grime colour, base smoothness, grime smoothness
    'AntiqueGold': dict(metal=(208, 152, 64), hi=(247, 204, 112), grime=(52, 36, 20), dark=None,
                        s_metal=0.72, s_grime=0.28),
    'DarkBronze': dict(metal=(122, 86, 46), hi=(184, 138, 76), grime=(28, 20, 13), dark=None,
                       s_metal=0.62, s_grime=0.25),
    'BlackGold': dict(metal=(214, 160, 70), hi=(248, 208, 120), grime=(26, 20, 14), dark=(16, 14, 13),
                      s_metal=0.74, s_grime=0.30),
}


def to_lin(c):
    c = np.asarray(c, np.float64) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def img(vals, chans, fill):
    a = np.empty((N, N, chans), np.uint8); a[:] = fill
    a[ry, rx] = np.clip(np.round(vals * 255), 0, 255).astype(np.uint8).reshape(-1, chans)
    return a


def save(arr, name):
    mode = {1: 'L', 3: 'RGB', 4: 'RGBA'}[arr.shape[2]]
    Image.fromarray(arr[:, :, 0] if mode == 'L' else arr, mode).save(os.path.join(OUT, name), optimize=True)


normal_img = img((nrm + 1) * 0.5, 3, (128, 128, 255))
save(normal_img, 'Frame_Normal.png')
save(img(ao[:, None], 1, 255), 'Frame_Occlusion.png')

for name, f in FINISHES.items():
    metal, hi, grime = to_lin(f['metal']), to_lin(f['hi']), to_lin(f['grime'])
    tint = 0.85 + 0.3 * grime_n[:, None]                  # patchy gilding tone
    spots = (spot_n[:, None] - 0.5) * 0.18
    base = metal * tint * (1 + spots)
    col = base * (1 - crest[:, None]) + hi * crest[:, None]
    gmask = cavity[:, None]
    metallic = 1 - 0.85 * cavity
    smooth = f['s_metal'] * (1 - cavity) + f['s_grime'] * cavity + 0.12 * crest - 0.08 * (grime_n - 0.5)
    if f['dark'] is not None:
        # black lacquer on flats and recesses, gilt on the carved relief, worn through on edges
        lac = to_lin(f['dark'])
        gilt = np.clip(sstep(0.42, 0.62, relh) * front + crest * 0.8, 0, 1)
        gilt = np.where(front, gilt, 0.0)
        col = lac * (1 - gilt[:, None]) + col * gilt[:, None]
        metallic = metallic * gilt
        smooth = 0.68 * (1 - gilt) + smooth * gilt
    col = col * (1 - gmask) + grime * gmask
    alb = to_srgb(col)
    save(img(alb, 3, (60, 45, 25)), f'Frame_{name}_Albedo.png')
    ms = np.stack([metallic, metallic, metallic, np.clip(smooth, 0, 1)], 1)
    save(img(ms, 4, (0, 0, 0, 80)), f'Frame_{name}_MetallicSmoothness.png')
    print('wrote', name)
