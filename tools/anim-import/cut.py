"""Cuts the sheet (sheet.jpg) into cells/<state>.png, assigning each drawn piece to the grid cell
its centre falls in, and measures each drawing's visor into visor.json."""
import json, os, sys
import numpy as np
from PIL import Image
from scipy import ndimage
from visor2 import visor2

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')
a = np.array(Image.open('sheet.jpg').convert('RGB')).astype(int)
nb = np.sqrt(((a - 255) ** 2).sum(-1)) > 40

def runs(v):
    out, s = [], None
    for i, x in enumerate(v):
        if x > 0 and s is None: s = i
        if x == 0 and s is not None:
            if i - s >= 5: out.append((s, i))
            s = None
    return out

rows, cols = runs(nb.sum(1)), runs(nb.sum(0))
rb = [0] + [(rows[i][1] + rows[i + 1][0]) // 2 for i in range(len(rows) - 1)] + [a.shape[0]]
cb = [0] + [(cols[i][1] + cols[i + 1][0]) // 2 for i in range(len(cols) - 1)] + [a.shape[1]]
lab, n = ndimage.label(ndimage.binary_dilation(nb, iterations=2))
cent = ndimage.center_of_mass(nb, lab, range(1, n + 1))
sizes = ndimage.sum(nb, lab, range(1, n + 1))
states = [s['id'] for s in json.load(open(os.path.join(ROOT, 'behaviours.json'), encoding='utf-8'))['states']]
cell = {}
for k in range(n):
    if sizes[k] < 30: continue
    cy, cx = cent[k]
    cell.setdefault((np.searchsorted(rb, cy) - 1, np.searchsorted(cb, cx) - 1), []).append(k + 1)
os.makedirs('cells', exist_ok=True)
for r in range(len(rows)):
    for c in range(len(cols)):
        mask = np.isin(lab, cell.get((r, c), []))
        ys, xs = np.nonzero(mask)
        y0, y1, x0, x1 = max(0, ys.min() - 20), ys.max() + 21, max(0, xs.min() - 20), xs.max() + 21
        sub = a[y0:y1, x0:x1].copy(); sub[~mask[y0:y1, x0:x1]] = 255
        Image.fromarray(sub.astype(np.uint8)).save(f'cells/{states[r * len(cols) + c]}.png')
if 'visor' in sys.argv:   # after prep.py
    v = {f[:-4]: [float(x) for x in visor2(np.array(Image.open('keyed/' + f)))[1:4]] for f in os.listdir('keyed')}
    json.dump(v, open('visor.json', 'w'))
