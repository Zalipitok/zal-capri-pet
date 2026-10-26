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
# The current drawings (2nd style: small face, long body) don't suit painted-on headphones or mouths:
# she bobs to music and talks with motion instead.
MOUTHS = False
BLINKS = False
HEADPHONES_ON = False
HEADPHONES = ['float', 'wander', 'perch', 'windowperch', 'roam', 'taskbar', 'taskpeek', 'dance', 'gaming', 'sip', 'nap',
              'smug', 'wave', 'sleepy', 'meditate', 'chatreply', 'bossfight', 'gamerwin', 'yawn', 'stretch', 'sideglance']
# Drawings that came out chopped at the edges: these animations borrow a clean one instead.
ALIASES = {}
NO_MOUTH = {'laughing', 'sip', 'nap', 'spin', 'thrown', 'upsidedown', 'tumble', 'yawn', 'dash', 'stretch', 'peek', 'rivalsneak', 'zigzag', 'ballsteal', 'pendulum'}  # mouth hidden, asleep, upside down or already wide open


def find_mouth2(a, vx, vy):
    """Her mouth: a white shape just below the visor with black all around it (hair highlights
    have pink around them instead)."""
    h, w = a.shape[:2]
    x0, x1, y0, y1 = vx - 75, vx + 75, vy + 10, vy + 100
    reg = a[y0:y1, x0:x1].astype(int)
    white = (reg[..., :3].min(-1) > 110) & (reg[..., 3] > 200)
    parts = visor_parts(a)
    if parts is not None:   # eye whites and highlights are in the eye holes: not the mouth
        comp, filled = parts
        holes = filled & ~comp
        hl, hn = ndimage.label(holes)
        if hn:
            ai = a.astype(int)
            blue = (ai[..., 2] > 130) & (ai[..., 2] > ai[..., 0] + 40) & (ai[..., 2] > ai[..., 1] + 20)
            blues = ndimage.sum(blue & holes, hl, range(1, hn + 1))
            eyes = np.isin(hl, [k + 1 for k in range(hn) if blues[k] >= 15])
            white &= ~ndimage.binary_dilation(eyes, iterations=3)[y0:y1, x0:x1]
    lab, n = ndimage.label(white, structure=np.ones((3, 3)))
    best, bestscore = None, 0
    for k in range(1, n + 1):
        m = lab == k
        size = m.sum()
        if size < 10:
            continue
        ring = ndimage.binary_dilation(m, iterations=3) & ~m
        px = reg[ring][:, :3]
        dark = ((px[:, 0] * 0.3 + px[:, 1] * 0.59 + px[:, 2] * 0.11) < 100).mean()
        if dark < 0.55:
            continue
        score = size * dark
        if score > bestscore:
            best, bestscore = m, score
    if best is None:
        return None
    mask = np.zeros((h, w), bool)
    mask[y0:y1, x0:x1] = best
    return mask


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
        if name in ALIASES:
            continue
        a = np.array(Image.open(os.path.join(SPR, f)).convert('RGBA'))
        info = {'feet': feet[name]}
        if BLINKS and name in blinkers:
            b = blink(a)
            if b is not None:
                b.save(os.path.join(SPR, f'anim-{name}-blink.png'), optimize=True)
                info['blink'] = True
        if MOUTHS and name not in NO_MOUTH:
            mouth = find_mouth2(a, vx, vy)
            if mouth is not None:
                clean = erase(a, mouth)
                for level, ratio in HEIGHTS.items():
                    paint(clean, template, mouth, ratio).save(os.path.join(SPR, f'anim-{name}-mouth-{level}.png'), optimize=True)
                info['mouth'] = True
        if HEADPHONES_ON and name in HEADPHONES:
            try:
                im = Image.fromarray(a)
                add_headphones(im, a).save(os.path.join(SPR, f'anim-{name}-hp.png'), optimize=True)
                info['headphones'] = True
            except Exception as e:
                print(name, 'no headphones:', e)
        anims[name] = info
    for name, target in ALIASES.items():
        anims[name] = {'use': target}
    # 256-colour palettes keep the files (and the download) about five times smaller.
    for f in os.listdir(SPR):
        if f.startswith('anim-') and f.endswith('.png'):
            path = os.path.join(SPR, f)
            im = Image.open(path)
            if im.mode != 'P':
                im.convert('RGBA').quantize(256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE).save(path, optimize=True)
    meta['anims'] = anims
    json.dump(meta, open(meta_path, 'w'), indent=1)
    print(len(anims), 'animations;', sum('blink' in v for v in anims.values()), 'blink,',
          sum('mouth' in v for v in anims.values()), 'mouth,', sum('headphones' in v for v in anims.values()), 'headphones')


if __name__ == '__main__':
    main()
