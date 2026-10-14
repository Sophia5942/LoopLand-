"""Bake wine bottle textures (Albedo, MetallicSmoothness, Normal) for each label variant.

Usage: python3 -I gen_bottle_tex.py <outdir> [size]
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from bottle_profile import *  # noqa

OUT = sys.argv[1]
N = int(sys.argv[2]) if len(sys.argv) > 2 else 2048
os.makedirs(OUT, exist_ok=True)
FD = '/usr/share/fonts/truetype/liberation/'
F_BOLD, F_REG, F_IT = FD + 'LiberationSerif-Bold.ttf', FD + 'LiberationSerif-Regular.ttf', FD + 'LiberationSerif-Italic.ttf'

VARIANTS = {
    'Rouge': dict(glass=(9, 26, 15), paper=(236, 226, 204), ink=(46, 30, 22), accent=(196, 152, 70),
                  capsule=(92, 10, 22), cap_metal=0.55, band=(200, 158, 78),
                  top='GRAND VIN DE', vintage='2019', appellation='BORDEAUX SUPÉRIEUR',
                  aoc='APPELLATION BORDEAUX SUPÉRIEUR CONTRÔLÉE'),
    'Reserve': dict(glass=(6, 8, 7), paper=(22, 20, 18), ink=(214, 186, 128), accent=(212, 168, 82),
                    capsule=(30, 26, 24), cap_metal=0.35, band=(214, 170, 84),
                    top='RÉSERVE PRIVÉE', vintage='2015', appellation='PAUILLAC',
                    aoc='APPELLATION PAUILLAC CONTRÔLÉE'),
    'Blanc': dict(glass=(58, 70, 26), paper=(246, 243, 234), ink=(38, 58, 40), accent=(186, 150, 72),
                  capsule=(214, 186, 120), cap_metal=0.9, band=(120, 132, 70),
                  top='GRAND VIN BLANC', vintage='2021', appellation='BORDEAUX BLANC',
                  aoc='APPELLATION BORDEAUX CONTRÔLÉE'),
}

PXMM = 12  # label render resolution (px per mm)


# ------------------------------------------------------------------ noise
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


def fbm(x, y, oct_=4, seed=0):
    s, a, t = 0, 1, 0
    for o in range(oct_):
        s = s + a * vnoise(x * 2 ** o, y * 2 ** o, seed + o); t += a; a *= 0.5
    return s / t


# ------------------------------------------------------------------ labels (rendered in mm space)
def font(path, mm):
    return ImageFont.truetype(path, max(1, int(mm * PXMM)))


def text_c(d, y_mm, txt, f, fill, W, spacing=0.0):
    """Centered text with optional letter spacing (mm)."""
    if spacing:
        widths = [d.textlength(c, font=f) for c in txt]
        total = sum(widths) + spacing * PXMM * (len(txt) - 1)
        x = (W - total) / 2
        for c, w in zip(txt, widths):
            d.text((x, y_mm * PXMM), c, font=f, fill=fill, anchor='ls')
            x += w + spacing * PXMM
    else:
        d.text((W / 2, y_mm * PXMM), txt, font=f, fill=fill, anchor='ms')


def chateau(d, cx, top, w, h, ink, gold, lw):
    """Simple line drawing of a château, in px."""
    L = lambda pts, c=ink, wd=lw: d.line(pts, fill=c, width=wd, joint='curve')
    base = top + h
    bw = w * 0.62
    x0, x1 = cx - bw / 2, cx + bw / 2
    body_top = top + h * 0.45
    L([(x0, base), (x0, body_top), (x1, body_top), (x1, base)])
    # mansard roof
    L([(x0, body_top), (x0 + bw * 0.06, top + h * 0.30), (x1 - bw * 0.06, top + h * 0.30), (x1, body_top)])
    # central pavilion with pediment
    pw = bw * 0.26
    L([(cx - pw / 2, base), (cx - pw / 2, top + h * 0.38), (cx + pw / 2, top + h * 0.38), (cx + pw / 2, base)])
    L([(cx - pw / 2 - lw, top + h * 0.38), (cx, top + h * 0.20), (cx + pw / 2 + lw, top + h * 0.38)], gold, lw)
    d.ellipse([cx - pw * 0.12, top + h * 0.27, cx + pw * 0.12, top + h * 0.34], outline=gold, width=max(1, lw // 2))
    # door
    d.rectangle([cx - pw * 0.16, base - h * 0.22, cx + pw * 0.16, base], outline=ink, width=lw)
    # towers
    for sx in (-1, 1):
        tx = cx + sx * (bw / 2 + w * 0.07)
        tw = w * 0.13
        L([(tx - tw / 2, base), (tx - tw / 2, top + h * 0.36), (tx + tw / 2, top + h * 0.36), (tx + tw / 2, base)])
        L([(tx - tw / 2 - lw, top + h * 0.36), (tx, top), (tx + tw / 2 + lw, top + h * 0.36)])
        d.line([(tx, top), (tx, top - h * 0.08)], fill=gold, width=max(1, lw // 2))
        for wy in (0.50, 0.70):
            d.rectangle([tx - tw * 0.18, top + h * wy, tx + tw * 0.18, top + h * (wy + 0.1)], outline=ink, width=max(1, lw // 2))
    # windows on the main block
    for i in range(6):
        if i in (2, 3):
            continue
        wx = x0 + bw * (0.09 + i * 0.165)
        for wy in (0.52, 0.74):
            d.rectangle([wx, top + h * wy, wx + bw * 0.07, top + h * (wy + 0.13)], outline=ink, width=max(1, lw // 2))
    # dormers
    for i in (0, 1, 4, 5):
        wx = x0 + bw * (0.09 + i * 0.165) + bw * 0.035
        L([(wx - bw * 0.03, top + h * 0.42), (wx, top + h * 0.33), (wx + bw * 0.03, top + h * 0.42)])
    # ground line and vines
    L([(cx - w / 2, base), (cx + w / 2, base)])
    for i in range(9):
        vx = cx - w / 2 + w * (i + 0.5) / 9
        d.line([(vx, base + h * 0.05), (vx, base + h * 0.13)], fill=ink, width=max(1, lw // 2))
        d.ellipse([vx - h * 0.03, base + h * 0.03, vx + h * 0.03, base + h * 0.08], outline=gold, width=max(1, lw // 2))


def front_label(v):
    Wmm = 2 * math.pi * PROFILE_MM[4][0] * (2 * LABEL_HALF_DEG) / 360
    Hmm = LABEL_Z_MM[1] - LABEL_Z_MM[0]
    W, H = int(Wmm * PXMM), int(Hmm * PXMM)
    col = Image.new('RGB', (W, H), v['paper'])
    gold = Image.new('L', (W, H), 0)          # gold foil mask (metallic + embossed)
    dc, dg = ImageDraw.Draw(col), ImageDraw.Draw(gold)
    ink, acc = v['ink'], v['accent']

    def both(fn):        # draw in gold on colour + mask
        fn(dc, acc); fn(dg, 255)

    m = PXMM
    both(lambda d, c: d.rectangle([3 * m, 3 * m, W - 3 * m, H - 3 * m], outline=c, width=int(0.7 * m)))
    dc.rectangle([4.6 * m, 4.6 * m, W - 4.6 * m, H - 4.6 * m], outline=ink, width=int(0.25 * m))
    text_c(dc, 12.5, v['top'], font(F_REG, 3.2), ink, W, spacing=0.9)
    # château drawing: ink lines with gold accents (accents also go to the gold mask)
    chateau(dc, W / 2, 17 * m, 62 * m, 25 * m, ink, acc, int(0.45 * m))
    g2 = Image.new('L', (W, H), 0)
    chateau(ImageDraw.Draw(g2), W / 2, 17 * m, 62 * m, 25 * m, 0, 255, int(0.45 * m))
    gold = Image.fromarray(np.maximum(np.asarray(gold), np.asarray(g2)))
    dg = ImageDraw.Draw(gold)
    text_c(dc, 54.5, 'CHÂTEAU', font(F_REG, 5.2), ink, W, spacing=1.6)
    both(lambda d, c: text_c(d, 66.0, 'LOOPLAND', font(F_BOLD, 10.5), c, W, spacing=0.5))
    for d, c in ((dc, acc), (dg, 255)):
        d.line([(W / 2 - 26 * m, 70 * m), (W / 2 - 3 * m, 70 * m)], fill=c, width=int(0.35 * m))
        d.line([(W / 2 + 3 * m, 70 * m), (W / 2 + 26 * m, 70 * m)], fill=c, width=int(0.35 * m))
        d.polygon([(W / 2, 68.6 * m), (W / 2 + 1.4 * m, 70 * m), (W / 2, 71.4 * m), (W / 2 - 1.4 * m, 70 * m)], fill=c)
    both(lambda d, c: text_c(d, 78.5, v['vintage'], font(F_BOLD, 6.5), c, W, spacing=0.6))
    text_c(dc, 84.0, v['appellation'], font(F_REG, 3.2), ink, W, spacing=0.5)
    text_c(dc, 87.6, v['aoc'], font(F_REG, 1.8), ink, W, spacing=0.15)
    f = font(F_REG, 1.9)
    dc.text((9 * m, 91.2 * m), '13,5% VOL', font=f, fill=ink, anchor='ls')
    dc.text((W - 9 * m, 91.2 * m), '750 ML', font=f, fill=ink, anchor='rs')
    text_c(dc, 91.2, 'MIS EN BOUTEILLE AU CHÂTEAU', font(F_IT, 1.9), ink, W)
    return col, gold, Wmm, Hmm


def back_label(v):
    Wmm = 2 * math.pi * PROFILE_MM[4][0] * (2 * BACK_HALF_DEG) / 360
    Hmm = BACK_Z_MM[1] - BACK_Z_MM[0]
    W, H = int(Wmm * PXMM), int(Hmm * PXMM)
    col = Image.new('RGB', (W, H), v['paper'])
    gold = Image.new('L', (W, H), 0)
    dc = ImageDraw.Draw(col); m = PXMM; ink = v['ink']
    dc.rectangle([2 * m, 2 * m, W - 2 * m, H - 2 * m], outline=v['accent'], width=int(0.4 * m))
    ImageDraw.Draw(gold).rectangle([2 * m, 2 * m, W - 2 * m, H - 2 * m], outline=255, width=int(0.4 * m))
    text_c(dc, 8, 'CHÂTEAU LOOPLAND', font(F_BOLD, 3.0), ink, W, spacing=0.3)
    text_c(dc, 11.5, v['appellation'] + ' · ' + v['vintage'], font(F_REG, 2.0), ink, W)
    lines = ['Issu de vieilles vignes et élevé', 'dix-huit mois en fût de chêne,', 'ce vin révèle des arômes de cassis,',
             'de cèdre et de tabac blond.', '', 'Servir entre 16 et 18 °C.', 'Produit de France — Contient des sulfites']
    f = font(F_IT, 1.75)
    for i, t in enumerate(lines):
        text_c(dc, 17 + i * 2.6, t, f, ink, W)
    # barcode
    rng = np.random.default_rng(7)
    x = W / 2 - 12 * m
    while x < W / 2 + 12 * m:
        bw = rng.integers(1, 4) * 0.22 * m
        dc.rectangle([x, 39 * m, x + bw, 50 * m], fill=ink)
        x += bw + rng.integers(1, 4) * 0.22 * m
    text_c(dc, 52.5, '3 701234 567897', font(F_REG, 1.8), ink, W, spacing=0.4)
    text_c(dc, 56.5, '750 ML    13,5% VOL', font(F_REG, 1.8), ink, W)
    return col, gold, Wmm, Hmm


# ------------------------------------------------------------------ atlas geometry
yy, xx = np.mgrid[0:N, 0:N]
U = (xx + 0.5) / N
Vv = 1 - (yy + 0.5) / N
zt = np.linspace(0, 299.4, 3000)
vt = np.array([side_v_of_z(z) for z in zt])
side = Vv >= SIDE_V[0] - 6.0 / N
Z = np.interp(Vv, vt, zt)                            # mm
pts, _ = side_rings()
R = np.interp(Z, [p[1] for p in pts], [p[0] for p in pts])
TH = U * 2 * math.pi

def disc(d):
    cu, cv, rad = d
    rr = np.hypot(U - cu, Vv - cv) / rad
    return rr, np.arctan2(Vv - cv, U - cu)

rb, ab = disc(BOTTOM_DISC); bottom = rb <= 1.03
rt, at = disc(TOP_DISC); topd = rt <= 1.03

# physical mm per texel (for normal maps)
mm_u = np.where(side, 2 * math.pi * np.maximum(R, 1) / N, 1.0)
mm_v = np.where(side, 1.0 / (np.interp(Vv, vt, np.gradient(vt, zt)) * N + 1e-9), 1.0)
mm_v = np.where(bottom, 31.0 / (BOTTOM_DISC[2] * N), mm_v); mm_u = np.where(bottom, 31.0 / (BOTTOM_DISC[2] * N), mm_u)
mm_v = np.where(topd, 14.6 / (TOP_DISC[2] * N), mm_v); mm_u = np.where(topd, 14.6 / (TOP_DISC[2] * N), mm_u)


def place(img, u0, u1, z0, z1, mode):
    """Resample a label image into its UV rectangle; returns full-size float array."""
    x0, x1 = u0 * N, u1 * N
    y0, y1 = (1 - side_v_of_z(z1)) * N, (1 - side_v_of_z(z0)) * N
    ix0, iy0 = int(math.floor(x0)), int(math.floor(y0))
    w, h = int(math.ceil(x1)) - ix0, int(math.ceil(y1)) - iy0
    # supersample onto the exact sub-pixel rectangle
    big = img.resize((w * 4, h * 4), Image.LANCZOS)
    canvas = Image.new(img.mode, (w * 4, h * 4), 0 if mode != 'RGB' else (0, 0, 0))
    canvas.paste(big)
    sm = canvas.resize((w, h), Image.BOX)
    out = np.zeros((N, N) + ((3,) if mode == 'RGB' else ()), np.float64)
    out[iy0:iy0 + h, ix0:ix0 + w] = np.asarray(sm, np.float64) / 255
    return out


def to_lin(c):
    c = np.asarray(c, np.float64) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


FRONT_U = (0.75 - LABEL_HALF_DEG / 360, 0.75 + LABEL_HALF_DEG / 360)   # faces Blender -Y
BACK_U = (0.25 - BACK_HALF_DEG / 360, 0.25 + BACK_HALF_DEG / 360)

# shared noise
n_glass = fbm(U * 3, Z / 40, 4, 1)
n_paper = fbm(xx / 3.0, yy / 3.0, 3, 9)
n_crinkle = fbm(U * 26, Z / 2.2, 4, 13) + 0.5 * fbm(U * 70, Z / 0.8, 3, 17)

for name, v in VARIANTS.items():
    fc, fg, _, _ = front_label(v)
    bc, bg, _, _ = back_label(v)
    lab_mask = place(Image.new('L', fc.size, 255), FRONT_U[0], FRONT_U[1], *LABEL_Z_MM, 'L') * side
    back_mask = place(Image.new('L', bc.size, 255), BACK_U[0], BACK_U[1], *BACK_Z_MM, 'L') * side
    lab_col = place(fc, FRONT_U[0], FRONT_U[1], *LABEL_Z_MM, 'RGB') + place(bc, BACK_U[0], BACK_U[1], *BACK_Z_MM, 'RGB')
    lab_gold = place(fg, FRONT_U[0], FRONT_U[1], *LABEL_Z_MM, 'L') + place(bg, BACK_U[0], BACK_U[1], *BACK_Z_MM, 'L')
    lab_a = np.clip(lab_mask + back_mask, 0, 1)

    # ---- glass
    glass = to_lin(v['glass']) * (0.85 + 0.3 * n_glass)[..., None]
    col = glass
    metal = np.zeros((N, N)); smooth = np.full((N, N), 0.93)
    height = np.zeros((N, N))
    # mould seams at 0 and 180 degrees
    for ang in (0.0, 0.5, 1.0):
        height += 0.03 * np.exp(-((U - ang) * 2 * math.pi * R / 0.25) ** 2) * side * (Z < 244)
    height += 0.02 * (n_glass - 0.5)

    # ---- bottom disc: punt with stippled resting ring
    stip = (np.sin(ab * 60) > 0.6) * (np.abs(rb * 31 - 28.5) < 1.6)
    col = np.where(bottom[..., None], glass * 0.8, col)
    smooth = np.where(bottom, 0.8 - 0.3 * stip, smooth)
    height = np.where(bottom, 0.12 * stip, height)

    # ---- labels
    paper_lin = lab_col ** 2.2
    grain = (0.94 + 0.12 * n_paper)[..., None]
    gold_lin = to_lin(v['accent'])
    paper_mix = paper_lin * grain
    paper_mix = paper_mix * (1 - lab_gold[..., None]) + gold_lin * lab_gold[..., None]
    col = col * (1 - lab_a[..., None]) + paper_mix * lab_a[..., None]
    metal = metal * (1 - lab_a) + lab_gold * lab_a
    smooth = smooth * (1 - lab_a) + (0.28 + 0.08 * n_paper + 0.42 * lab_gold) * lab_a
    height = height * (1 - lab_a) + (0.25 + 0.06 * lab_gold + 0.02 * n_paper) * lab_a

    # ---- capsule (foil) on the side + top disc
    cap = side & (Z >= CAPSULE_Z_MM)
    capf = cap.astype(float)
    band = (np.abs(Z - 251.5) < 1.3) & cap
    band2 = (np.abs(Z - 281.0) < 0.6) & cap
    crimp = (Z > 288.5) & cap
    ribs = 0.5 + 0.5 * np.cos(U * 2 * math.pi * 60)
    cap_col = to_lin(v['capsule']) * (0.9 + 0.2 * n_crinkle)[..., None]
    cap_col = np.where((band | band2)[..., None], to_lin(v['band']), cap_col)
    col = np.where(cap[..., None], cap_col, col)
    metal = np.where(cap, np.where(band | band2, 1.0, v['cap_metal']), metal)
    smooth = np.where(cap, 0.62 + 0.1 * (n_crinkle - 0.5) + 0.12 * (band | band2), smooth)
    cap_h = 0.35 + 0.05 * n_crinkle + 0.06 * (band | band2) + crimp * 0.08 * ribs
    cap_h = cap_h + 0.4 * np.clip((Z - CAPSULE_Z_MM) / 0.6, 0, 1) - 0.4    # skirt edge
    height = np.where(cap, cap_h, height)
    # top disc: embossed seal ring and LL monogram
    mono = Image.new('L', (512, 512), 0); dm = ImageDraw.Draw(mono)
    dm.ellipse([40, 40, 472, 472], outline=255, width=14)
    dm.ellipse([78, 78, 434, 434], outline=255, width=5)
    dm.text((256, 270), 'LL', font=ImageFont.truetype(F_BOLD, 230), fill=255, anchor='mm')
    mono = np.asarray(mono.resize((int(2 * TOP_DISC[2] * N), int(2 * TOP_DISC[2] * N)), Image.LANCZOS), np.float64) / 255
    mfull = np.zeros((N, N)); s0 = int((1 - TOP_DISC[1] - TOP_DISC[2]) * N); s1 = int((TOP_DISC[0] - TOP_DISC[2]) * N)
    mfull[s0:s0 + mono.shape[0], s1:s1 + mono.shape[1]] = mono
    mfull *= topd
    top_col = to_lin(v['capsule']) * (0.9 + 0.2 * n_crinkle)[..., None]
    top_col = top_col * (1 - mfull[..., None]) + to_lin(v['band']) * mfull[..., None]
    col = np.where(topd[..., None], top_col, col)
    metal = np.where(topd, v['cap_metal'] + (1 - v['cap_metal']) * mfull, metal)
    smooth = np.where(topd, 0.62 + 0.1 * mfull, smooth)
    height = np.where(topd, 0.35 + 0.08 * mfull + 0.04 * n_crinkle, height)

    # ---- outputs
    alb = (to_srgb(col) * 255).round().astype(np.uint8)
    Image.fromarray(alb).save(os.path.join(OUT, f'WineBottle_{name}_Albedo.png'), optimize=True)
    ms = np.zeros((N, N, 4), np.uint8)
    ms[..., 0] = ms[..., 1] = ms[..., 2] = (np.clip(metal, 0, 1) * 255).astype(np.uint8)
    ms[..., 3] = (np.clip(smooth, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(ms, 'RGBA').save(os.path.join(OUT, f'WineBottle_{name}_MetallicSmoothness.png'), optimize=True)
    gx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) / 2 / mm_u
    gy = (np.roll(height, 1, 0) - np.roll(height, -1, 0)) / 2 / mm_v          # +v is up the image
    nx, ny = -gx, -gy
    ln = np.sqrt(nx ** 2 + ny ** 2 + 1)
    nm = np.stack([nx / ln, ny / ln, 1 / ln], -1)
    Image.fromarray(((nm + 1) * 127.5).round().astype(np.uint8)).save(
        os.path.join(OUT, f'WineBottle_{name}_Normal.png'), optimize=True)
    print('wrote', name)
