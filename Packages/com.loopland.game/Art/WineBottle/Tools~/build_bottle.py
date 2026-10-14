"""Build the optimised wine bottle (LOD0-2) in Blender and export FBX + .blend.

Usage: python build_bottle.py -- <outdir>
"""
import bpy, bmesh, math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bottle_profile import *  # noqa

OUT = sys.argv[-1]
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)


def build(lod):
    n = SEGMENTS[lod]
    rings = [(r * SCALE, z * SCALE, t, z) for r, z, t, l in PROFILE_MM if lod in l]
    me = bpy.data.meshes.new(f'WineBottle_LOD{lod}')
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new('UVMap')
    verts = []
    for r, z, t, zmm in rings:
        if r == 0:
            verts.append([bm.verts.new((0, 0, z))])
        else:
            verts.append([bm.verts.new((r * math.cos(2 * math.pi * k / n), r * math.sin(2 * math.pi * k / n), z))
                          for k in range(n)])
    bm.verts.ensure_lookup_table()

    def disc_uv(co, disc, rmax):
        cu, cv, rad = disc
        return (cu + co.x / rmax * rad, cv + co.y / rmax * rad)

    r_bottom = max(r for r, z, t, _ in rings if t == 'bottom')
    r_top = rings[-2][0]
    for i in range(len(rings) - 1):
        a, b = verts[i], verts[i + 1]
        ta, tb = rings[i][2], rings[i + 1][2]
        for k in range(n):
            k1 = (k + 1) % n
            if len(a) == 1:            # fan from the punt apex, facing down
                f = bm.faces.new((a[0], b[k1], b[k]))
                region = 'bottom'
            elif len(b) == 1:          # fan to the capsule top centre
                f = bm.faces.new((a[k], a[k1], b[0]))
                region = 'top'
            else:
                f = bm.faces.new((a[k], a[k1], b[k1], b[k]))
                region = 'bottom' if tb == 'bottom' else 'side'
            for loop in f.loops:
                co = loop.vert.co
                if region == 'bottom':
                    loop[uv].uv = disc_uv(co, BOTTOM_DISC, r_bottom)
                elif region == 'top':
                    loop[uv].uv = disc_uv(co, TOP_DISC, r_top)
                else:
                    ang = math.atan2(co.y, co.x) % (2 * math.pi)
                    u = ang / (2 * math.pi)
                    # close the seam: the k1 == 0 column of the last quad gets u = 1
                    if loop.vert in (a[k1], b[k1]) and k1 == 0:
                        u = 1.0
                    loop[uv].uv = (u, side_v_of_z(co.z / SCALE))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(f'WineBottle_LOD{lod}', me)
    bpy.context.scene.collection.objects.link(ob)
    # smooth shading with hard edges only at the base rim / lip corners
    for p in me.polygons:
        p.use_smooth = True
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(50))
    ob.select_set(False)
    return ob


objs = [build(l) for l in (0, 1, 2)]
mat = bpy.data.materials.new('WineBottle')
for o in objs:
    o.data.materials.append(mat)
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    print(o.name, 'verts', len(o.data.vertices), 'faces', len(o.data.polygons), 'tris', tris)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'WineBottle.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'WineBottle.fbx'), use_selection=True,
                         object_types={'MESH'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         bake_space_transform=True, axis_forward='-Z', axis_up='Y',
                         mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
                         path_mode='STRIP', embed_textures=False)
print('exported')
