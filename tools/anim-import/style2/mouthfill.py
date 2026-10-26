"""Open mouths came out see-through (the white inside was taken for background): enclosed holes
just below her face get filled white again."""
import os, json, numpy as np
from PIL import Image
from scipy import ndimage
FX, FY = 338, 367
EXTRA = {'celebrate'}   # mouth outline is partly green there
SKIP = {'nap'}          # that hole is under her chin, not a mouth
def fill(a, name=''):
    clear = a[..., 3] < 128
    lab, n = ndimage.label(clear)
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]])))
    done = []
    for k in range(1, n + 1):
        if k in border: continue
        m = lab == k; s = m.sum()
        if s < 20 or s > 700: continue
        ys, xs = np.nonzero(m)
        ring = ndimage.binary_dilation(m, iterations=3) & ~m
        rgb = a[ring][:, :3].astype(int)
        dark = ((rgb.sum(-1) < 180) & (a[ring][:, 3] > 128)).mean()
        if abs(xs.mean() - FX) < 60 and -5 < ys.mean() - FY < 70 and (dark > 0.5 or name in EXTRA) and name not in SKIP:
            g = ndimage.binary_dilation(m, iterations=1)
            a[g & (a[..., 3] < 255), :3] = 250
            a[g, 3] = 255
            a[m, :3] = 250
            done.append(int(s))
    return a, done
if __name__ == '__main__':
    for f in sorted(os.listdir('norm')):
        a = np.array(Image.open('norm/' + f)); a, d = fill(a, f[:-4])
        if d: Image.fromarray(a).save('norm/' + f); print(f[:-4], d)
