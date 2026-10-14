import bpy, sys, math, os
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
sc = bpy.context.scene
for i, nm in enumerate(['WineBottle_LOD0', 'WineBottle_LOD1', 'WineBottle_LOD2']):
    o = bpy.data.objects[nm]; o.location = (i * 0.42 - 0.42, 0, 0)
    m = bpy.data.materials.new('w%d' % i); m.use_nodes = True; nt = m.node_tree
    b = nt.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = (0.5, 0.5, 0.52, 1); b.inputs['Roughness'].default_value = 0.5
    w = nt.nodes.new('ShaderNodeWireframe'); w.use_pixel_size = True; w.inputs[0].default_value = 1.2
    e = nt.nodes.new('ShaderNodeEmission'); e.inputs[0].default_value = (1.0, 0.55, 0.15, 1); e.inputs[1].default_value = 3
    mix = nt.nodes.new('ShaderNodeMixShader')
    nt.links.new(w.outputs[0], mix.inputs[0]); nt.links.new(b.outputs[0], mix.inputs[1]); nt.links.new(e.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], nt.nodes['Material Output'].inputs[0])
    o.data.materials.clear(); o.data.materials.append(m)
bpy.ops.object.light_add(type='AREA', location=(-1.5, -2, 2.5)); L = bpy.context.object; L.data.energy = 600; L.data.size = 2
L.rotation_euler = (math.radians(50), 0, math.radians(-35))
wd = bpy.data.worlds.new('w'); wd.use_nodes = True; wd.node_tree.nodes['Background'].inputs['Color'].default_value = (0.03, 0.03, 0.035, 1); sc.world = wd
bpy.ops.object.camera_add(location=(0, -3.4, 0.7), rotation=(math.radians(85), 0, 0)); sc.camera = bpy.context.object; sc.camera.data.lens = 50
sc.render.engine = 'CYCLES'; sc.cycles.samples = 32; sc.render.resolution_x = 1200; sc.render.resolution_y = 900
sc.view_settings.view_transform = 'AgX'; sc.render.filepath = a[1]
bpy.ops.render.render(write_still=True)
