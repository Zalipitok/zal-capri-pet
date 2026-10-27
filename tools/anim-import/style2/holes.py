"""Makes the leftover white background see-through: patches of the sheet's white paper that got
enclosed between her arms, legs and hair when the drawings were cut out. Only the patches listed
in HOLES (checked by eye) are cleared, so her white mouths, teeth and eye glints are never touched.
  python holes.py <folder with anim pngs> [--dry]   (--find lists candidates instead)"""
import os, sys
import numpy as np
from PIL import Image
from scipy import ndimage

def holes(a):
    rgb = a[..., :3].astype(int); al = a[..., 3]
    white = (rgb.min(-1) > 205) & (rgb.max(-1) - rgb.min(-1) < 40) & (al > 200)
    dark = (rgb.max(-1) < 70) & (al > 200)
    lab, n = ndimage.label(white)
    mask = np.zeros(white.shape, bool); found = []
    for k in range(1, n + 1):
        m = lab == k; size = int(m.sum())
        if size < 40: continue
        ring = ndimage.binary_dilation(m, iterations=3) & ~ndimage.binary_dilation(m, iterations=1)
        ring &= al > 200
        if ring.sum() == 0: continue
        darkfrac = (dark & ring).sum() / ring.sum()
        if darkfrac >= 0.8: continue            # a mouth or teeth inside her black face
        mask |= m; ys, xs = np.nonzero(m); found.append((size, int(xs.mean()), int(ys.mean()), round(float(darkfrac), 2)))
    return mask, found

# (drawing, x, y): the middle of each white patch that is background, on the 677x897 canvas
HOLES = [('beckon', 373, 474), ('breaktime', 335, 390), ('breaktime', 395, 409), ('dash', 278, 366), ('dash', 292, 490),
         ('filescan', 455, 398), ('friendflee', 241, 405), ('gamerwin', 245, 407), ('headpat', 304, 398), ('headpat', 370, 575),
         ('possess', 245, 387), ('pounce', 139, 304), ('sabotage', 248, 447), ('sabotage', 288, 461), ('spin', 290, 417),
         ('spin', 391, 570), ('swat', 470, 326), ('upsidedown', 480, 492), ('welcomeback', 198, 317), ('welcomeback', 253, 341),
         ('windup', 238, 331), ('zigzag', 254, 374)]

def listed(a, name):
    """The listed patches of this drawing (the white component nearest each listed point)."""
    rgb = a[..., :3].astype(int); al = a[..., 3]
    white = (rgb.min(-1) > 205) & (rgb.max(-1) - rgb.min(-1) < 40) & (al > 200)
    lab, n = ndimage.label(white)
    mask = np.zeros(white.shape, bool); found = []
    for nm, x, y in HOLES:
        if nm != name: continue
        best = None
        for k in range(1, n + 1):
            ys, xs = np.nonzero(lab == k)
            if len(xs) < 30: continue
            d = np.hypot(xs.mean() - x, ys.mean() - y)
            if d < 15 and (best is None or d < best[0]): best = (d, k, len(xs))
        if best: mask |= lab == best[1]; found.append((x, y, best[2]))
    return mask, found

def clear(a, mask):
    out = a.copy()
    rgb = a[..., :3].astype(int)
    # the white itself, plus the pale anti-aliased fringe around it
    fringe = ndimage.binary_dilation(mask, iterations=2) & (rgb.min(-1) > 150)
    out[mask | fringe, 3] = 0
    return out

if __name__ == '__main__':
    folder = sys.argv[1]; dry = '--dry' in sys.argv
    for f in sorted(os.listdir(folder)):
        if not f.endswith('.png'): continue
        p = os.path.join(folder, f); a = np.array(Image.open(p).convert('RGBA'))
        name = f[:-4]
        mask, found = holes(a) if '--find' in sys.argv else listed(a, name)
        if not found: continue
        print(f, found)
        if not dry: Image.fromarray(clear(a, mask)).save(p)
