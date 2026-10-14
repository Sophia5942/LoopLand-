"""Planar-UV the round rug, assign the rug material, export FBX + .blend, render a preview.
Usage: python build_rug.py -- <rug.fbx> <texdir> <outdir>"""
import bpy, sys, os, math
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
src, tex, out = args[:3]
VAR = args[3] if len(args) > 3 else 'Burgundy'
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
me = ob.data
# centre the pivot on the rug's base
xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
c = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
for v in me.vertices: v.co -= c
ob.location = (0, 0, 0)
ob.name = me.name = 'Rug_Round'
rad = max(max(xs) - min(xs), max(ys) - min(ys)) / 2
uv = me.uv_layers.active or me.uv_layers.new(name='UVMap')
for l in me.loops:
    co = me.vertices[l.vertex_index].co
    uv.data[l.index].uv = (0.5 + co.x / rad * 0.4975, 0.5 + co.y / rad * 0.4975)
m = bpy.data.materials.new(f'Rug_{VAR}'); m.use_nodes = True
nt = m.node_tree; b = nt.nodes['Principled BSDF']
def img(fn, cs):
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(os.path.join(tex, fn)); t.image.colorspace_settings.name = cs; return t
nt.links.new(img(f'Rug_{VAR}_Albedo.png', 'sRGB').outputs['Color'], b.inputs['Base Color'])
nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(img(f'Rug_{VAR}_Normal.png', 'Non-Color').outputs['Color'], nm.inputs['Color'])
nt.links.new(nm.outputs['Normal'], b.inputs['Normal']); b.inputs['Roughness'].default_value = 0.88
me.materials.clear(); me.materials.append(m)
for p in me.polygons: p.use_smooth = False
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'Rug_Round.blend'))
bpy.ops.export_scene.fbx(filepath=os.path.join(out, 'Rug_Round.fbx'), use_selection=True, object_types={'MESH'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                         axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_tspace=True, path_mode='STRIP')
print('rug', len(me.vertices), 'verts, radius', round(rad, 3))
# quick preview
sc = bpy.context.scene
bpy.ops.mesh.primitive_plane_add(size=30, location=(0, 0, -0.001))
fl = bpy.context.object; fm = bpy.data.materials.new('f'); fm.diffuse_color = (0.03, 0.015, 0.01, 1); fl.data.materials.append(fm)
fm.use_nodes = True; fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.03, 0.015, 0.01, 1)
bpy.ops.object.light_add(type='AREA', location=(1.5, -2, 5)); L = bpy.context.object; L.data.size = 4; L.data.energy = 1500; L.data.color = (1, 0.8, 0.6)
w = bpy.data.worlds.new('w'); w.use_nodes = True; w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.01, 0.008, 0.006, 1); sc.world = w
bpy.ops.object.camera_add(location=(0, -5.5, 3.2)); cam = bpy.context.object; sc.camera = cam
cam.rotation_euler = (Vector((0, -0.3, 0)) - cam.location).to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = 30
sc.render.engine = 'CYCLES'; sc.cycles.samples = 32; sc.cycles.use_denoising = True
sc.render.resolution_x, sc.render.resolution_y = 1200, 800; sc.view_settings.view_transform = 'AgX'
sc.render.filepath = os.path.join(out, f'rug_preview_{VAR}.png'); bpy.ops.render.render(write_still=True)
