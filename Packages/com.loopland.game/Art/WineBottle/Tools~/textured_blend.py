"""Hook the Rouge textures into the WineBottle material and save a Blender-ready .blend.
Usage: python textured_blend.py -- <in.blend> <texdir_abs> <out.blend> <relative_texdir_from_out>"""
import bpy, sys, os
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
m = bpy.data.materials['WineBottle']; m.use_nodes = True
nt = m.node_tree; nodes, links = nt.nodes, nt.links
bsdf = nodes['Principled BSDF']
def tex(fn, cs, y):
    t = nodes.new('ShaderNodeTexImage'); t.location = (-700, y)
    t.image = bpy.data.images.load(os.path.join(a[1], fn)); t.image.colorspace_settings.name = cs
    return t
al = tex('WineBottle_Rouge_Albedo.png', 'sRGB', 300)
ms = tex('WineBottle_Rouge_MetallicSmoothness.png', 'Non-Color', 0)
nm = tex('WineBottle_Rouge_Normal.png', 'Non-Color', -300)
links.new(al.outputs['Color'], bsdf.inputs['Base Color'])
sep = nodes.new('ShaderNodeSeparateColor'); sep.location = (-400, 50)
links.new(ms.outputs['Color'], sep.inputs['Color']); links.new(sep.outputs['Red'], bsdf.inputs['Metallic'])
inv = nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1; inv.location = (-400, -100)
links.new(ms.outputs['Alpha'], inv.inputs[1]); links.new(inv.outputs[0], bsdf.inputs['Roughness'])
n = nodes.new('ShaderNodeNormalMap'); n.location = (-400, -300)
links.new(nm.outputs['Color'], n.inputs['Color']); links.new(n.outputs['Normal'], bsdf.inputs['Normal'])
# spread LODs out so they are easy to compare; keep LOD0 at the origin
for o in bpy.data.objects:
    if o.name.endswith('LOD1'): o.location.x = 0.4
    if o.name.endswith('LOD2'): o.location.x = 0.8
bpy.ops.wm.save_as_mainfile(filepath=a[2])
for img in bpy.data.images:
    if img.filepath:
        img.filepath = '//' + a[3] + '/' + os.path.basename(img.filepath)
bpy.ops.wm.save_as_mainfile(filepath=a[2])
print('saved', a[2])
