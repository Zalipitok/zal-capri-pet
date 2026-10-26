"""Rebuilds Capri's 33 animation frames from the sprite sheets.

Cutting the sheets into fixed cells clipped her hair wherever it crossed a cell line, and the
sheets were drawn at different scales, so she jumped and changed size between poses.
This tool:
  1. separates each figure on the full sheet (pixels go to the nearest figure, so hair that
     crosses into the next cell is kept),
  2. scales every sheet group to the same visor (eye band) width,
  3. places every frame on one shared canvas with the visor at the same spot,
  4. writes sprites/frame-NN.png plus sprites/frames.json (canvas size and where her feet are).
Run from the Capri folder:  python tools/normalize_sprites.py
Needs: pip install pillow numpy scipy scikit-image
"""
import json, os, sys
import numpy as np
from PIL import Image
from scipy import ndimage
from skimage.segmentation import watershed

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'sprites')
MARGIN = 24          # px of empty space kept around the largest frame
TARGET_VISOR = 108.0 # visor width (px) every frame is scaled to

def cells_for(sheet, w, h):
    """The rectangles Capri used to cut each sheet (x, y, w, h)."""
    cw, ch = w // 2, h // 2
    if sheet == 'poses-casting-clean.png':
        return [(0, 0, cw, 740), (cw, 0, w - cw, 740), (0, 740, 576, h - 740), (536, 740, w - 536, h - 740)]
    return [((i % 2) * cw, (i // 2) * ch, cw if i % 2 == 0 else w - cw, ch if i < 2 else h - ch) for i in range(4)]

def visor_center(rgba, box):
    """Centre of the biggest bright-green blob in the upper part of a box: her visor."""
    x, y, w, h = box
    a = rgba[y:y + h, x:x + w].astype(int)
    green = (a[:, :, 1] > 150) & (a[:, :, 0] < 120) & (a[:, :, 2] < 170) & (a[:, :, 3] > 200)
    green[int(h * 0.6):] = False
    lab, n = ndimage.label(green)
    if n == 0:
        raise SystemExit(f'no visor found in {box}')
    sizes = ndimage.sum(green, lab, range(1, n + 1))
    k = int(np.argmax(sizes)) + 1
    ys, xs = np.nonzero(lab == k)
    return x + xs.mean(), y + ys.mean(), xs.max() - xs.min() + 1

def split_sheet(path):
    """Returns [(frame_rgba, visor_x, visor_y, visor_w)] in cell order, each cropped to its figure."""
    rgba = np.array(Image.open(path).convert('RGBA'))
    h, w = rgba.shape[:2]
    boxes = cells_for(os.path.basename(path), w, h)
    alpha = rgba[:, :, 3]
    mask = alpha > 8
    # Figures are separated using solid pixels only: faint anti-aliased edges can bridge two figures
    # where a toe touches the next figure's hair. Faint pixels are given to the nearest figure at the end.
    full_mask = mask
    mask = alpha > 160
    markers = np.zeros((h, w), np.int32)
    visors = []
    for k, box in enumerate(boxes, 1):
        vx, vy, vw = visor_center(rgba, box)
        visors.append((vx, vy, vw))
        markers[max(0, int(vy) - 3):int(vy) + 4, max(0, int(vx) - 3):int(vx) + 4] = k
    markers[~mask] = 0
    # Flood outward from each visor through the figure: every pixel joins the figure it is connected to first.
    labels = watershed(np.zeros((h, w)), markers, mask=mask)
    # Detached bits (sparks, effect lines, a stray toe) belong to the nearest figure.
    loose, n = ndimage.label(mask & (labels == 0), structure=np.ones((3, 3)))
    _, (iy, ix) = ndimage.distance_transform_edt(labels == 0, return_indices=True)
    for i, obj in enumerate(ndimage.find_objects(loose), 1):
        part = loose[obj] == i
        if part.sum() < 12:
            continue
        ys, xs = np.nonzero(part)
        ys, xs = ys + obj[0].start, xs + obj[1].start
        votes = np.bincount(labels[iy[ys, xs], ix[ys, xs]], minlength=len(boxes) + 1)
        labels[obj][part] = int(np.argmax(votes[1:])) + 1
    faint = full_mask & (labels == 0)
    _, (iy, ix) = ndimage.distance_transform_edt(labels == 0, return_indices=True)
    labels[faint] = labels[iy[faint], ix[faint]]
    frames = []
    for k in range(1, len(boxes) + 1):
        keep = labels == k
        # Same speck filter as before: drop isolated bits under 12 px.
        comp, n = ndimage.label(keep, structure=np.ones((3, 3)))
        sizes = ndimage.sum(keep, comp, range(1, n + 1))
        for i, s in enumerate(sizes, 1):
            if s < 12:
                keep[comp == i] = False
        ys, xs = np.nonzero(keep)
        x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
        out = rgba[y0:y1, x0:x1].copy()
        out[~keep[y0:y1, x0:x1]] = 0
        vx, vy, vw = visors[k - 1]
        frames.append((out, vx - x0, vy - y0, vw))
    return frames

def single(path):
    rgba = np.array(Image.open(path).convert('RGBA'))
    h, w = rgba.shape[:2]
    vx, vy, vw = visor_center(rgba, (0, 0, w, h))
    keep = rgba[:, :, 3] > 8
    ys, xs = np.nonzero(keep)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    return [(rgba[y0:y1, x0:x1].copy(), vx - x0, vy - y0, vw)]

def main():
    groups = [  # sheets drawn at the same scale share one scale factor
        ['poses-clean.png', 'poses-extra-clean.png', 'poses-life-clean.png', 'poses-faces-clean.png', 'poses-casting-clean.png'],
        ['pose-shaken.png'],
        ['poses-motion.png', 'poses-daily.png', 'poses-interaction.png'],
    ]
    raw = []  # (rgba, vx, vy, scale) in Capri's pose order 0..32
    for group in groups:
        parts = []
        for sheet in group:
            path = os.path.join(ROOT, 'tools', 'sheets', sheet)
            parts += single(path) if sheet == 'pose-shaken.png' else split_sheet(path)
        scale = TARGET_VISOR / float(np.median([p[3] for p in parts]))
        raw += [(p[0], p[1], p[2], scale) for p in parts]
    assert len(raw) == 33, len(raw)

    scaled = []
    for rgba, vx, vy, s in raw:
        im = Image.fromarray(rgba)
        if abs(s - 1) > 1e-3:
            im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)
        scaled.append((im, vx * s, vy * s))
    # Canvas: the visor sits on the vertical centre line (so flipping her keeps the head in place)
    # and at the same height in every frame.
    half_w = max(max(vx, im.width - vx) for im, vx, vy in scaled) + MARGIN
    up = max(vy for im, vx, vy in scaled) + MARGIN
    down = max(im.height - vy for im, vx, vy in scaled) + MARGIN
    W, H = int(np.ceil(half_w * 2)), int(np.ceil(up + down))
    ax, ay = W / 2.0, up
    os.makedirs(OUT, exist_ok=True)
    feet = []
    for n, (im, vx, vy) in enumerate(scaled):
        canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        canvas.alpha_composite(im, (int(round(ax - vx)), int(round(ay - vy))))
        alpha = np.array(canvas)[:, :, 3]
        rows = np.nonzero(alpha.max(axis=1) > 64)[0]
        feet.append(round(float(rows.max() + 1) / H, 4))
        canvas.save(os.path.join(OUT, f'frame-{n:02d}.png'), optimize=True)
    with open(os.path.join(OUT, 'frames.json'), 'w') as f:
        json.dump({'version': 1, 'width': W, 'height': H, 'visor': [round(ax / W, 4), round(ay / H, 4)],
                   'feet': feet, 'note': 'feet = bottom of the drawn figure as a fraction of canvas height'}, f, indent=1)
    print(f'canvas {W}x{H}, visor at ({ax:.0f},{ay:.0f}); wrote {len(scaled)} frames to {OUT}')

if __name__ == '__main__':
    main()
