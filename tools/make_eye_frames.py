"""Builds Capri's eye-direction frames so her eyes can follow the cursor.
Run after normalize_sprites.py:  python tools/make_eye_frames.py
For each round-eyed pose in POSES, both irises (blue ring + white pupil) slide toward one of 8
directions inside their sockets; the space they leave is filled with the eye's green ring colour.
Writes sprites/eye-<pose>-<dir>.png, dir 0..7 = right, up-right, up, up-left, left, down-left,
down, down-right (in the drawing's own orientation).
"""
import os, json, math
import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPR = os.path.join(ROOT, 'sprites')
POSES = [12, 16, 4]
SHIFT = 0.42  # how far the iris moves, as a share of its radius

def find_eyes(a, vx, vy):
    """The two irises: biggest blue blobs (with the white pupil inside) near the visor."""
    h, w = a.shape[:2]
    x0, x1, y0, y1 = max(0, vx - 140), min(w, vx + 140), max(0, vy - 80), min(h, vy + 50)
    r, g, b, al = [a[y0:y1, x0:x1, k].astype(int) for k in range(4)]
    blue = (b > 140) & (r < 110) & (b > g + 30) & (al > 200)
    lab, n = ndimage.label(blue, structure=np.ones((3, 3)))
    if n < 2:
        return []
    sizes = ndimage.sum(blue, lab, range(1, n + 1))
    eyes = []
    for k in np.argsort(sizes)[::-1][:2]:
        if sizes[k] < 40:
            continue
        filled = ndimage.binary_fill_holes(lab == k + 1)       # ring + the white pupil inside it
        ys, xs = np.nonzero(filled)
        cx, cy = xs.mean(), ys.mean()
        rad = math.sqrt(filled.sum() / math.pi)
        eyes.append((cx + x0, cy + y0, rad))
    return eyes

def ring_colour(a, cx, cy, rad):
    """The green ring just outside the iris."""
    ys, xs = np.mgrid[0:a.shape[0], 0:a.shape[1]]
    d = np.hypot(xs - cx, ys - cy)
    ring = (d > rad + 1) & (d < rad + 4)
    px = a[ring]
    greenish = px[(px[:, 1] > 150) & (px[:, 0] < 140) & (px[:, 3] > 200)]
    if len(greenish) < 5:
        return np.array([20, 200, 120, 255])
    return np.median(greenish, axis=0).astype(np.uint8)

def shift_eye(out, src, cx, cy, rad, dx, dy):
    h, w = src.shape[:2]
    ys, xs = np.mgrid[0:h, 0:w]
    disk = np.hypot(xs - cx, ys - cy) <= rad + 1.2
    out[disk] = ring_colour(src, cx, cy, rad)
    # Paste the iris shifted, sampling from the original.
    sx, sy = int(round(dx)), int(round(dy))
    moved = np.hypot(xs - (cx + sx), ys - (cy + sy)) <= rad + 1.2
    yy, xx = np.nonzero(moved)
    oy, ox = yy - sy, xx - sx
    ok = (oy >= 0) & (oy < h) & (ox >= 0) & (ox < w)
    out[yy[ok], xx[ok]] = src[oy[ok], ox[ok]]

def main():
    meta = json.load(open(os.path.join(SPR, 'frames.json')))
    W, H = meta['width'], meta['height']
    vx, vy = int(meta['visor'][0] * W), int(meta['visor'][1] * H)
    made = {}
    for p in POSES:
        src = np.array(Image.open(os.path.join(SPR, f'frame-{p:02d}.png')).convert('RGBA'))
        eyes = find_eyes(src, vx, vy)
        if len(eyes) != 2:
            print(f'pose {p}: found {len(eyes)} eyes, skipped'); continue
        for d in range(8):
            ang = math.radians(d * 45)
            out = src.copy()
            for cx, cy, rad in eyes:
                s = rad * SHIFT
                shift_eye(out, src, cx, cy, rad, math.cos(ang) * s, -math.sin(ang) * s)
            Image.fromarray(out).save(os.path.join(SPR, f'eye-{p}-{d}.png'), optimize=True)
        made[str(p)] = 8
        print(f'pose {p}: eyes at', [(round(x), round(y), round(r, 1)) for x, y, r in eyes])
    meta['eyes'] = made
    json.dump(meta, open(os.path.join(SPR, 'frames.json'), 'w'), indent=1)

if __name__ == '__main__':
    main()
