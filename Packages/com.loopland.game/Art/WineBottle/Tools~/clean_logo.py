"""Strip the drop shadow from the ALIAS logo: keep gold faces + solid black bevels, tight crop.
Usage: python3 -I clean_logo.py <src.png> <out.png>"""
import sys
import numpy as np
from PIL import Image
a = np.asarray(Image.open(sys.argv[1]).convert('RGBA')).astype(np.float64)
rgb, al = a[..., :3], a[..., 3]
lum = rgb.mean(2)
gold = lum >= 40
alpha = np.where(gold, al, np.where(al >= 240, al, 0))
out = np.dstack([rgb, alpha]).clip(0, 255).astype(np.uint8)
ys, xs = np.nonzero(alpha > 8)
pad = 4
im = Image.fromarray(out, 'RGBA').crop((xs.min() - pad, ys.min() - pad, xs.max() + pad + 1, ys.max() + pad + 1))
im.save(sys.argv[2])
print(im.size)
