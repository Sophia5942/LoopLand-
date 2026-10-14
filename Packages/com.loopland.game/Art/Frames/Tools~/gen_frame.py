"""Bake procedural ornate frame textures into the UV atlas of Frame_.fbx.

Usage: python3 -I gen_frame.py <fbx> <workdir(for fbxparse)> <outdir> <size>
"""
import sys, os
sys.path.insert(0, sys.argv[2])
from fbxparse import *  # noqa
from PIL import Image

FBX_OUT = sys.argv[3]
N = int(sys.argv[4])
os.makedirs(FBX_OUT, exist_ok=True)

nodes = parse()
g = next(find(nodes, 'Geometry'))
V = next(find([g], 'Vertices'))[1][0].reshape(-1, 3)
P = next(find([g], 'PolygonVertexIndex'))[1][0]
uvn = next(find([g], 'LayerElementUV'))
UV = next(find([uvn], 'UV'))[1][0].reshape(-1, 2)
UI = next(find([uvn], 'UVIndex'))[1][0]
MAT = next(find(nodes, 'Materials'))[1][0]

polys, cur = [], []
for v in P:
    if v < 0:
        cur.append(~v); polys.append(cur); cur = []
    else:
        cur.append(v)
corner = 0
puv, ppos = [], []
for p in polys:
    puv.append(UV[UI[corner:corner + len(p)]]); ppos.append(V[p]); corner += len(p)

# ---------------------------------------------------------------- rasterise
pid = -np.ones((N, N), np.int32)            # polygon id per texel (row, col)
yy, xx = np.mgrid[0:N, 0:N]
uu = (xx + 0.5) / N
vv = 1.0 - (yy + 0.5) / N
affine = []                                  # pos = A @ [u, v, 1]
for i, (uvq, pq) in enumerate(zip(puv, ppos)):
    M = np.c_[uvq, np.ones(len(uvq))]
    A, *_ = np.linalg.lstsq(M, pq, rcond=None)
    affine.append(A.T)
    if MAT[i] != 0:
        continue
    for tri in ((0, 1, 2), (0, 2, 3)):
        a, b, c = uvq[list(tri)]
        x0, x1 = int(min(a[0], b[0], c[0]) * N) - 1, int(max(a[0], b[0], c[0]) * N) + 2
        y0, y1 = int((1 - max(a[1], b[1], c[1])) * N) - 1, int((1 - min(a[1], b[1], c[1])) * N) + 2
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, N), min(y1, N)
        su, sv = uu[y0:y1, x0:x1], vv[y0:y1, x0:x1]
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        l1 = ((b[1] - c[1]) * (su - c[0]) + (c[0] - b[0]) * (sv - c[1])) / d
        l2 = ((c[1] - a[1]) * (su - c[0]) + (a[0] - c[0]) * (sv - c[1])) / d
        l3 = 1 - l1 - l2
        e = -1e-6
        inside = (l1 >= e) & (l2 >= e) & (l3 >= e)
        blk = pid[y0:y1, x0:x1]
        blk[inside] = i

covered = pid >= 0
# dilate polygon ids outward (padding against mip/filter bleed)
PAD = max(4, N // 512)
for _ in range(PAD):
    empty = pid < 0
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        sh = np.roll(pid, (dy, dx), axis=(0, 1))
        take = empty & (sh >= 0) & (pid < 0)
        pid[take] = sh[take]

sel = pid >= 0
ry, rx = np.nonzero(sel)
ids = pid[sel]
su = ((rx + 0.5) / N).astype(np.float64)
sv = (1.0 - (ry + 0.5) / N).astype(np.float64)
AF = np.stack(affine)                       # (npoly, 3, 3)
Ai = AF[ids]                                # (n, 3, 3)

def pos_at(u, v):
    return Ai[:, :, 0] * u[:, None] + Ai[:, :, 1] * v[:, None] + Ai[:, :, 2]

normals = []
for pq in ppos:
    n = np.cross(pq[1] - pq[0], pq[2] - pq[0]); normals.append(n / np.linalg.norm(n))
NRM = np.stack(normals)[ids]
front = NRM[:, 0] > 0.9                      # faces looking out of the frame
# metres per texel along u / v for this polygon
mpp_u = np.linalg.norm(Ai[:, :, 0], axis=1) / N
mpp_v = np.linalg.norm(Ai[:, :, 1], axis=1) / N

# ---------------------------------------------------------------- noise
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

# ---------------------------------------------------------------- ornament height (metres)
HY, HZ, ZC = 0.5689, 0.4743, 0.556
BANDS = [0.0, 0.0055, 0.0298, 0.0352, 0.0635, 0.069, 0.0933, 0.0987]

def sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)

def height(p, isfront):
    y, z = p[:, 1], p[:, 2]
    zc = np.where(z >= 0, ZC, -ZC)
    ly, lz = y, z - zc
    dy, dz = HY - np.abs(ly), HZ - np.abs(lz)
    d = np.minimum(dy, dz)
    along = np.where(dy < dz, lz, ly)
    h = np.zeros_like(d)

    # outer raised band: twisted rope
    w0, w1 = BANDS[1], BANDS[2]
    t = np.clip((d - w0) / (w1 - w0), 0, 1)
    dome = np.sqrt(np.clip(np.sin(np.pi * t), 0, 1))
    ph = along / 0.0085 + t * 1.15
    strand = np.abs(np.sin(np.pi * ph)) ** 0.6
    rope = 0.0032 * dome * (0.3 + 0.7 * strand)
    h = np.where((d >= w0) & (d < w1), rope, h)

    # middle cove: acanthus vine scroll + corner rosettes
    m0, m1 = BANDS[3], BANDS[4]
    mw = m1 - m0
    t = (d - m0) / mw
    cove = -0.0012 * np.sin(np.pi * np.clip(t, 0, 1))
    Pd = 0.046
    k = 2 * np.pi / Pd
    c = 0.5 + 0.26 * np.sin(k * along)
    slope = 0.26 * k * np.cos(k * along) * mw
    dist = np.abs(t - c) * mw / np.sqrt(1 + slope ** 2)
    stem = 0.0017 * np.sqrt(np.clip(1 - (dist / 0.0021) ** 2, 0, 1))
    orn = stem
    # leaves hang off each crest of the vine, alternating sides
    half = Pd / 2
    kidx = np.floor((along - Pd / 4) / half + 0.5)
    ac = Pd / 4 + kidx * half
    side = np.where(np.mod(kidx, 2) == 0, -1.0, 1.0)      # crest high -> leaf hangs low
    tc = 0.5 + 0.26 * np.sin(k * ac)
    lu = along - (ac + 0.0045)
    lv = (t - (tc + side * 0.30)) * mw
    ang = -side * 0.55
    ru = lu * np.cos(ang) - lv * np.sin(ang)
    rv = lu * np.sin(ang) + lv * np.cos(ang)
    theta = np.arctan2(rv, ru)
    lobes = 1 + 0.10 * np.cos(5 * theta)
    rr = np.sqrt((ru / 0.0105) ** 2 + (rv / 0.0058) ** 2) / lobes
    leaf = 0.0026 * np.sqrt(np.clip(1 - rr ** 2, 0, 1))
    vein = 1 - 0.35 * np.exp(-(rv / 0.0009) ** 2) * (rr < 1)
    side_vein = 1.0
    leaf = leaf * vein * side_vein
    # small curl (spiral bud) on the other side of each crest
    bu = along - (ac - 0.006)
    bv = (t - (tc - side * 0.18)) * mw
    br = np.sqrt(bu ** 2 + bv ** 2)
    bud = 0.0019 * np.sqrt(np.clip(1 - (br / 0.0042) ** 2, 0, 1)) * (0.75 + 0.25 * np.cos(np.arctan2(bv, bu) * 1 + br / 0.0042 * 9))
    orn = np.maximum(np.maximum(orn, leaf), bud)
    # corner rosettes
    cy = np.sign(ly) * (HY - 0.0494)
    cz = np.sign(lz) * (HZ - 0.0494)
    ry_, rz_ = ly - cy, lz - cz
    rr = np.sqrt(ry_ ** 2 + rz_ ** 2)
    th = np.arctan2(rz_, ry_)
    R = 0.0175
    pet = np.abs(np.cos(4 * th)) ** 0.7
    edge = R * (0.82 + 0.18 * pet)
    ros = 0.0045 * np.sqrt(np.clip(1 - (rr / edge) ** 2, 0, 1)) * (0.65 + 0.35 * pet)
    inner_pet = 0.0012 * np.clip(np.cos(8 * th + 0.4), 0, 1) * sstep(0.004, 0.009, rr) * (1 - sstep(0.011, 0.015, rr))
    ros = ros + np.where(rr < edge, inner_pet, 0)
    ros = np.maximum(ros, 0.0058 * np.sqrt(np.clip(1 - (rr / 0.0042) ** 2, 0, 1)))
    in_ros = rr < edge
    midband = (d >= m0) & (d < m1)
    mid = cove + orn
    h = np.where(midband, mid, h)
    h = np.where(in_ros & (d >= BANDS[2]) & (d < BANDS[5]), np.maximum(h, ros), h)

    # inner raised band: bead and reel
    b0, b1 = BANDS[5], BANDS[6]
    t = np.clip((d - b0) / (b1 - b0), 0, 1)
    Pb = 0.021
    x = np.mod(along, Pb) - Pb / 2
    bead = 0.0034 * np.sqrt(np.clip(1 - (x / 0.0071) ** 2 - ((t - 0.5) / 0.40) ** 2, 0, 1))
    xr = np.abs(x) - 0.0091
    reel = 0.0022 * np.sqrt(np.clip(1 - (xr / 0.0011) ** 2 - ((t - 0.5) / 0.30) ** 2, 0, 1))
    base = 0.0006 * np.sin(np.pi * t)
    bb = np.maximum(np.maximum(bead, reel), base)
    h = np.where((d >= b0) & (d < b1) & ~(in_ros & (d < BANDS[5] + 0.001)), bb, h)

    # fillets: tiny half-round
    for f0, f1 in ((BANDS[0], BANDS[1]), (BANDS[2], BANDS[3]), (BANDS[4], BANDS[5]), (BANDS[6], BANDS[7])):
        t = np.clip((d - f0) / (f1 - f0), 0, 1)
        fil = 0.0007 * np.sin(np.pi * t)
        h = np.where((d >= f0) & (d < f1) & ~((in_ros) & (f0 > 0.02) & (f1 < 0.07)), fil, h)

    h = np.where(isfront, h, 0.0)
    # hammered gilding micro relief everywhere
    return h

print('texels', len(ids))
CH = 1_000_000
H = np.zeros(len(ids)); GX = np.zeros(len(ids)); GY = np.zeros(len(ids)); LAP = np.zeros(len(ids))
POS = np.zeros((len(ids), 3))
for s in range(0, len(ids), CH):
    e = s + CH
    A_ = Ai[s:e]
    u0, v0 = su[s:e], sv[s:e]
    du = 1.0 / N
    def at(u, v):
        return A_[:, :, 0] * u[:, None] + A_[:, :, 1] * v[:, None] + A_[:, :, 2]
    fr = front[s:e]
    p0 = at(u0, v0)
    h0 = height(p0, fr)
    hpu = height(at(u0 + du, v0), fr); hmu = height(at(u0 - du, v0), fr)
    hpv = height(at(u0, v0 + du), fr); hmv = height(at(u0, v0 - du), fr)
    H[s:e] = h0; POS[s:e] = p0
    GX[s:e] = (hpu - hmu) / 2 / mpp_u[s:e]       # dh/du in metres per metre
    GY[s:e] = (hpv - hmv) / 2 / mpp_v[s:e]
    LAP[s:e] = (hpu + hmu + hpv + hmv - 4 * h0) / (mpp_u[s:e] ** 2)
    print('chunk', s)

np.savez_compressed(os.path.join(FBX_OUT, 'bake.npz'), ry=ry, rx=rx, ids=ids, H=H, GX=GX, GY=GY, LAP=LAP,
                    POS=POS.astype(np.float32), front=front, covered=covered[ry, rx], N=N)
print('done')
