"""Builds Capri's music frames (headphones on, head bobbing) from her hover frame.
Run after normalize_sprites.py:  python tools/make_music_frames.py
Writes sprites/music-0..3.png (tilt left, up, tilt right, up) on the same canvas.
"""
import os, json
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPR = os.path.join(ROOT, 'sprites')
BASE = 12                 # hover pose
PIVOT = (340.0, 440.0)    # neck, in canvas pixels
HEAD_END, NECK = 400.0, 500.0   # full head rotation above HEAD_END, none below NECK

def visor_box(a):
    g = (a[:, :, 1] > 150) & (a[:, :, 0] < 120) & (a[:, :, 2] < 170) & (a[:, :, 3] > 200)
    g[int(a.shape[0] * 0.6):] = False
    lab, n = ndimage.label(g)
    k = int(np.argmax(ndimage.sum(g, lab, range(1, n + 1)))) + 1
    ys, xs = np.nonzero(lab == k)
    return xs, ys

def add_headphones(img, a):
    """Angular earcups at the visor's ends and a band over the top, in her palette."""
    xs, ys = visor_box(a)
    left = (xs.min(), ys[xs == xs.min()].mean()); right = (xs.max(), ys[xs == xs.max()].mean())
    cx, cy = xs.mean(), ys.mean()
    d = ImageDraw.Draw(img)
    black, pink, green, edge = (12, 6, 16, 255), (255, 20, 110, 255), (30, 240, 140, 255), (170, 40, 255, 255)
    # Band: straight, faceted segments from cup to cup, like the rest of her design.
    top = cy - 128
    band = [(left[0] - 20, left[1] - 26), (left[0] - 30, cy - 78), (cx - 58, top + 6), (cx + 8, top - 10),
            (cx + 70, top + 14), (right[0] + 26, cy - 72), (right[0] + 18, right[1] - 30)]
    for i in range(len(band) - 1):
        d.line([band[i], band[i + 1]], fill=edge, width=20)
    for i in range(len(band) - 1):
        d.line([band[i], band[i + 1]], fill=black, width=13)
    # Small green spike on top of the band.
    d.polygon([(cx - 6, top - 6), (cx + 16, top - 30), (cx + 20, top - 4)], fill=green)
    for (x, y), flip in ((left, -1), (right, 1)):
        x += flip * 14
        cup = [(x - 20, y - 34), (x + 14, y - 40), (x + 26 * flip if flip > 0 else x + 22, y - 6), (x + 14, y + 34), (x - 22, y + 30), (x - 30 * (1 if flip < 0 else 0.6), y)]
        d.polygon([(px + 3, py + 3) for px, py in cup], fill=edge)
        d.polygon(cup, fill=black)
        inner = [(x - 9, y - 18), (x + 8, y - 21), (x + 13, y), (x + 6, y + 18), (x - 11, y + 15), (x - 15, y)]
        d.polygon(inner, fill=green)
        d.line([(x - 4, y - 8), (x + 4, y - 9)], fill=pink, width=4)
    return img

def bend(a, angle_deg, dip):
    """Rotates the head and hair about the neck, fading to no rotation at the neck."""
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    t = np.clip((NECK - yy) / (NECK - HEAD_END), 0, 1)
    wgt = t * t * (3 - 2 * t)                      # smoothstep
    ang = np.radians(angle_deg) * wgt
    px, py = PIVOT
    # inverse map: output pixel -> source pixel
    sx = px + (xx - px) * np.cos(ang) + (yy - py + 0) * np.sin(ang)
    sy = py - (xx - px) * np.sin(ang) + (yy - py) * np.cos(ang) - dip * wgt
    out = np.zeros_like(a)
    for c in range(4):
        out[:, :, c] = ndimage.map_coordinates(a[:, :, c].astype(np.float64), [sy, sx], order=1, mode='constant', cval=0).clip(0, 255)
    # keep colour clean where alpha is zero
    out[out[:, :, 3] == 0] = 0
    return out.astype(np.uint8)

def main():
    base = Image.open(os.path.join(SPR, f'frame-{BASE:02d}.png')).convert('RGBA')
    withphones = add_headphones(base.copy(), np.array(base))
    a = np.array(withphones)
    for n, (angle, dip) in enumerate([(-9, 12), (0, 0), (9, 12), (0, 0)]):
        Image.fromarray(bend(a, angle, dip)).save(os.path.join(SPR, f'music-{n}.png'), optimize=True)
    meta_path = os.path.join(SPR, 'frames.json')
    meta = json.load(open(meta_path))
    meta['music'] = {'frames': 4, 'base': BASE, 'feet': meta['feet'][BASE]}
    json.dump(meta, open(meta_path, 'w'), indent=1)
    print('wrote sprites/music-0..3.png')

if __name__ == '__main__':
    main()
