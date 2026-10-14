"""Shared wine-bottle profile and UV layout (used by the mesh builder and the texture baker).

Bordeaux 750 ml proportions (real bottle: 300 mm tall, 75 mm wide), scaled to the
size of the original Bottle_.fbx (1.0673 m tall, 0.2658 m wide). Origin = base centre, +Z up.
"""
import math

SCALE = 1.0673 / 300.0          # metres per "real" millimetre

# (r_mm, z_mm, tag) bottom -> top. tags: 'bottom' (planar UV on the base disc),
# 'side' (cylindrical UV), 'top' (planar UV on the capsule top). LOD masks pick rings per LOD.
#                r      z     tag      lods
PROFILE_MM = [
    (0.0,   9.0,  'bottom', (0, 1, 2)),   # punt apex (fan centre)
    (17.0,  5.0,  'bottom', (0, 1)),      # punt slope
    (31.0,  0.0,  'bottom', (0, 1, 2)),   # resting ring
    (35.5,  1.2,  'side',   (0,)),        # heel bevel
    (37.4,  5.5,  'side',   (0, 1, 2)),   # body start
    (37.4, 190.0, 'side',   (0, 1, 2)),   # body end
    (36.6, 201.0, 'side',   (0,)),        # shoulder
    (33.6, 212.0, 'side',   (0, 1)),
    (27.8, 222.5, 'side',   (0,)),
    (21.0, 231.0, 'side',   (0, 1, 2)),
    (16.4, 239.0, 'side',   (0,)),
    (14.9, 247.0, 'side',   (0, 1)),      # neck start (capsule starts ~245)
    (14.6, 284.0, 'side',   (0, 1, 2)),   # neck end
    (15.7, 287.0, 'side',   (0, 1)),      # lip bead (under capsule)
    (15.7, 296.0, 'side',   (0,)),
    (14.6, 299.4, 'side',   (0, 1, 2)),   # top edge
    (0.0,  300.0, 'top',    (0, 1, 2)),   # capsule top centre (fan centre)
]
SEGMENTS = {0: 24, 1: 12, 2: 8}

CAPSULE_Z_MM = 245.0
LABEL_Z_MM = (52.0, 148.0)      # front label
LABEL_HALF_DEG = 78.0           # half angular width of the front label
BACK_Z_MM = (70.0, 130.0)
BACK_HALF_DEG = 34.0

# UV atlas layout
SIDE_V = (0.165, 0.995)         # side strip, by arc length
BOTTOM_DISC = (0.085, 0.082, 0.078)   # (cu, cv, radius)
TOP_DISC = (0.255, 0.082, 0.078)


def side_rings():
    """All side rings of the full-detail profile, with cumulative arc length (mm)."""
    pts = [(r, z) for r, z, t, _ in PROFILE_MM if t == 'side']
    # side strip starts at the resting ring so the heel is part of it
    start = [(r, z) for r, z, t, _ in PROFILE_MM if t == 'bottom'][-1]
    pts = [start] + pts
    s = [0.0]
    for (r0, z0), (r1, z1) in zip(pts, pts[1:]):
        s.append(s[-1] + math.hypot(r1 - r0, z1 - z0))
    return pts, s


def side_v_of_z(z_mm):
    """v coordinate on the side strip for a point of the full profile at height z."""
    pts, s = side_rings()
    total = s[-1]
    for i in range(len(pts) - 1):
        (r0, z0), (r1, z1) = pts[i], pts[i + 1]
        if z0 <= z_mm <= z1 or i == len(pts) - 2:
            t = 0.0 if z1 == z0 else (z_mm - z0) / (z1 - z0)
            si = s[i] + t * (s[i + 1] - s[i])
            return SIDE_V[0] + (SIDE_V[1] - SIDE_V[0]) * si / total
    raise ValueError(z_mm)


def r_of_z(z_mm):
    pts, _ = side_rings()
    for (r0, z0), (r1, z1) in zip(pts, pts[1:]):
        if z0 <= z_mm <= z1:
            t = 0.0 if z1 == z0 else (z_mm - z0) / (z1 - z0)
            return r0 + t * (r1 - r0)
    return pts[-1][0]
