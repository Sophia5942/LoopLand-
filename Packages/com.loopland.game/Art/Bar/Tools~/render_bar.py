"""Assemble the bar scene (counter, stools, back bar, pendants) and render a Cycles preview.
Usage: python render_bar.py -- <Bar_Assets.blend> <bottle_tex_dir> <out.png> [samples] [save_scene.blend]
"""
import bpy, sys, math, os
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]
blend, btex, outp = a[:3]
samples = int(a[3]) if len(a) > 3 else 128
bpy.ops.wm.open_mainfile(filepath=blend)
sc = bpy.context.scene
O = bpy.data.objects

# wine bottle materials: hook up the bottle textures
for v in ('Rouge', 'Reserve', 'Blanc'):
    m = bpy.data.materials['WineBottle_' + v]; m.use_nodes = True
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    def img(fn, cs):
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(os.path.join(btex, fn))
        t.image.colorspace_settings.name = cs; return t
    al = img(f'WineBottle_{v}_Albedo.png', 'sRGB'); nt.links.new(al.outputs['Color'], b.inputs['Base Color'])
    ms = img(f'WineBottle_{v}_MetallicSmoothness.png', 'Non-Color')
    sep = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(ms.outputs['Color'], sep.inputs['Color'])
    nt.links.new(sep.outputs['Red'], b.inputs['Metallic'])
    inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1
    nt.links.new(ms.outputs['Alpha'], inv.inputs[1]); nt.links.new(inv.outputs[0], b.inputs['Roughness'])

def inst(name, loc, rotz=0.0):
    src = O[name]; o = src.copy(); o.location = loc; o.rotation_euler = (0, 0, rotz)
    sc.collection.objects.link(o); return o

AISLE = 1.15
bb_y = 0.457 + AISLE + 0.34
O['Bar_BackBar'].location = (0, bb_y, 0); O['Bar_BackBarBottles'].location = (0, bb_y, 0)
for i, x in enumerate([-2.6, -1.65, -0.7, 0.25, 1.2, 2.15]):
    inst('Bar_Stool', (x, -0.575 - 0.42, 0), rotz=0.3 * i)
O['Bar_Stool'].location = (3.55, -0.75, 0)        # one at the round end
CEIL = 4.4
for x in (-2.4, -1.1, 0.2, 1.5):
    inst('Bar_PendantLamp', (x, 0.0, CEIL))
O['Bar_PendantLamp'].location = (2.6, 0.0, CEIL)

# room
wood = bpy.data.materials['Bar_Mahogany']
def plane(loc, size, rot, mat):
    bpy.ops.mesh.primitive_plane_add(size=1, location=loc, rotation=rot)
    p = bpy.context.object; p.scale = size; p.data.materials.append(mat)
    bpy.ops.object.transform_apply(scale=True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.uv.cube_project(cube_size=1.2); bpy.ops.object.mode_set(mode='OBJECT')
    return p
floor_m = bpy.data.materials['Bar_MahoganyTop'].copy(); floor_m.name = 'floor'
plane((0, 0, 0), (20, 20, 1), (0, 0, 0), floor_m)
plane((0, bb_y + 0.36, 3), (20, 6, 1), (math.radians(90), 0, 0), wood)
plane((-L2 if (L2 := 3.27) else 0, 0, 3), (12, 6, 1), (math.radians(90), 0, math.radians(90)), wood)
ceil_m = bpy.data.materials.new('ceil'); ceil_m.diffuse_color = (0.05, 0.03, 0.02, 1)
plane((0, 0, CEIL), (20, 20, 1), (math.radians(180), 0, 0), ceil_m)

# lights: a warm point inside every shade, soft fill, back bar shelf glow
for o in [o for o in sc.objects if o.name.startswith('Bar_PendantLamp')]:
    bpy.ops.object.light_add(type='POINT', location=o.location + Vector((0, 0, -1.15)))
    l = bpy.context.object; l.data.energy = 140; l.data.color = (1.0, 0.72, 0.45); l.data.shadow_soft_size = 0.08
for z in (1.92, 2.45, 2.98, 3.5):
    bpy.ops.object.light_add(type='AREA', location=(0, bb_y + 0.1, z), rotation=(0, 0, 0))
    l = bpy.context.object; l.data.shape = 'RECTANGLE'; l.data.size = 6.2; l.data.size_y = 0.15
    l.rotation_euler = (math.radians(180), 0, 0); l.data.energy = 120; l.data.color = (1.0, 0.75, 0.5)
bpy.ops.object.light_add(type='AREA', location=(2, -5, 3.5)); f = bpy.context.object
f.data.size = 4; f.data.energy = 250; f.data.color = (1.0, 0.7, 0.5)
f.rotation_euler = (math.radians(60), 0, math.radians(-20))
w = bpy.data.worlds.new('w'); w.use_nodes = True; w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.004, 0.003, 0.002, 1)
sc.world = w

CAMS = {'wide': ((4.3, -3.0, 2.15), (-1.4, 0.7, 1.35), 24), 'detail': ((1.9, -2.3, 0.9), (0.2, -0.45, 0.85), 30)}
cl, ct, lens = CAMS[os.environ.get('BAR_CAM', 'wide')]
bpy.ops.object.camera_add(location=cl); cam = bpy.context.object; sc.camera = cam
cam.rotation_euler = (Vector(ct) - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam.data.lens = lens
sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = samples; sc.cycles.use_denoising = True
sc.render.resolution_x = 1400; sc.render.resolution_y = 1000
sc.view_settings.view_transform = 'AgX'; sc.view_settings.look = 'AgX - Medium High Contrast'
sc.render.filepath = outp
if len(a) > 4:
    bpy.ops.wm.save_as_mainfile(filepath=a[4])
bpy.ops.render.render(write_still=True)
