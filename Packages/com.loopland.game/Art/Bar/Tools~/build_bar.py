"""Build the pub bar set in Blender: counter, stool, back bar (with wine bottles), pendant lamp.

Sizes follow the original Bar.fbx (6.535 x 1.149 x 1.61 m), about 1.5x real-world scale.
Usage: python build_bar.py -- <texdir> <bottle.blend> <outdir>
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix

TEX, BOTTLE_BLEND, OUT = sys.argv[sys.argv.index('--') + 1:][:3]
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
SC = bpy.context.scene

WOOD_TILE, SMALL_TILE = 1.2, 0.5

# ------------------------------------------------------------------ materials (Blender preview set-up)
MATS = {}


def material(name, tex, tint=(1, 1, 1), emission=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; nodes, links = nt.nodes, nt.links
    b = nodes['Principled BSDF']
    def img(fn, cs):
        t = nodes.new('ShaderNodeTexImage')
        t.image = bpy.data.images.get(fn) or bpy.data.images.load(os.path.join(TEX, fn))
        t.image.colorspace_settings.name = cs
        return t
    a = img(f'{tex}_Albedo.png', 'sRGB')
    mul = nodes.new('ShaderNodeMix'); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'
    mul.inputs['Factor'].default_value = 1.0; mul.inputs[7].default_value = (*tint, 1)
    links.new(a.outputs['Color'], mul.inputs[6]); links.new(mul.outputs[2], b.inputs['Base Color'])
    ms = img(f'{tex}_MetallicSmoothness.png', 'Non-Color')
    sep = nodes.new('ShaderNodeSeparateColor'); links.new(ms.outputs['Color'], sep.inputs['Color'])
    links.new(sep.outputs['Red'], b.inputs['Metallic'])
    inv = nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1
    links.new(ms.outputs['Alpha'], inv.inputs[1]); links.new(inv.outputs[0], b.inputs['Roughness'])
    nm = img(f'{tex}_Normal.png', 'Non-Color')
    nmap = nodes.new('ShaderNodeNormalMap'); links.new(nm.outputs['Color'], nmap.inputs['Color'])
    links.new(nmap.outputs['Normal'], b.inputs['Normal'])
    if emission:
        b.inputs['Emission Color'].default_value = (*emission[0], 1)
        b.inputs['Emission Strength'].default_value = emission[1]
    MATS[name] = m
    return m


material('Bar_Mahogany', 'Bar_Mahogany')
material('Bar_MahoganyTop', 'Bar_Mahogany', tint=(0.72, 0.68, 0.66))
material('Bar_Brass', 'Bar_Brass')
material('Bar_Leather', 'Bar_Leather')
material('Bar_FrostedGlass', 'Bar_FrostedGlass', emission=((1.0, 0.72, 0.42), 1.6))
material('Bar_Mirror', 'Bar_Mirror')

# ------------------------------------------------------------------ mesh helpers
def new_obj(name, bm, mat):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    SC.collection.objects.link(ob)
    me.materials.append(MATS[mat] if isinstance(mat, str) else mat)
    return ob


def box_uv(bm, tile, grain=0):
    """Per-face box projection in world space; wood grain (texture U) runs along axis `grain`."""
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        k = max(range(3), key=lambda i: abs(n[i]))
        ab = [i for i in range(3) if i != k]
        if grain in ab:
            ua, va = grain, [i for i in ab if i != grain][0]
        else:
            ua, va = ab
        for l in f.loops:
            l[uv].uv = (l.vert.co[ua] / tile, l.vert.co[va] / tile)


def box(name, lo, hi, mat, bevel=0.0, seg=2, grain=0, tile=WOOD_TILE):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector([lo[i] + (v.co[i] + 0.5) * (hi[i] - lo[i]) for i in range(3)])
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=seg, profile=0.5, affect='EDGES')
    bm.normal_update()
    box_uv(bm, tile, grain)
    return new_obj(name, bm, mat)


def lathe(name, prof, mat, segs=24, tile=SMALL_TILE, closed=False, origin=(0, 0, 0), rot=None, grain_along=False):
    """Spin a (r, z) profile around Z. U = around (metres), V = along profile (swapped with grain_along)."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap')
    pts = prof + ([prof[0]] if closed else [])
    s = [0.0]
    for (r0, z0), (r1, z1) in zip(pts, pts[1:]):
        s.append(s[-1] + math.hypot(r1 - r0, z1 - z0))
    rings = []
    for (r, z) in pts:
        rings.append([bm.verts.new((r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), z))
                      for k in range(segs)])
    rmax = max(r for r, _ in prof)
    for i in range(len(pts) - 1):
        for k in range(segs):
            k1 = (k + 1) % segs
            a, b, c, d = rings[i][k], rings[i][k1], rings[i + 1][k1], rings[i + 1][k]
            if a.co == b.co and d.co == c.co:
                continue
            try:
                f = bm.faces.new((a, b, c, d) if not (a.co - b.co).length < 1e-9 else (a, c, d))
            except ValueError:
                continue
            for l in f.loops:
                kk = rings[i].index(l.vert) if l.vert in rings[i] else rings[i + 1].index(l.vert)
                kk = segs if (kk == 0 and k1 == 0 and k == segs - 1) else kk
                ii = i if l.vert in rings[i] else i + 1
                uu, vv = kk / segs * 2 * math.pi * rmax / tile, s[ii] / tile
                l[uv].uv = (vv, uu) if grain_along else (uu, vv)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    M = Matrix.Translation(origin) @ (rot.to_4x4() if rot is not None else Matrix())
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    return new_obj(name, bm, mat)


def spine(L, xe, front=True, arc=(-90, 90), back=True, x0=None, n_arc=28):
    """Centre-line samples (c, d): point = c + R*d. Front runs along -Y, the round end is at +X."""
    x0 = -L / 2 if x0 is None else x0
    out = []
    if front:
        for x in (x0, xe):
            out.append((Vector((x, 0, 0)), Vector((0, -1, 0))))
    a0, a1 = arc
    n = max(2, int(n_arc * (a1 - a0) / 180))
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        out.append((Vector((xe, 0, 0)), Vector((math.cos(a), math.sin(a), 0))))
    if back:
        out.append((Vector((x0, 0, 0)), Vector((0, 1, 0))))
    # drop duplicates where straight runs meet the arc
    clean = [out[0]]
    for c, d in out[1:]:
        pc, pd = clean[-1]
        if (pc + pd - c - d).length > 1e-6 or (pd - d).length > 1e-6:
            clean.append((c, d))
    return clean


def sweep(name, sp, R, prof, mat, closed_prof=False, tile=WOOD_TILE, cap_ends=False):
    """Sweep a (n, z) profile (n = outward offset from radius R) along a spine."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap')
    pts = prof + ([prof[0]] if closed_prof else [])
    sv = [0.0]
    for (n0, z0), (n1, z1) in zip(pts, pts[1:]):
        sv.append(sv[-1] + math.hypot(n1 - n0, z1 - z0))
    grid = []
    for c, d in sp:
        grid.append([bm.verts.new(c + (R + n) * d + Vector((0, 0, z))) for n, z in pts])
    su = [[0.0] * len(pts)]
    for i in range(1, len(grid)):
        su.append([su[-1][j] + (grid[i][j].co - grid[i - 1][j].co).length for j in range(len(pts))])
    for i in range(len(grid) - 1):
        for j in range(len(pts) - 1):
            f = bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
            for l in f.loops:
                ii = i if l.vert in grid[i] else i + 1
                jj = grid[ii].index(l.vert)
                l[uv].uv = (su[ii][jj] / tile, sv[jj] / tile)
    if cap_ends:
        # a spine that starts and ends on the wall plane (x0) gets one end panel joining both runs
        if abs(grid[0][0].co.x - grid[-1][0].co.x) < 1e-6 and abs(sp[0][1].y + sp[-1][1].y) < 1e-6:
            caps = [grid[0] + list(reversed(grid[-1]))]
        else:
            caps = [g for g in (list(reversed(grid[0])), grid[-1]) if len(g) >= 3]
        for g in caps:
            try:
                f = bm.faces.new(g)
            except ValueError:
                continue
            for l in f.loops:
                l[uv].uv = (l.vert.co.y / tile, l.vert.co.z / tile)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_obj(name, bm, mat)


def fill_outline(name, sp, R, z, mat, down=False, tile=WOOD_TILE):
    bm = bmesh.new()
    vs = [bm.verts.new(c + R * d + Vector((0, 0, z))) for c, d in sp]
    f = bm.faces.new(vs if not down else list(reversed(vs)))
    bm.normal_update()
    if (f.normal.z < 0) != down:
        f.normal_flip()
    bmesh.ops.triangulate(bm, faces=[f])
    box_uv(bm, tile, 0)
    return new_obj(name, bm, mat)


def extrude_profile(name, prof2d, frame, th, mat, tile=WOOD_TILE, grain=2):
    """Extrude a closed 2D profile (n, z) laterally; frame = (origin c, outward d) at a spine point."""
    c, d = frame
    t = Vector((-d.y, d.x, 0))
    bm = bmesh.new()
    def P(n, z, s):
        return c + n * d + Vector((0, 0, z)) + s * t
    f0 = bm.faces.new([bm.verts.new(P(n, z, -th / 2)) for n, z in prof2d])
    r = bmesh.ops.extrude_face_region(bm, geom=[f0])
    for v in [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]:
        v.co += t * th
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    box_uv(bm, tile, grain)
    return new_obj(name, bm, mat)


def oriented(ob, frame, n_off, z):
    """Place an object built around the origin (X = along, Y = outward(-), Z up) at a spine frame."""
    c, d = frame
    t = Vector((-d.y, d.x, 0))
    M = Matrix(((t.x, -d.x, 0, 0), (t.y, -d.y, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1)))
    ob.data.transform(M)
    ob.data.transform(Matrix.Translation(c + n_off * d + Vector((0, 0, z))))
    return ob


def finish(name, parts, angle=35):
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    ob = bpy.context.object; ob.name = name; ob.data.name = name
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bm.to_mesh(ob.data); bm.free()
    for p in ob.data.polygons:
        p.use_smooth = True
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))
    ob.select_set(False)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f'{name}: {len(ob.data.vertices)} verts, {tris} tris, materials {[m.name for m in ob.data.materials]}')
    return ob


# ================================================================== COUNTER
L, H = 6.535, 1.61
RT, RB = 0.575, 0.457          # top / base half-depths (from the original Bar.fbx)
T = 0.075
HB = H - T
XE = L / 2 - RT                # centre of the round end
KICK = 0.12
parts = []
full = spine(L, XE)
cust = spine(L, XE, back=False, arc=(-90, 0))         # customer side: front + first quarter of the round end
cust_full = spine(L, XE, back=False, arc=(-90, 60))

# carcass, toe kick and skirting
parts.append(sweep('carcass', full, RB, [(0, KICK), (0, HB)], 'Bar_Mahogany', cap_ends=True))
parts.append(sweep('kick', full, RB - 0.05, [(0, 0), (0, KICK)], 'Bar_Mahogany', cap_ends=True))
parts.append(fill_outline('kick_floor', full, RB - 0.05, 0.0, 'Bar_Mahogany', down=True))
parts.append(sweep('skirting', full, RB, [(0, KICK), (0.03, KICK), (0.03, KICK + 0.07), (0.018, KICK + 0.085),
                                            (0.012, KICK + 0.11), (0, KICK + 0.115)], 'Bar_Mahogany', cap_ends=True))
# frieze under the top + panel rails
parts.append(sweep('frieze', full, RB, [(0, 1.38), (0.02, 1.38), (0.02, 1.41), (0.034, 1.425), (0.034, 1.49),
                                          (0.05, 1.51), (0.05, HB), (0, HB)], 'Bar_Mahogany', cap_ends=True))
for z0, z1 in ((0.235, 0.30), (1.31, 1.37)):
    parts.append(sweep('rail', cust_full, RB, [(0, z0), (0.022, z0 + 0.008), (0.022, z1 - 0.008), (0, z1)],
                       'Bar_Mahogany', cap_ends=True))
# bar top with a bullnose edge
top_prof = [(-0.118, HB), (-0.03, HB), (-0.012, HB + 0.006), (-0.002, HB + 0.022), (0, HB + 0.037),
            (-0.002, HB + 0.052), (-0.01, HB + 0.064), (-0.024, HB + 0.072), (-0.045, H)]
parts.append(sweep('top_edge', full, RT, top_prof, 'Bar_MahoganyTop', cap_ends=True))
parts.append(fill_outline('top_face', full, RT - 0.045, H, 'Bar_MahoganyTop'))

# stiles, raised panels and corbels on the customer side
straight = cust[0][0].x, cust[1][0].x
bays = 7
bw = (straight[1] - straight[0]) / bays
stile_frames = []
for i in range(bays + 1):
    x = straight[0] + i * bw
    stile_frames.append((Vector((x, 0, 0)), Vector((0, -1, 0))))
for a in (-60, -30, 0, 30, 60):
    r = math.radians(a)
    stile_frames.append((Vector((XE, 0, 0)), Vector((math.cos(r), math.sin(r), 0))))
for k, fr in enumerate(stile_frames):
    st = box(f'stile{k}', (-0.045, -0.026, 0.30), (0.045, 0, 1.31), 'Bar_Mahogany', bevel=0.006, grain=2)
    parts.append(oriented(st, fr, RB, 0))
    corbel = [(0.012, HB), (0.118, HB), (0.118, HB - 0.022), (0.104, HB - 0.036), (0.09, HB - 0.06),
              (0.078, HB - 0.095), (0.07, HB - 0.135)]
    cx, cz, rr = 0.052, HB - 0.18, 0.034
    for a in range(30, -181, -30):
        corbel.append((cx + rr * math.cos(math.radians(a)), cz + rr * math.sin(math.radians(a))))
    corbel.append((0.012, cz - 0.01))
    parts.append(extrude_profile(f'corbel{k}', corbel, (fr[0] + RB * fr[1], fr[1]), 0.07, 'Bar_Mahogany'))
    # carved rosette on both faces of the scroll
    for side in (-1, 1):
        ros = lathe(f'rosette{k}{side}', [(0, 0.012), (0.012, 0.011), (0.024, 0.006), (0.027, 0.0)], 'Bar_Mahogany',
                    segs=12, tile=WOOD_TILE)
        c, d = fr; t = Vector((-d.y, d.x, 0))
        q = t.to_track_quat('Z', 'Y') if side > 0 else (-t).to_track_quat('Z', 'Y')
        ros.data.transform(q.to_matrix().to_4x4())
        ros.data.transform(Matrix.Translation(c + (RB + cx) * d + Vector((0, 0, cz)) + side * t * 0.035))
        parts.append(ros)
for i in range(bays):
    x0, x1 = straight[0] + i * bw + 0.045, straight[0] + (i + 1) * bw - 0.045
    parts.append(box(f'panel{i}', (x0 + 0.02, -RB - 0.018, 0.33), (x1 - 0.02, -RB + 0.001, 1.28), 'Bar_Mahogany',
                     bevel=0.04, seg=1, grain=2))
    parts.append(box(f'panelmould{i}', (x0, -RB - 0.008, 0.30), (x1, -RB + 0.001, 1.31), 'Bar_Mahogany',
                     bevel=0.006, grain=2))

# brass foot rail with brackets
rail_sp = spine(L, XE, back=False, arc=(-90, 55), x0=-L / 2 + 0.15)
circle = [(0.21 + 0.03 * math.cos(2 * math.pi * k / 10), 0.30 + 0.03 * math.sin(2 * math.pi * k / 10)) for k in range(10)]
parts.append(sweep('footrail', rail_sp, RB, circle, 'Bar_Brass', closed_prof=True, tile=SMALL_TILE))
for fr in (rail_sp[0], rail_sp[-1]):
    cap = lathe('railcap', [(0, -0.035), (0.03, -0.02), (0.031, 0.0), (0.03, 0.02), (0, 0.035)], 'Bar_Brass', segs=10)
    c, d = fr; t = Vector((-d.y, d.x, 0))
    cap.data.transform(t.to_track_quat('Z', 'Y').to_matrix().to_4x4())
    cap.data.transform(Matrix.Translation(c + (RB + 0.21) * d + Vector((0, 0, 0.30))))
    parts.append(cap)
brackets = [(Vector((x, 0, 0)), Vector((0, -1, 0))) for x in [straight[0] + 0.5 + i * 1.05 for i in range(6)]]
brackets += [(Vector((XE, 0, 0)), Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0))) for a in (-45, 20)]
for fr in brackets:
    arm = lathe('arm', [(0.05, 0), (0.05, 0.012), (0.016, 0.02), (0.014, 0.2), (0, 0.205)], 'Bar_Brass', segs=10)
    c, d = fr
    arm.data.transform(d.to_track_quat('Z', 'Y').to_matrix().to_4x4())
    arm.data.transform(Matrix.Translation(c + RB * d + Vector((0, 0, 0.30))))
    parts.append(arm)
counter = finish('Bar_Counter', parts)

# ================================================================== STOOL
parts = []
SEAT = 1.14
parts.append(lathe('cushion', [(0, SEAT), (0.2, SEAT - 0.002), (0.255, SEAT - 0.008), (0.285, SEAT - 0.022),
                                 (0.298, SEAT - 0.045), (0.301, SEAT - 0.07), (0.298, SEAT - 0.09), (0.29, SEAT - 0.1),
                                 (0, SEAT - 0.1)], 'Bar_Leather', segs=32))
for k in range(36):
    a = 2 * math.pi * k / 36
    parts.append(lathe('nail', [(0, 0.008), (0.006, 0.006), (0.009, 0.0), (0, 0.0)], 'Bar_Brass', segs=6,
                       origin=(0.297 * math.cos(a), 0.297 * math.sin(a), SEAT - 0.083),
                       rot=Vector((math.cos(a), math.sin(a), 0)).to_track_quat('Z', 'Y').to_matrix()))
parts.append(lathe('seatbase', [(0, SEAT - 0.1), (0.285, SEAT - 0.1), (0.29, SEAT - 0.115), (0.275, SEAT - 0.14),
                                  (0.2, SEAT - 0.15), (0, SEAT - 0.15)], 'Bar_Mahogany', segs=24, tile=WOOD_TILE))
top_r, bot_r, leg_top = 0.19, 0.27, SEAT - 0.14
for k in range(4):
    a = math.pi / 4 + k * math.pi / 2
    p0 = Vector((top_r * math.cos(a), top_r * math.sin(a), leg_top))
    p1 = Vector((bot_r * math.cos(a), bot_r * math.sin(a), 0))
    ln = (p0 - p1).length
    leg = lathe('leg', [(0.0, 0.0), (0.024, 0.0), (0.026, 0.04), (0.03, ln * 0.55), (0.028, ln - 0.06), (0.032, ln - 0.03),
                        (0.03, ln), (0, ln)], 'Bar_Mahogany', segs=10, tile=WOOD_TILE, grain_along=True)
    leg.data.transform((p0 - p1).normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4())
    leg.data.transform(Matrix.Translation(p1))
    parts.append(leg)
def ring_r(z):
    return bot_r + (top_r - bot_r) * z / leg_top
parts.append(lathe('footring', [(ring_r(0.36) + 0.018 * math.cos(t), 0.36 + 0.018 * math.sin(t))
                                for t in [2 * math.pi * i / 8 for i in range(8)]], 'Bar_Brass', segs=32, closed=True))
parts.append(lathe('stretcher', [(ring_r(0.74) + 0.014 * math.cos(t), 0.74 + 0.014 * math.sin(t))
                                 for t in [2 * math.pi * i / 8 for i in range(8)]], 'Bar_Mahogany', segs=28, closed=True,
                   tile=WOOD_TILE))
stool = finish('Bar_Stool', parts)

# ================================================================== BACK BAR
parts = []
D = 0.68
BAYS = 5
yf, yb = -D / 2, D / 2
parts.append(box('bb_kick', (-L / 2, yf + 0.05, 0), (L / 2, yb, 0.1), 'Bar_Mahogany'))
parts.append(box('bb_cab', (-L / 2, yf, 0.1), (L / 2, yb, 1.35), 'Bar_Mahogany'))
parts.append(box('bb_counter', (-L / 2 - 0.02, yf - 0.045, 1.35), (L / 2 + 0.02, yb, 1.41), 'Bar_MahoganyTop', bevel=0.012))
bwb = L / BAYS
for i in range(BAYS + 1):
    x = -L / 2 + i * bwb
    x = min(max(x, -L / 2 + 0.05), L / 2 - 0.05)
    parts.append(box(f'bb_stile{i}', (x - 0.05, yf - 0.025, 0.12), (x + 0.05, yf, 1.33), 'Bar_Mahogany', bevel=0.006, grain=2))
for i in range(BAYS):
    x0, x1 = -L / 2 + i * bwb + 0.05, -L / 2 + (i + 1) * bwb - 0.05
    for (z0, z1) in ((0.17, 0.7), (0.76, 1.29)):
        parts.append(box(f'bb_panel{i}', (x0 + 0.03, yf - 0.018, z0 + 0.02), (x1 - 0.03, yf + 0.001, z1 - 0.02),
                         'Bar_Mahogany', bevel=0.045, seg=1, grain=0))
TOPZ = 3.55
SHELF_D = 0.32
ys = yb - 0.03 - SHELF_D
parts.append(box('bb_back', (-L / 2, yb - 0.025, 1.41), (L / 2, yb, TOPZ), 'Bar_Mahogany'))
parts.append(box('bb_mirror', (-L / 2 + 0.05, yb - 0.035, 1.41), (L / 2 - 0.05, yb - 0.025, TOPZ), 'Bar_Mirror',
                 tile=SMALL_TILE))
SHELVES = (1.94, 2.47, 3.0)
for i in range(BAYS + 1):
    x = -L / 2 + i * bwb
    x = min(max(x, -L / 2 + 0.06), L / 2 - 0.06)
    parts.append(box(f'pil{i}', (x - 0.06, ys - 0.03, 1.41), (x + 0.06, yb - 0.025, TOPZ), 'Bar_Mahogany', bevel=0.008, grain=2))
    parts.append(box(f'pilb{i}', (x - 0.075, ys - 0.045, 1.41), (x + 0.075, yb - 0.025, 1.5), 'Bar_Mahogany', bevel=0.01, grain=0))
    parts.append(box(f'pilc{i}', (x - 0.075, ys - 0.045, TOPZ - 0.12), (x + 0.075, yb - 0.025, TOPZ - 0.04), 'Bar_Mahogany',
                     bevel=0.01, grain=0))
for i in range(BAYS):
    x0, x1 = -L / 2 + i * bwb + 0.06, -L / 2 + (i + 1) * bwb - 0.06
    for z in SHELVES:
        parts.append(box(f'shelf{i}', (x0, ys, z), (x1, yb - 0.035, z + 0.035), 'Bar_Mahogany', bevel=0.004))
parts.append(box('crown', (-L / 2 - 0.02, ys - 0.06, TOPZ - 0.04), (L / 2 + 0.02, yb, TOPZ + 0.08), 'Bar_Mahogany', bevel=0.015))
parts.append(box('cornice', (-L / 2 - 0.05, ys - 0.11, TOPZ + 0.08), (L / 2 + 0.05, yb, TOPZ + 0.14), 'Bar_Mahogany', bevel=0.02))
backbar = finish('Bar_BackBar', parts)

# bottles (separate object, uses the WineBottle materials)
with bpy.data.libraries.load(BOTTLE_BLEND, link=False) as (src, dst):
    dst.meshes = [m for m in src.meshes if m.startswith('WineBottle_LOD')]
for v in ('Rouge', 'Reserve', 'Blanc'):
    MATS[f'WineBottle_{v}'] = bpy.data.materials.new(f'WineBottle_{v}')
    MATS[f'WineBottle_{v}'].diffuse_color = {'Rouge': (0.02, 0.06, 0.03, 1), 'Reserve': (0.02, 0.02, 0.02, 1),
                                             'Blanc': (0.25, 0.3, 0.1, 1)}[v]
rng = random.Random(4)
BOT_H = 0.45
sc_b = BOT_H / 1.0673
bparts = []
levels = [1.41] + [z + 0.035 for z in SHELVES]
for i in range(BAYS):
    x0, x1 = -L / 2 + i * bwb + 0.06, -L / 2 + (i + 1) * bwb - 0.06
    for li, z in enumerate(levels):
        n = int((x1 - x0) / 0.14)
        for k in range(n):
            if rng.random() < 0.12:
                continue
            lod = 'WineBottle_LOD1' if li < 2 else 'WineBottle_LOD2'
            me = bpy.data.meshes[lod].copy()
            ob = bpy.data.objects.new('b', me); SC.collection.objects.link(ob)
            me.materials.clear()
            me.materials.append(MATS['WineBottle_' + rng.choice(['Rouge', 'Rouge', 'Reserve', 'Blanc'])])
            M = (Matrix.Translation((x0 + 0.07 + k * (x1 - x0 - 0.14) / max(n - 1, 1), ys + SHELF_D / 2 + 0.01, z))
                 @ Matrix.Rotation(rng.uniform(-0.25, 0.25), 4, 'Z') @ Matrix.Scale(sc_b, 4))
            me.transform(M)
            bparts.append(ob)
bottles = finish('Bar_BackBarBottles', bparts, angle=50)

# ================================================================== PENDANT LAMP (origin at the ceiling)
parts = []
parts.append(lathe('canopy', [(0, 0), (0.08, 0), (0.08, -0.015), (0.06, -0.05), (0.015, -0.06), (0, -0.06)], 'Bar_Brass', segs=16))
parts.append(lathe('rod', [(0.009, -0.06), (0.009, -0.95)], 'Bar_Brass', segs=8))
parts.append(lathe('fitter', [(0, -0.93), (0.03, -0.94), (0.05, -0.98), (0.05, -1.01), (0.045, -1.02)], 'Bar_Brass', segs=16))
shade_out = [(0.045, -1.0), (0.09, -1.03), (0.15, -1.08), (0.2, -1.15), (0.235, -1.23), (0.25, -1.3)]
shade_in = [(r - 0.006, z) for r, z in reversed(shade_out)]
parts.append(lathe('shade', shade_out + [(0.244, -1.302)] + shade_in, 'Bar_FrostedGlass', segs=28))
parts.append(lathe('bulb', [(0, -1.08), (0.04, -1.1), (0.055, -1.15), (0.04, -1.2), (0, -1.21)], 'Bar_FrostedGlass', segs=12))
pendant = finish('Bar_PendantLamp', parts)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'Bar_Assets.blend'))
for ob in (counter, stool, backbar, bottles, pendant):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, ob.name + '.fbx'), use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                             axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_tspace=True,
                             add_leaf_bones=False, path_mode='STRIP', embed_textures=False)
print('done')
