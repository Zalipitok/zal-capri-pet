"""Builds the variants of the per-animation sprites (sprites/anim-<state>.png):
  anim-<state>-blink.png        eyes closed (for animations that blink)
  anim-<state>-mouth-1/2.png    talking mouth, half and wide open
  anim-<state>-hp.png           headphones on (animations she does while music plays)
and lists everything in frames.json under "anims" (with each sprite's feet position).
Run after tools/anim-import/import_sheet.py:  python tools/make_anim_frames.py
"""
import os, json, sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_mouth_frames import find_mouth, grin_template, erase, paint, HEIGHTS, GRIN_POSE
from make_music_frames import add_headphones

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPR = os.path.join(ROOT, 'sprites')
CENTER = (338, 367)
HEADPHONES = ['float', 'wander', 'perch', 'windowperch', 'roam', 'taskbar', 'taskpeek', 'dance', 'gaming', 'sip', 'nap',
              'smug', 'wave', 'sleepy', 'meditate', 'chatreply', 'bossfight', 'gamerwin', 'yawn', 'stretch', 'sideglance']
NO_MOUTH = {'laughing', 'sip', 'nap'}   # mouth covered or asleep


def visor_parts(a):
    r, g, b, al = [a[..., i].astype(int) for i in range(4)]
    green = (g > 170) & (r < 130) & (b < 170) & (g > r + 60) & (al > 200)
    lab, n = ndimage.label(green)
    best, bestd = 0, 1e9
    for k in range(1, n + 1):
        ys, xs = np.nonzero(lab == k)
        if len(xs) < 300:
            continue
        d = np.hypot(xs.mean() - CENTER[0], ys.mean() - CENTER[1])
        if d < bestd:
            best, bestd = k, d
    if not best or bestd > 40:
        return None
    comp = lab == best
    return comp, ndimage.binary_fill_holes(comp)


def blink(a):
    parts = visor_parts(a)
    if parts is None:
        return None
    comp, filled = parts
    holes = filled & ~comp
    lab, n = ndimage.label(holes)
    if n == 0:
        return None
    sizes = ndimage.sum(holes, lab, range(1, n + 1))
    eyes = [k + 1 for k in np.argsort(sizes)[::-1][:2] if sizes[k] >= 40]
    if not eyes:
        return None
    colour = np.median(a[comp][:, :3], axis=0)
    out = a.copy()
    im = None
    lids = []
    for k in eyes:
        eye = lab == k
        grow = ndimage.binary_dilation(eye, iterations=2) & filled
        out[grow, :3] = colour
        out[grow, 3] = 255
        ys, xs = np.nonzero(eye)
        lids.append((xs.min(), xs.max(), ys.mean() + (ys.max() - ys.min()) * 0.15, ys.max() - ys.min()))
    im = Image.fromarray(out)
    d = ImageDraw.Draw(im)
    for x0, x1, y, h in lids:
        width = max(4, int(round(h * 0.2)))
        pts = [(x, y + 0.18 * h * (1 - ((2 * (x - x0) / max(1, x1 - x0)) - 1) ** 2)) for x in np.linspace(x0 + 1, x1 - 1, 24)]
        d.line(pts, fill=(14, 8, 22, 255), width=width, joint='curve')
    return im


def main():
    meta_path = os.path.join(SPR, 'frames.json')
    meta = json.load(open(meta_path))
    feet = json.load(open(os.path.join(SPR, 'anim-feet.json')))
    W, H = meta['width'], meta['height']
    vx, vy = int(meta['visor'][0] * W), int(meta['visor'][1] * H)
    grin_src = np.array(Image.open(os.path.join(SPR, f'frame-{GRIN_POSE:02d}.png')).convert('RGBA'))
    template = grin_template(grin_src, find_mouth(grin_src, vx, vy))
    behaviours = json.load(open(os.path.join(ROOT, 'behaviours.json'), encoding='utf-8'))
    blinkers = {s['id'] for s in behaviours['states'] if 'blink' in s.get('tags', [])}
    anims = {}
    for f in sorted(os.listdir(SPR)):
        if not (f.startswith('anim-') and f.endswith('.png')) or f.count('-') != 1:
            continue
        name = f[5:-4]
        a = np.array(Image.open(os.path.join(SPR, f)).convert('RGBA'))
        info = {'feet': feet[name]}
        if name in blinkers:
            b = blink(a)
            if b is not None:
                b.save(os.path.join(SPR, f'anim-{name}-blink.png'), optimize=True)
                info['blink'] = True
        if name not in NO_MOUTH:
            mouth = find_mouth(a, vx, vy)
            if mouth is not None and mouth.sum() >= 40:
                clean = erase(a, mouth)
                for level, ratio in HEIGHTS.items():
                    paint(clean, template, mouth, ratio).save(os.path.join(SPR, f'anim-{name}-mouth-{level}.png'), optimize=True)
                info['mouth'] = True
        if name in HEADPHONES:
            try:
                im = Image.fromarray(a)
                add_headphones(im, a).save(os.path.join(SPR, f'anim-{name}-hp.png'), optimize=True)
                info['headphones'] = True
            except Exception as e:
                print(name, 'no headphones:', e)
        anims[name] = info
    meta['anims'] = anims
    json.dump(meta, open(meta_path, 'w'), indent=1)
    print(len(anims), 'animations;', sum('blink' in v for v in anims.values()), 'blink,',
          sum('mouth' in v for v in anims.values()), 'mouth,', sum('headphones' in v for v in anims.values()), 'headphones')


if __name__ == '__main__':
    main()
