"""Lengthens everything below her neck so the new drawings have the long body of the original
frames instead of a big head on a short body. The head (and anything above the neck, like
raised arms) is untouched; the stretch eases in over 30 px below the neck so there's no seam.
Shorter, chibi-like drawings get a bit more stretch than tall ones, so poses match better."""
import os, json
import numpy as np
from PIL import Image
from scipy import ndimage

VY = 367
NECK = VY + 32           # just under the visor
EASE = 30
BASE, REF_D, LO, HI = 1.22, 349.0, 1.10, 1.38

def factor(d):
    return float(np.clip(BASE * (REF_D / max(d, 1)) ** 0.4, LO, HI))

def stretch(a, k):
    h, w = a.shape[:2]
    H = h
    y = np.arange(H, dtype=float)
    # k(y): 1 above the neck, easing up to k over EASE px
    t = np.clip((y - NECK) / EASE, 0, 1); t = t * t * (3 - 2 * t)
    ky = 1 + (k - 1) * t
    # source y for each output y: integrate 1/k
    src = np.concatenate([[0], np.cumsum(1 / ky[:-1])])
    src = np.where(y <= NECK, y, NECK + (src - src[int(NECK)]))
    pm = a.astype(float); pm[..., :3] *= pm[..., 3:4] / 255
    yy = np.repeat(src[:, None], w, 1); xx = np.repeat(np.arange(w, dtype=float)[None, :], H, 0)
    out = np.stack([ndimage.map_coordinates(pm[..., c], [yy, xx], order=1, cval=0) for c in range(4)], -1)
    al = out[..., 3:4] / 255
    with np.errstate(divide='ignore', invalid='ignore'):
        out[..., :3] = np.where(al > 0, out[..., :3] / np.maximum(al, 1e-3), 0)
    o = out.clip(0, 255).astype(np.uint8); o[o[..., 3] < 8] = 0
    return o

if __name__ == '__main__':
    rep = json.load(open('norm.json'))
    for f in sorted(os.listdir('norm')):
        name = f[:-4]
        a = np.array(Image.open('norm/' + f))
        ys, _ = np.nonzero(a[..., 3] > 128)
        k = factor(ys.max() - VY)
        out = stretch(a, k)
        # keep it on the canvas: if the feet would run off the bottom, lift the stretch a little
        while (np.nonzero(out[..., 3] > 128)[0].max() > a.shape[0] - 6) and k > 1.0:
            k -= 0.03; out = stretch(a, k)
        Image.fromarray(out).save('norm/' + f)
        yy, _ = np.nonzero(out[..., 3] > 128)
        rep[name]['feet'] = round(float((yy.max() + 1) / a.shape[0]), 4); rep[name]['stretch'] = round(k, 3)
    json.dump(rep, open('norm.json', 'w'), indent=1)
