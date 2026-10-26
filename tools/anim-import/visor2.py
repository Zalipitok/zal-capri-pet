import numpy as np
from scipy import ndimage
from scipy.spatial import ConvexHull

def visor2(rgba):
    a = rgba.astype(int)
    r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    green = (g > 150) & (g > r + 50) & (g > b + 20) & (al > 200)
    lab, n = ndimage.label(green)
    ys_all, xs_all = np.nonzero(al > 128)
    top, bottom = ys_all.min(), ys_all.max()
    best = None
    for k in range(1, n + 1):
        ys, xs = np.nonzero(lab == k)
        if len(xs) < 120: continue
        rel = (ys.mean() - top) / max(1, bottom - top)
        if rel > 0.8: continue
        try: hull = ConvexHull(np.c_[xs, ys]).volume
        except Exception: continue
        # visor: wide, fairly solid, high up
        score = hull * (1.2 - rel)
        if best is None or score > best[0]:
            best = (score, xs.mean(), ys.mean(), hull)
    return best
