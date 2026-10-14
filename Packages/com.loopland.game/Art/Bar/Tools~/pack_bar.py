"""Copy the bar set into the Unity package and write .meta / .mat files.

Reuses the meta/material templates from ../../Frames/Tools~/package.py.
Usage: python3 -I pack_bar.py <frames_package_py> <out_dir> <tex_dir> <tools_src_dir> <dest Art/Bar> <template_fbx_meta>
"""
import ast, sys, os, shutil, uuid, glob, re
PKG, OUTD, TEXD, TOOLS, DEST, FBX_META = sys.argv[1:7]
TEX, MAT, MOD, TL = (os.path.join(DEST, d) for d in ('Textures', 'Materials', 'Models', 'Tools~'))
for d in (TEX, MAT, MOD, TL):
    os.makedirs(d, exist_ok=True)

def guid(name):
    return uuid.uuid5(uuid.NAMESPACE_URL, 'loopland/bar/' + name).hex

def bottle_guid(name):
    return uuid.uuid5(uuid.NAMESPACE_URL, 'loopland/winebottle/' + name).hex

ns = {'os': os, 'guid': guid, 'DEST': DEST, 'MAT': MAT}
for node in ast.parse(open(PKG).read()).body:
    if isinstance(node, ast.FunctionDef) and node.name in ('folder_meta', 'tex_meta', 'mat'):
        exec(compile(ast.Module([node], []), PKG, 'exec'), ns)

# textures: tileable -> Repeat wrap mode
for f in sorted(glob.glob(os.path.join(TEXD, '*.png'))):
    n = os.path.basename(f); dst = os.path.join(TEX, n); shutil.copyfile(f, dst)
    ns['tex_meta'](dst, 'normal' if n.endswith('_Normal.png') else 'color' if n.endswith('_Albedo.png') else 'linear')
    m = open(dst + '.meta').read()
    m = re.sub(r'wrap([UVW]): 1', r'wrap\1: 0', m)
    open(dst + '.meta', 'w').write(m)

def make(name, tex, color=None, emission=None, bump=1.0):
    ns['mat'](name, f'{tex}_Albedo.png', f'{tex}_MetallicSmoothness.png', f'{tex}_Normal.png', None, bump)
    p = os.path.join(MAT, name + '.mat'); s = open(p).read()
    if color:
        s = s.replace('- _Color: {r: 1, g: 1, b: 1, a: 1}', '- _Color: {r: %g, g: %g, b: %g, a: 1}' % color)
    if emission:
        s = s.replace('  - _NORMALMAP\n', '  - _NORMALMAP\n  - _EMISSION\n')
        s = s.replace('- _EmissionColor: {r: 0, g: 0, b: 0, a: 1}', '- _EmissionColor: {r: %g, g: %g, b: %g, a: 1}' % emission)
        s = s.replace('  m_LightmapFlags: 4', '  m_LightmapFlags: 2')
    open(p, 'w').write(s)

make('Bar_Mahogany', 'Bar_Mahogany')
make('Bar_MahoganyTop', 'Bar_Mahogany', color=(0.72, 0.68, 0.66))
make('Bar_Brass', 'Bar_Brass', bump=0.5)
make('Bar_Leather', 'Bar_Leather')
make('Bar_FrostedGlass', 'Bar_FrostedGlass', emission=(1.6, 1.1, 0.65))
make('Bar_Mirror', 'Bar_Mirror', bump=0.0)

template = open(FBX_META).read()
def fbx(name, remap):
    dst = os.path.join(MOD, name + '.fbx'); shutil.copyfile(os.path.join(OUTD, name + '.fbx'), dst)
    m = re.sub(r'^guid: .*$', 'guid: ' + guid('model/' + name), template, count=1, flags=re.M)
    ext = '  externalObjects:\n'
    for mat_name, g in remap:
        ext += ("  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n"
                f"      name: {mat_name}\n    second: {{fileID: 2100000, guid: {g}, type: 2}}\n")
    m = re.sub(r'  externalObjects:\n(?:  [-\s].*\n)+?(?=  materials:)', ext, m)
    open(dst + '.meta', 'w').write(m)

bar = lambda *names: [(n, guid('mat/' + n)) for n in names]
fbx('Bar_Counter', bar('Bar_Mahogany', 'Bar_MahoganyTop', 'Bar_Brass'))
fbx('Bar_Stool', bar('Bar_Leather', 'Bar_Brass', 'Bar_Mahogany'))
fbx('Bar_BackBar', bar('Bar_Mahogany', 'Bar_MahoganyTop', 'Bar_Mirror'))
fbx('Bar_BackBarBottles', [(f'WineBottle_{v}', bottle_guid(f'mat/WineBottle_{v}')) for v in ('Rouge', 'Reserve', 'Blanc')])
fbx('Bar_PendantLamp', bar('Bar_Brass', 'Bar_FrostedGlass'))

for f in ('Bar_Assets.blend', 'Bar_Scene.blend'):
    shutil.copyfile(os.path.join(OUTD, f), os.path.join(TL, f))
for f in ('gen_bar_tex.py', 'build_bar.py', 'render_bar.py', 'pack_bar.py'):
    shutil.copyfile(os.path.join(TOOLS, f), os.path.join(TL, f))
for d in (DEST, TEX, MAT, MOD):
    ns['folder_meta'](d)
print('packed')
