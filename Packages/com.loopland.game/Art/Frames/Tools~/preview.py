"""Front-view lit preview of the top frame. Usage: preview.py <fbx> <workdir> <texdir> <finish> <canvas_png|-> <out.png> [zoom]"""
import sys
sys.path.insert(0, sys.argv[2])
from fbxparse import *  # noqa
from PIL import Image

texdir, finish, canvas, outp = sys.argv[3], sys.argv[4], sys.argv[5], sys.argv[6]
zoom = len(sys.argv) > 7
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

def load(p, mode):
    return np.asarray(Image.open(p).convert(mode)).astype(np.float64) / 255

alb = load(f'{texdir}/Frame_{finish}_Albedo.png', 'RGB')
ms = load(f'{texdir}/Frame_{finish}_MetallicSmoothness.png', 'RGBA')
nm = load(f'{texdir}/Frame_Normal.png', 'RGB') * 2 - 1
ao = load(f'{texdir}/Frame_Occlusion.png', 'L')
cv = load(canvas, 'RGB') if canvas != '-' else None
TN = alb.shape[0]

W = 1400 if not zoom else 1200
if zoom:
    y0, y1, z0, z1 = -0.575, -0.30, 0.78, 1.035
else:
    y0, y1, z0, z1 = -0.6, 0.6, 0.05, 1.06
Hh = int(W * (z1 - z0) / (y1 - y0))
ys = np.linspace(y0, y1, W); zs = np.linspace(z1, z0, Hh)
YY, ZZ = np.meshgrid(ys, zs)
best = np.full(YY.shape, -9.0); out = np.zeros(YY.shape + (3,)) + np.array([0.02, 0.018, 0.016])
c = 0
for i, p in enumerate(polys):
    uvq = UV[UI[c:c + len(p)]]; pq = V[p]; c += len(p)
    n = np.cross(pq[1] - pq[0], pq[2] - pq[0]); n /= np.linalg.norm(n)
    if n[0] < 0.9:
        continue
    x = pq[:, 0].mean()
    M = np.c_[pq[:, 1], pq[:, 2], np.ones(4)]
    Auv, *_ = np.linalg.lstsq(M, uvq, rcond=None)
    Mi = np.c_[uvq, np.ones(4)]
    Ap, *_ = np.linalg.lstsq(Mi, pq, rcond=None)
    T = Ap[0] / np.linalg.norm(Ap[0]); B = Ap[1] / np.linalg.norm(Ap[1])
    # point in quad test (convex) in yz
    q = pq[:, 1:]
    inside = np.ones(YY.shape, bool)
    sgn = None
    for k in range(4):
        a, b_ = q[k], q[(k + 1) % 4]
        cr = (b_[0] - a[0]) * (ZZ - a[1]) - (b_[1] - a[1]) * (YY - a[0])
        s = np.sign(np.sum((b_[0] - a[0]) * (q[:, 1] - a[1]) - (b_[1] - a[1]) * (q[:, 0] - a[0])))
        inside &= cr * s >= -1e-9
    m = inside & (x > best)
    if not m.any():
        continue
    best[m] = x
    u = Auv[0, 0] * YY[m] + Auv[1, 0] * ZZ[m] + Auv[2, 0]
    v = Auv[0, 1] * YY[m] + Auv[1, 1] * ZZ[m] + Auv[2, 1]
    if MAT[i] == 1:
        if cv is None:
            out[m] = 0.05; continue
        cn = cv.shape[0]
        px = np.clip((u * cn).astype(int), 0, cn - 1); py = np.clip(((1 - v) * cn).astype(int), 0, cn - 1)
        col = cv[py, px] ** 2.2
        L = 0.9
        out[m] = col * L * 1.1
        continue
    px = np.clip((u * TN).astype(int), 0, TN - 1); py = np.clip(((1 - v) * TN).astype(int), 0, TN - 1)
    a = alb[py, px] ** 2.2; met = ms[py, px, 0]; sm = ms[py, px, 3]; o = ao[py, px]
    t = nm[py, px]
    wn = t[:, :1] * T + t[:, 1:2] * B + t[:, 2:3] * np.array([1.0, 0, 0])
    wn /= np.linalg.norm(wn, axis=1, keepdims=True)
    Ld = np.array([0.75, -0.45, 0.55]); Ld /= np.linalg.norm(Ld)
    Vd = np.array([1.0, 0, 0])
    Hd = (Ld + Vd); Hd /= np.linalg.norm(Hd)
    ndl = np.clip(wn @ Ld, 0, 1)[:, None]; ndh = np.clip(wn @ Hd, 0, 1)[:, None]
    power = 2 ** (sm * 11)[:, None]
    spec_col = a * met[:, None] + 0.04 * (1 - met[:, None])
    diff = a * (1 - met[:, None])
    spec = spec_col * ndh ** power * (power + 2) / 8
    # fake environment reflection: brighter from above
    r = 2 * (wn @ Vd)[:, None] * wn - Vd
    env = (0.25 + 0.55 * np.clip(r[:, 2:3] * 0.5 + 0.5, 0, 1) ** 2)
    out[m] = (diff * (ndl * 1.6 + 0.15) + spec * ndl * 1.6 + spec_col * env * (0.35 + 0.65 * sm[:, None])) * o[:, None]
img = np.clip(out, 0, 1) ** (1 / 2.2)
Image.fromarray((img * 255).astype(np.uint8)).save(outp)
