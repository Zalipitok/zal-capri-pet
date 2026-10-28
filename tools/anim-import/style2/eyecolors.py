"""Evens out the colours inside her eyes. The AI-drawn sheet left yellow, orange, pink and purple
tints in some eyes (sickly yellow whites, purple irises). Inside each eye hole of her green visor:
yellowish/orange whites become white, pink/purple becomes her iris blue, and the outline stays.
Shapes are not touched.   python eyecolors.py <in folder> <out folder> [names...]"""
import os, sys, colorsys
import numpy as np
from PIL import Image
from scipy import ndimage

def eye_holes(a, centre=None):
    """Everything inside the outline of her visor that isn't the green itself: the eyes (even when
    their black lids touch the visor's edge, which defeats a plain hole fill)."""
    from skimage.morphology import convex_hull_image
    A = a.astype(int); R, G, B, al = A[..., 0], A[..., 1], A[..., 2], A[..., 3]
    green = (G > 140) & (G > R + 45) & (G > B + 20) & (al > 200)
    cx, cy = centre if centre else (338, 367)
    yy, xx = np.mgrid[0:a.shape[0], 0:a.shape[1]]
    near = np.hypot(xx - cx, yy - cy) < 75
    lab, n = ndimage.label(green)
    visor = np.zeros(green.shape, bool)
    for k in range(1, n + 1):   # the green pieces around her eyes (lids can split the visor)
        m = lab == k
        if m.sum() >= 30 and (m & near).sum() >= 0.5 * m.sum(): visor |= m
    if not visor.any(): return visor
    hull = convex_hull_image(visor) & near
    hair = (R > 170) & (G < 110) & (B < 150)
    return hull & ~green & ~hair & (al > 200)

def fix(a, centre=None):
    out = a.copy()
    holes = eye_holes(a, centre)
    if not holes.any(): return out, 0
    px = a[holes][:, :3].astype(float) / 255
    hsv = np.array([colorsys.rgb_to_hsv(*p) for p in px])
    h, sat, v = hsv[:, 0] * 360, hsv[:, 1], hsv[:, 2]
    # stay clear of the hair's anti-aliased edge
    A = a.astype(int); hair = (A[..., 0] > 150) & (A[..., 1] < 120) & (A[..., 0] > A[..., 2] + 30)
    nearhair = ndimage.binary_dilation(hair, iterations=2)[holes]
    new = hsv.copy(); changed = np.zeros(len(px), bool)
    # yellow / orange / lime whites: take the colour out, keep the shading
    warm = (sat > 0.15) & (h >= 15) & (h <= 75) & ~nearhair
    new[warm, 1] = 0.03; new[warm, 0] = 230 / 360; changed |= warm
    # violet / purple irises: turn the hue to her blue, keep the shading
    purple = (sat > 0.2) & (h >= 245) & (h <= 330) & (px[:, 2] >= px[:, 0] * 0.85) & ~nearhair
    new[purple, 0] = 225 / 360; changed |= purple
    rgb = np.array([colorsys.hsv_to_rgb(*q) for q in new])
    vals = out[holes]; vals[changed, :3] = np.clip(rgb[changed] * 255, 0, 255).astype(np.uint8); out[holes] = vals
    return out, int(changed.sum())

if __name__ == '__main__':
    src, dst = sys.argv[1], sys.argv[2]; names = sys.argv[3:]
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
    from make_style2_variants import eyes
    os.makedirs(dst, exist_ok=True)
    for f in sorted(os.listdir(src)):
        if not f.endswith('.png') or (names and f[:-4] not in names): continue
        a = np.array(Image.open(os.path.join(src, f)).convert('RGBA'))
        ey = eyes(a, (130, 120))
        centre = (sum(e[0] for e in ey) / len(ey), sum(e[1] for e in ey) / len(ey)) if ey else None
        o, n = fix(a, centre)
        Image.fromarray(o).save(os.path.join(dst, f))
        if n: print(f, n)
