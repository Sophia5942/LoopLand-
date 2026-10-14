"""Copy the rug into the Unity package with .meta/.mat files (templates from Frames/Tools~/package.py).
Usage: python3 -I pack_rug.py <frames_package_py> <out_dir> <tex_dir> <tools_dir> <dest Art/Rug> <template_fbx_meta>"""
import ast, sys, os, shutil, uuid, glob, re
PKG, OUTD, TEXD, TOOLS, DEST, FBX_META = sys.argv[1:7]
TEX, MAT, MOD, TL = (os.path.join(DEST, d) for d in ('Textures', 'Materials', 'Models', 'Tools~'))
for d in (TEX, MAT, MOD, TL): os.makedirs(d, exist_ok=True)
guid = lambda n: uuid.uuid5(uuid.NAMESPACE_URL, 'loopland/rug/' + n).hex
ns = {'os': os, 'guid': guid, 'DEST': DEST, 'MAT': MAT}
for node in ast.parse(open(PKG).read()).body:
    if isinstance(node, ast.FunctionDef) and node.name in ('folder_meta', 'tex_meta', 'mat'):
        exec(compile(ast.Module([node], []), PKG, 'exec'), ns)
for f in sorted(glob.glob(os.path.join(TEXD, '*.png'))):
    n = os.path.basename(f); dst = os.path.join(TEX, n); shutil.copyfile(f, dst)
    ns['tex_meta'](dst, 'normal' if n.endswith('_Normal.png') else 'color' if n.endswith('_Albedo.png') else 'linear')
for v in ('Persian',):
    ns['mat'](f'Rug_{v}', f'Rug_{v}_Albedo.png', f'Rug_{v}_MetallicSmoothness.png', f'Rug_{v}_Normal.png', None, 0.6)
dst = os.path.join(MOD, 'Rug_Round.fbx'); shutil.copyfile(os.path.join(OUTD, 'Rug_Round.fbx'), dst)
m = re.sub(r'^guid: .*$', 'guid: ' + guid('model/Rug_Round'), open(FBX_META).read(), count=1, flags=re.M)
ext = ("  externalObjects:\n  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n"
       f"      name: Rug_Persian\n    second: {{fileID: 2100000, guid: {guid('mat/Rug_Persian')}, type: 2}}\n")
open(dst + '.meta', 'w').write(re.sub(r'  externalObjects:\n(?:  [-\s].*\n)+?(?=  materials:)', ext, m))
shutil.copyfile(os.path.join(OUTD, 'Rug_Round.blend'), os.path.join(TL, 'Rug_Round.blend'))
for f in ('gen_rug_kashan.py', 'build_rug.py', 'pack_rug.py'): shutil.copyfile(os.path.join(TOOLS, f), os.path.join(TL, f))
for d in (DEST, TEX, MAT, MOD): ns['folder_meta'](d)
print('packed')
