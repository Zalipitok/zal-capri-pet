"""Builds Capri's talking-mouth frames for every pose she speaks in.
Run after normalize_sprites.py:  python tools/make_mouth_frames.py

Her closed mouth is a white zigzag; her open mouth (as drawn in pose 1) is a solid white grin with a
jagged top edge. For each speaking pose this erases the zigzag and paints that grin in its place,
turned to the zigzag's angle and sized to its width:
  sprites/mouth-<pose>-1.png  half open (a flatter grin)
  sprites/mouth-<pose>-2.png  wide open (the full grin)
"""
import os, json, math
import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPR = os.path.join(ROOT, 'sprites')
SKIP = {10}            # hand over her mouth
GRIN_POSE = 1          # the pose whose drawn open mouth is used as the template
HEIGHTS = {1: 0.55, 2: 0.88}     # grin height per level, relative to the template's proportions
GRIN_POSE_HEIGHTS = {1: 0.6, 2: 1.3}  # pose 1 already grins: flatter, then taller

def find_mouth(a, vx, vy):
    """The white mouth below her visor: the largest bright-white blob in that area."""
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
    mask[y0:y1, x0:x1] = lab == k
    return mask

def axis(mask):
    """Centre, direction angle (deg), length along it and thickness across it."""
    ys, xs = np.nonzero(mask)
    cx, cy = xs.mean(), ys.mean()
    cov = np.cov(np.vstack([xs - cx, ys - cy]))
    vals, vecs = np.linalg.eigh(cov)
    ux, uy = vecs[:, 1]
    if ux < 0:
        ux, uy = -ux, -uy
    along = (xs - cx) * ux + (ys - cy) * uy
    across = -(xs - cx) * uy + (ys - cy) * ux
    return cx, cy, math.degrees(math.atan2(uy, ux)), along.max() - along.min() + 1, across.max() - across.min() + 1

def grin_template(a, mask):
    """The grin as white with soft edges, straightened to horizontal."""
    cx, cy, ang, length, thick = axis(mask)
    grow = ndimage.binary_dilation(mask, iterations=2)
    ys, xs = np.nonzero(grow)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    rgb = a[y0:y1, x0:x1, :3].astype(float)
    cover = np.clip((rgb.min(axis=2) - 60) / 170.0, 0, 1) * grow[y0:y1, x0:x1]
    t = np.zeros((y1 - y0, x1 - x0, 4), np.uint8)
    t[:, :, :3] = 248
    t[:, :, 3] = (cover * 255).astype(np.uint8)
    im = Image.fromarray(t).rotate(ang, resample=Image.BICUBIC, expand=True)  # undo its tilt
    bbox = im.getbbox()
    return im.crop(bbox)

def erase(a, mask):
    """Fills the old mouth with the colours right around it."""
    grow = ndimage.binary_dilation(mask, iterations=2)
    _, (iy, ix) = ndimage.distance_transform_edt(grow, return_indices=True)
    out = a.copy()
    out[grow] = a[iy[grow], ix[grow]]
    return out

def paint(base, template, mask, height_ratio):
    cx, cy, ang, length, thick = axis(mask)
    tw, th = template.size
    w = max(8, int(round(length * 1.05)))
    h = max(4, int(round(th * (w / tw) * height_ratio)))
    g = template.resize((w, h), Image.LANCZOS).rotate(-ang, resample=Image.BICUBIC, expand=True)
    # Keep the top of the grin where the top of the old mouth was, so it opens downward.
    shift = (h - thick) / 2.0
    px = cx - math.sin(math.radians(ang)) * shift
    py = cy + math.cos(math.radians(ang)) * shift
    out = Image.fromarray(base)
    out.alpha_composite(g, (int(round(px - g.width / 2)), int(round(py - g.height / 2))))
    return out

def main():
    meta = json.load(open(os.path.join(SPR, 'frames.json')))
    W, H = meta['width'], meta['height']
    vx, vy = int(meta['visor'][0] * W), int(meta['visor'][1] * H)
    grin_src = np.array(Image.open(os.path.join(SPR, f'frame-{GRIN_POSE:02d}.png')).convert('RGBA'))
    template = grin_template(grin_src, find_mouth(grin_src, vx, vy))
    voices = json.load(open(os.path.join(ROOT, 'voice-pack.json'), encoding='utf-8-sig'))
    poses = sorted({int(c.get('speech_pose', 12)) for c in voices} | {12})
    made = {}
    for p in poses:
        if p in SKIP:
            continue
        a = np.array(Image.open(os.path.join(SPR, f'frame-{p:02d}.png')).convert('RGBA'))
        mouth = find_mouth(a, vx, vy)
        if mouth is None:
            print(f'pose {p}: no mouth found, skipped'); continue
        clean = erase(a, mouth)
        heights = GRIN_POSE_HEIGHTS if p == GRIN_POSE else HEIGHTS
        for level, ratio in heights.items():
            paint(clean, template, mouth, ratio).save(os.path.join(SPR, f'mouth-{p}-{level}.png'), optimize=True)
        made[str(p)] = 2
    meta['mouths'] = made
    json.dump(meta, open(os.path.join(SPR, 'frames.json'), 'w'), indent=1)
    print('mouth frames for poses', sorted(int(k) for k in made))

if __name__ == '__main__':
    main()
