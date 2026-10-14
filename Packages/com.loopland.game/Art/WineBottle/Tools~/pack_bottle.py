"""Copy the bottle model/textures into the Unity package and write .meta/.mat files.

Reuses the meta/material templates from ../../Frames/Tools~/package.py.
Usage: python3 -I pack_bottle.py <frames_package_py> <out_dir> <tex_dir> <tools_src_dir> <dest Art/WineBottle> <frame_fbx_meta>
"""
import ast, sys, os, shutil, uuid, glob, re
PKG, OUTD, TEXD, TOOLS, DEST, FRAME_META = sys.argv[1:7]
TEX, MAT, MOD, TL = (os.path.join(DEST, d) for d in ('Textures', 'Materials', 'Models', 'Tools~'))
for d in (TEX, MAT, MOD, TL):
    os.makedirs(d, exist_ok=True)

def guid(name):
    return uuid.uuid5(uuid.NAMESPACE_URL, 'loopland/winebottle/' + name).hex

src = open(PKG).read()
ns = {'os': os, 'guid': guid, 'DEST': DEST, 'MAT': MAT}
for node in ast.parse(src).body:
    if isinstance(node, ast.FunctionDef) and node.name in ('folder_meta', 'tex_meta', 'mat'):
        exec(compile(ast.Module([node], []), PKG, 'exec'), ns)

for f in sorted(glob.glob(os.path.join(TEXD, '*.png'))):
    dst = os.path.join(TEX, os.path.basename(f)); shutil.copyfile(f, dst)
    n = os.path.basename(f)
    ns['tex_meta'](dst, 'normal' if n.endswith('_Normal.png') else 'color' if n.endswith('_Albedo.png') else 'linear')
variants = sorted({os.path.basename(f).split('_')[1] for f in glob.glob(os.path.join(TEXD, '*_Albedo.png'))})
for v in variants:
    ns['mat'](f'WineBottle_{v}', f'WineBottle_{v}_Albedo.png', f'WineBottle_{v}_MetallicSmoothness.png',
              f'WineBottle_{v}_Normal.png', None, 1.0)

fbx = os.path.join(MOD, 'WineBottle.fbx'); shutil.copyfile(os.path.join(OUTD, 'WineBottle.fbx'), fbx)
meta = open(FRAME_META).read()
meta = re.sub(r'^guid: .*$', 'guid: ' + guid('model/WineBottle.fbx'), meta, count=1, flags=re.M)
ext = ("  externalObjects:\n  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n"
       f"      name: WineBottle\n    second: {{fileID: 2100000, guid: {guid('mat/WineBottle_Rouge')}, type: 2}}\n")
meta = re.sub(r'  externalObjects:\n(?:  [-\s].*\n)+?(?=  materials:)', ext, meta)
open(fbx + '.meta', 'w').write(meta)

shutil.copyfile(os.path.join(OUTD, 'WineBottle_Textured.blend'), os.path.join(TL, 'WineBottle.blend'))
for f in ('bottle_profile.py', 'build_bottle.py', 'gen_bottle_tex.py', 'textured_blend.py', 'render.py',
          'render_lods.py', 'pack_bottle.py'):
    shutil.copyfile(os.path.join(TOOLS, f), os.path.join(TL, f))
for d in (DEST, TEX, MAT, MOD):
    ns['folder_meta'](d)
print('variants', variants)
