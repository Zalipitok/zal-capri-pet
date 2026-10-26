"""Puts every keyed sprite on Capri's 677x897 canvas: the same head (visor) size, the visor at the
same spot, overall size partly evened out, nudged/shrunk only if a pose wouldn't fit."""
import os, json
import numpy as np
from PIL import Image, ImageFilter
W, H, VX, VY, REF, MARGIN = 677, 897, 338, 367, 4180.0, 6
V = json.load(open('visor.json'))
OVERRIDE = {'impact': {'hull': 2640}}   # hands cover the visor, so its size can't be measured

def scale_of(name, a):
    cx, cy, hull = V[name]
    hull = OVERRIDE.get(name, {}).get('hull', hull)
    return np.sqrt(REF / hull), cx, cy

areas = {}
for f in os.listdir('keyed'):
    a = np.array(Image.open('keyed/' + f)); s, _, _ = scale_of(f[:-4], a)
    areas[f[:-4]] = (a[..., 3] > 128).sum() * s * s
MED = float(np.median(list(areas.values())))

os.makedirs('norm', exist_ok=True)
report = {}
for f in sorted(os.listdir('keyed')):
    name = f[:-4]
    rgba = np.array(Image.open('keyed/' + f))
    s, cx, cy = scale_of(name, rgba)
    L = np.sqrt(areas[name] / MED)
    comp = min(1.15, max(0.9, L ** -0.4))
    s *= comp
    ys, xs = np.nonzero(rgba[..., 3] > 20)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    fit = min((W - 2 * MARGIN) / ((x1 - x0) * s), (H - 2 * MARGIN) / ((y1 - y0) * s), 1.0)
    s *= fit
    ox, oy = VX - cx * s, VY - cy * s
    l, r, t, b = ox + x0 * s, ox + x1 * s, oy + y0 * s, oy + y1 * s
    if l < MARGIN: ox += MARGIN - l
    if r > W - MARGIN: ox -= r - (W - MARGIN)
    if t < MARGIN: oy += MARGIN - t
    if b > H - MARGIN: oy -= b - (H - MARGIN)
    crop = Image.fromarray(rgba).crop((x0, y0, x1, y1))
    # premultiply so edges don't pick up dark fringes when resampled
    pm = np.array(crop).astype(float); pm[..., :3] *= pm[..., 3:4] / 255
    big = Image.fromarray(pm.clip(0, 255).astype(np.uint8)).resize((max(1, round((x1 - x0) * s)), max(1, round((y1 - y0) * s))), Image.LANCZOS)
    big = big.filter(ImageFilter.UnsharpMask(radius=1.2, percent=60, threshold=2)) if s > 1.05 else big
    bb = np.array(big).astype(float)
    al = bb[..., 3:4] / 255
    with np.errstate(divide='ignore', invalid='ignore'):
        bb[..., :3] = np.where(al > 0, bb[..., :3] / np.maximum(al, 1e-3), 0)
    big = Image.fromarray(bb.clip(0, 255).astype(np.uint8))
    canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    canvas.alpha_composite(big, (int(round(ox + x0 * s)), int(round(oy + y0 * s))))
    out = np.array(canvas); out[out[..., 3] < 8] = 0
    Image.fromarray(out).save('norm/' + f)
    yy, _ = np.nonzero(out[..., 3] > 128)
    report[name] = {'feet': round(float((yy.max() + 1) / H), 4), 'scale': round(float(s), 3), 'sizefix': round(float(comp), 3), 'fit': round(float(fit), 3)}
json.dump(report, open('norm.json', 'w'), indent=1)
for k, v in report.items():
    if v['fit'] < 1: print('shrunk to fit', k, v)
