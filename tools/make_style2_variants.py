"""Talking-mouth and headphone frames for the 2nd art style (small face).
Mouth: her closed mouth is a thin white zigzag under the eyes; it's replaced by a white downward
triangle (like her drawn open mouths), turned to the zigzag's angle and sized to its width.
Headphones: placed from the two eyes (midpoint, spacing and tilt)."""
import os, sys, json, math
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
FX, FY = 338, 367
# Sitting drawings: where she sits (fraction of the canvas height); the app rests that on the
# window top / taskbar instead of her feet.
SEATS = {'perch': 0.63, 'windowperch': 0.645, 'taskbar': 0.705}
# The drawings face right (the app mirrors them); these few face left and are left alone.
FACING_LEFT = {'yawn', 'friendchase', 'teleport', 'rivalsneak', 'orbit', 'zigzag', 'friendflee', 'attentionbait'}
DRAWN_OPEN = {'caught', 'dance', 'friendtalk', 'gamerwin', 'spin', 'casting',   # already open-mouthed, or no clean spot
              'celebrate', 'laughing', 'peek', 'zigzag', 'tumble'}

def axis(m):
    ys, xs = np.nonzero(m); cx, cy = xs.mean(), ys.mean()
    c = np.cov(np.vstack([xs - cx, ys - cy])) if len(xs) > 2 else np.eye(2)
    w, v = np.linalg.eigh(c); ux, uy = v[:, 1]
    if ux < 0: ux, uy = -ux, -uy
    along = (xs - cx) * ux + (ys - cy) * uy; across = -(xs - cx) * uy + (ys - cy) * ux
    return cx, cy, math.degrees(math.atan2(uy, ux)), along.max() - along.min() + 1, across.min(), across.max()

def eyes(a, reach=(70, 60)):
    A = a.astype(int)
    blue = (A[..., 2] > 140) & (A[..., 2] > A[..., 0] + 50) & (A[..., 2] > A[..., 1] + 20) & (A[..., 3] > 200)
    yy, xx = np.mgrid[0:A.shape[0], 0:A.shape[1]]
    blue &= (abs(xx - FX) < reach[0]) & (abs(yy - FY) < reach[1])
    lab, n = ndimage.label(ndimage.binary_dilation(blue, iterations=2))
    if n < 1: return None
    sizes = ndimage.sum(blue, lab, range(1, n + 1))
    order = [k + 1 for k in np.argsort(sizes)[::-1] if sizes[k] >= 8][:2]
    pts = [tuple(np.array(np.nonzero(lab == k))[::-1].mean(1)) for k in order]
    return pts

def find_mouth(a, faint=False):
    A = a.astype(int)
    yy, xx = np.mgrid[0:A.shape[0], 0:A.shape[1]]
    # The face isn't always at the anchor: look under her eyes when they can be found.
    fx, fy = FX, FY
    ey = eyes(a, (130, 120))
    if ey and len(ey) == 2 and 15 < math.dist(*ey) < 110:
        fx, fy = (ey[0][0] + ey[1][0]) / 2, (ey[0][1] + ey[1][1]) / 2
    zone = (abs(xx - fx) < 55) & (yy > fy - 5) & (yy < fy + 70)
    lum = A[..., :3].min(-1)
    white = zone & (lum > 85) & (A[..., 3] > 200)
    if faint:   # some drawings have a dim grey zigzag
        white = zone & (lum > 48) & ((A[..., :3].max(-1) - lum) < 45) & (A[..., 3] > 200)
    dark = (A[..., :3].sum(-1) < 220) & (A[..., 3] > 200)
    # thin white strokes on black: most of the 5x5 neighbourhood is dark
    darkfrac = ndimage.uniform_filter(dark.astype(float), 7)
    white &= darkfrac > 0.45
    green = (A[..., 1] > 140) & (A[..., 1] > A[..., 0] + 45) & (A[..., 3] > 200)
    blue = (A[..., 2] > 140) & (A[..., 2] > A[..., 0] + 50)
    white &= ~ndimage.binary_dilation(green | blue, iterations=4)   # eye highlights sit in the visor
    groups, n = ndimage.label(ndimage.binary_dilation(white, iterations=3))
    best = None
    for k in range(1, n + 1):
        m = white & (groups == k); sz = m.sum()
        if sz < 5 or sz > 500: continue
        ys, xs = np.nonzero(m); w, h = xs.max() - xs.min() + 1, ys.max() - ys.min() + 1
        if w < 8 or w < h * 1.2: continue
        d = np.hypot(xs.mean() - fx, ys.mean() - (fy + 22))
        if best is None or d < best[0]: best = (d, m)
    if best is None and not faint: return find_mouth(a, True)
    return best[1] if best else None

def talk(a, m, level):
    out = a.copy()
    cx, cy, ang, L, t0, t1 = axis(m)
    g = ndimage.binary_dilation(m, iterations=3)
    out[g, :3] = (18, 10, 24); out[g, 3] = 255
    c, s = math.cos(math.radians(ang)), math.sin(math.radians(ang))
    R = lambda u, t: (cx + u * c - t * s, cy + u * s + t * c)
    width = L * (0.95 if level == 1 else 1.15); height = width * (0.42 if level == 1 else 0.7)
    top = t0 - 1
    tri = [R(-width / 2, top), R(width / 2, top - width * 0.06), R(width * 0.06, top + height)]
    im = Image.fromarray(out); d = ImageDraw.Draw(im)
    d.polygon(tri, fill=(250, 250, 250, 255))
    return im

def headphones(a, ey):
    (x1, y1), (x2, y2) = sorted(ey)
    cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
    ang = math.degrees(math.atan2(y2 - y1, x2 - x1)); dist = math.hypot(x2 - x1, y2 - y1)
    k = dist / 46.0          # eye spacing of the original hover frame was ~46 px at this drawing scale
    c, s = math.cos(math.radians(ang)), math.sin(math.radians(ang))
    P = lambda u, t: (cx + (u * c - t * s) * k, cy + (u * s + t * c) * k)
    im = Image.fromarray(a.copy()); d = ImageDraw.Draw(im)
    black, pink, green, edge = (12, 6, 16, 255), (255, 20, 110, 255), (30, 240, 140, 255), (170, 40, 255, 255)
    hw = 52
    band = [(-hw, -6), (-hw - 6, -40), (-30, -78), (4, -88), (40, -74), (hw + 8, -38), (hw + 2, -4)]
    pts = [P(u, t) for u, t in band]
    for i in range(len(pts) - 1): d.line([pts[i], pts[i + 1]], fill=edge, width=max(3, int(11 * k)))
    for i in range(len(pts) - 1): d.line([pts[i], pts[i + 1]], fill=black, width=max(2, int(7 * k)))
    d.polygon([P(-4, -86), P(10, -102), P(13, -85)], fill=green)
    for side in (-1, 1):
        u0 = side * (hw + 2)
        cup = [(u0 - 12, -20), (u0 + 9, -24), (u0 + 15, -3), (u0 + 9, 20), (u0 - 12, 18), (u0 - 17, 0)]
        d.polygon([P(u + 2, t + 2) for u, t in cup], fill=edge)
        d.polygon([P(u, t) for u, t in cup], fill=black)
        inner = [(u0 - 5, -10), (u0 + 5, -12), (u0 + 8, 0), (u0 + 4, 10), (u0 - 6, 9), (u0 - 9, 0)]
        d.polygon([P(u, t) for u, t in inner], fill=green)
        d.line([P(u0 - 2, -4), P(u0 + 3, -5)], fill=pink, width=max(2, int(3 * k)))
    return im

if __name__ == '__main__':
    # Run from the repo root after tools/make_anim_frames.py:  python tools/make_style2_variants.py
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from make_anim_frames import HEADPHONES
    ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    src = dst = os.path.join(ROOT, 'sprites')
    HP = set(HEADPHONES)
    os.makedirs(dst, exist_ok=True); info = {}; nomouth = []; noeyes = []
    for f in sorted(os.listdir(src)):
        if not (f.startswith('anim-') and f.endswith('.png')) or f.count('-') != 1: continue
        n = f[5:-4]; a = np.array(Image.open(os.path.join(src, f)).convert('RGBA')); rec = {}
        m = None if n in DRAWN_OPEN else find_mouth(a)
        if m is not None:
            for lvl in (1, 2): talk(a, m, lvl).save(os.path.join(dst, f'anim-{n}-mouth-{lvl}.png'))
            rec['mouth'] = True
        else: nomouth.append(n)
        if n in HP:
            ey = eyes(a)
            if ey and len(ey) == 2 and 20 < math.dist(*ey) < 90:
                headphones(a, ey).save(os.path.join(dst, f'anim-{n}-hp.png')); rec['headphones'] = True
            else: noeyes.append(n)
        info[n] = rec
    meta_path = os.path.join(dst, 'frames.json'); meta = json.load(open(meta_path))
    for n, rec in info.items():
        if n in meta['anims'] and 'use' not in meta['anims'][n]: meta['anims'][n].update(rec)
    for n in FACING_LEFT:
        if n in meta['anims']: meta['anims'][n]['facing'] = 'left'
    for n, v in SEATS.items():
        if n in meta['anims'] and 'use' not in meta['anims'][n]: meta['anims'][n]['seat'] = v
    for n in DRAWN_OPEN:   # drop talking frames made by an earlier run
        if n in meta['anims']: meta['anims'][n].pop('mouth', None)
        for lvl in (1, 2):
            f = os.path.join(dst, f'anim-{n}-mouth-{lvl}.png')
            if os.path.exists(f): os.remove(f)
    json.dump(meta, open(meta_path, 'w'), indent=1)
    for f in os.listdir(dst):
        if f.startswith('anim-') and (f.endswith('-hp.png') or '-mouth-' in f):
            pth = os.path.join(dst, f); im = Image.open(pth)
            if im.mode != 'P': im.convert('RGBA').quantize(256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE).save(pth, optimize=True)
    print('mouths', sum('mouth' in r for r in info.values()), 'hp', sum('headphones' in r for r in info.values()))
    print('no mouth:', nomouth); print('hp skipped:', noeyes)
