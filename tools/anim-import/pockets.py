"""Removes background pockets (white gaps between limbs that the edge flood-fill can't reach),
keeping her mouth and eye whites and small hair highlights."""
import os, json, numpy as np
from PIL import Image
from scipy import ndimage
V = json.load(open('visor.json'))
SKIP = {'spin'}
def fix(name, a):
    rgb = a[..., :3].astype(int)
    white = (rgb.min(-1) > 225) & (a[..., 3] > 200)
    lab, n = ndimage.label(white)
    cx, cy, hull = V[name]
    vw = np.sqrt(hull * 1.8)
    removed = []
    for k in range(1, n + 1):
        m = lab == k
        area = m.sum()
        if area < 150: continue
        ys, xs = np.nonzero(m)
        mx, my = xs.mean(), ys.mean()
        # face zone: visor and the mouth just below it
        if abs(mx - cx) < vw * 0.45 and -vw * 0.6 < my - cy < vw * 0.7: continue
        if name in ('thrown',) and abs(mx - cx) < vw * 0.9 and abs(my - cy) < vw: continue
        removed.append((int(mx), int(my), int(area)))
        grow = ndimage.binary_dilation(m, iterations=2)
        edge = grow & ~m
        a[m, 3] = 0
        # soften the ring next to the hole: it was blended with white
        d = 255 - rgb[edge].min(-1)
        a[edge, 3] = np.minimum(a[edge, 3], np.clip(d * 1.6, 0, 255)).astype(np.uint8)
    return a, removed
if __name__ == '__main__':
    for f in sorted(os.listdir('keyed')):
        name = f[:-4]
        if name in SKIP: continue
        a = np.array(Image.open('keyed/' + f))
        a, removed = fix(name, a)
        if removed:
            Image.fromarray(a).save('keyed/' + f)
            print(name, removed)
