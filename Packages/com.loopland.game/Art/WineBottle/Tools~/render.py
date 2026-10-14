"""Cycles preview: three variants standing + one lying, warm cellar lighting.
Usage: python render.py -- <blend> <texdir> <out.png> [closeup]
"""
import bpy, sys, math, os
args = sys.argv[sys.argv.index('--') + 1:]
blend, texdir, outp = args[:3]
close = len(args) > 3
bpy.ops.wm.open_mainfile(filepath=blend)
sc = bpy.context.scene
src = bpy.data.objects['WineBottle_LOD0']
for o in list(sc.objects):
    if o is not src:
        bpy.data.objects.remove(o)


def make_mat(name):
    m = bpy.data.materials.new('M_' + name)
    m.use_nodes = True
    nt = m.node_tree; nodes = nt.nodes; links = nt.links
    bsdf = nodes['Principled BSDF']
    def tex(fn, colorspace):
        t = nodes.new('ShaderNodeTexImage')
        t.image = bpy.data.images.load(os.path.join(texdir, fn))
        t.image.colorspace_settings.name = colorspace
        return t
    a = tex(f'WineBottle_{name}_Albedo.png', 'sRGB')
    ms = tex(f'WineBottle_{name}_MetallicSmoothness.png', 'Non-Color')
    nm = tex(f'WineBottle_{name}_Normal.png', 'Non-Color')
    links.new(a.outputs['Color'], bsdf.inputs['Base Color'])
    sep = nodes.new('ShaderNodeSeparateColor')
    links.new(ms.outputs['Color'], sep.inputs['Color'])
    links.new(sep.outputs['Red'], bsdf.inputs['Metallic'])
    inv = nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1
    links.new(ms.outputs['Alpha'], inv.inputs[1])
    links.new(inv.outputs[0], bsdf.inputs['Roughness'])
    nmap = nodes.new('ShaderNodeNormalMap')
    links.new(nm.outputs['Color'], nmap.inputs['Color'])
    links.new(nmap.outputs['Normal'], bsdf.inputs['Normal'])
    return m

variants = ['Rouge', 'Reserve', 'Blanc']
if close:
    placements = [('Rouge', (0, 0, 0), (0, 0, 0)), ('Reserve', (0.75, 0.35, 0), (0, 0, math.radians(180)))]
else:
    placements = [(v, (i * 0.42 - 0.42, 0, 0), (0, 0, 0)) for i, v in enumerate(variants)]
    placements.append(('Rouge', (0.95, 0.15, 0.135), (math.radians(90), 0, math.radians(-60))))
for v, loc, rot in placements:
    o = src.copy(); o.data = src.data.copy()
    o.data.materials.clear(); o.data.materials.append(make_mat(v))
    o.location = loc; o.rotation_euler = rot
    sc.collection.objects.link(o)
bpy.data.objects.remove(src)

# shelf
bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
fl = bpy.context.object
fm = bpy.data.materials.new('wood'); fm.use_nodes = True
fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.035, 0.018, 0.01, 1)
fm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.45
fl.data.materials.append(fm)
bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 1.6, 2), rotation=(math.radians(90), 0, 0))
wl = bpy.context.object; wl.data.materials.append(fm)

def light(kind, loc, energy, color, size=1.0, rot=None):
    bpy.ops.object.light_add(type=kind, location=loc)
    l = bpy.context.object; l.data.energy = energy; l.data.color = color
    if kind == 'AREA': l.data.size = size
    if rot: l.rotation_euler = rot
    return l
light('AREA', (-1.6, -1.8, 2.4), 900, (1.0, 0.82, 0.62), 1.4, (math.radians(55), 0, math.radians(-40)))
light('AREA', (2.2, -0.5, 1.6), 350, (1.0, 0.75, 0.5), 0.6, (math.radians(70), 0, math.radians(75)))
light('AREA', (0, 1.0, 3.0), 300, (1.0, 0.85, 0.7), 2.0)        # rim from behind/top
w = bpy.data.worlds.new('w'); w.use_nodes = True
w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.02, 0.012, 0.008, 1)
sc.world = w

bpy.ops.object.camera_add()
cam = bpy.context.object; sc.camera = cam
if close:
    cam.location = (0.05, -1.25, 0.55); cam.rotation_euler = (math.radians(86), 0, math.radians(3))
    cam.data.lens = 50
else:
    cam.location = (0.3, -3.6, 0.95); cam.rotation_euler = (math.radians(83), 0, math.radians(4))
    cam.data.lens = 45
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 96
sc.cycles.use_denoising = True
sc.render.resolution_x, sc.render.resolution_y = (1100, 900) if close else (1500, 900)
sc.view_settings.view_transform = 'AgX'
sc.render.filepath = outp
bpy.ops.render.render(write_still=True)
