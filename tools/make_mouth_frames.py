"""Builds Capri's talking-mouth frames for every pose she speaks in.
Run after normalize_sprites.py:  python tools/make_mouth_frames.py
For each speaking pose it writes sprites/mouth-<pose>-1.png (half open) and -2.png (wide open):
the upper teeth stay put, a copy drops as the lower jaw, and the gap is filled with a dark mouth.
"""
import os, json
import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPR = os.path.join(ROOT, 'sprites')
SKIP = {10}                    # hand over her mouth
OPEN = {1: 7, 2: 15}           # jaw drop in canvas pixels per level
INSIDE = (92, 0, 38)           # dark crimson mouth interior
TONGUE = (200, 20, 90)

def find_mouth(a, vx, vy):
    """The white teeth below her visor: the largest bright-white blob in that area."""
    h, w = a.shape[:2]
    x0, x1, y0, y1 = max(0, vx - 110), min(w, vx + 110), vy + 12, min(h, vy + 120)
    region = a[y0:y1, x0:x1].astype(int)
    white = (region[:, :, 0] > 200) & (region[:, :, 1] > 200) & (region[:, :, 2] > 200) & (region[:, :, 3] > 200)
    lab, n = ndimage.label(white, structure=np.ones((3, 3)))
    if n == 0:
        return None
    sizes = ndimage.sum(white, lab, range(1, n + 1))
    k = int(np.argmax(sizes)) + 1
    if sizes[k - 1] < 25:
        return None
    mask = np.zeros((h, w), bool)
    mask[y0:y1, x0:x1] = ndimage.binary_dilation(lab == k, iterations=1)
    return mask

def open_mouth(a, teeth, drop):
    """Thin zigzag teeth: the row is copied down as the lower jaw.
    A thick, solid grin: its top half stays and its bottom half drops."""
    out = a.copy()
    src = a.copy()
    ys, xs = np.nonzero(teeth)
    # Stroke thickness: teeth pixels per column. A zigzag line is a few px thick, a grin is a solid shape.
    thick = len(ys) / len(np.unique(xs)) > 10
    if thick:
        cut = (ys.min() + ys.max()) // 2
        upper = teeth.copy(); upper[cut:] = False
        moving = teeth.copy(); moving[:cut] = False
        out[moving] = 0  # the bottom half leaves its old place...
        out[moving, 3] = 255
        out[moving, :3] = INSIDE
    else:
        upper = teeth
        moving = teeth
    if thick:
        drop = max(3, drop * 2 // 3)  # a solid grin already reads as open; open it less
    lower = np.zeros_like(teeth); lower[drop:] = moving[:-drop]
    # Dark mouth from the middle of the upper teeth down to the moved jaw, column by column.
    for x in np.unique(xs):
        col_up = np.nonzero(upper[:, x])[0]
        col_low = np.nonzero(lower[:, x])[0]
        if len(col_up) == 0 or len(col_low) == 0:
            continue
        top = (col_up.min() + col_up.max()) // 2
        bottom = col_low.max() - 1
        if bottom > top:
            out[top:bottom + 1, x, :3] = INSIDE
            out[top:bottom + 1, x, 3] = 255
            if drop >= 12 and not thick:
                t0 = max(top, col_low.min() - 5)
                out[t0:col_low.min(), x, :3] = TONGUE
    out[upper] = src[upper]
    shifted = np.zeros_like(a); shifted[drop:] = src[:-drop]
    out[lower] = shifted[lower]
    return out

def main():
    meta = json.load(open(os.path.join(SPR, 'frames.json')))
    W, H = meta['width'], meta['height']
    vx, vy = int(meta['visor'][0] * W), int(meta['visor'][1] * H)
    voices = json.load(open(os.path.join(ROOT, 'voice-pack.json'), encoding='utf-8-sig'))
    poses = sorted({int(c.get('speech_pose', 12)) for c in voices} | {12})
    made = {}
    for p in poses:
        if p in SKIP:
            continue
        a = np.array(Image.open(os.path.join(SPR, f'frame-{p:02d}.png')).convert('RGBA'))
        teeth = find_mouth(a, vx, vy)
        if teeth is None:
            print(f'pose {p}: no mouth found, skipped'); continue
        for level, drop in OPEN.items():
            Image.fromarray(open_mouth(a, teeth, drop)).save(os.path.join(SPR, f'mouth-{p}-{level}.png'), optimize=True)
        made[str(p)] = 2
    meta['mouths'] = made
    json.dump(meta, open(os.path.join(SPR, 'frames.json'), 'w'), indent=1)
    print('mouth frames for poses', sorted(int(k) for k in made))

if __name__ == '__main__':
    main()
