"""Gives the original frames (sprites/frame-00..32.png) the mouth style of the per-animation
drawings: one solid white grin instead of a white zigzag with lines through it.
The grin is taken from anim-friendtalk.png, sized and turned to fit each old mouth.
Run before make_music_frames.py, make_mouth_frames.py and make_eye_frames.py (they build on the
frames), never after enhance_sprites.py's originals step without running this again."""
import os, sys, json
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_mouth_frames import grin_template, erase, paint
from make_anim_frames import find_mouth2

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPR = os.path.join(ROOT, 'sprites')
TEMPLATE = 'anim-friendtalk.png'
SKIP = {10, 27}   # hand or cup over her mouth

def template():
    a = np.array(Image.open(os.path.join(SPR, TEMPLATE)).convert('RGBA'))
    return grin_template(a, find_mouth2(a, 338, 367))

def main(poses):
    t = template()
    done = []
    for p in poses:
        if p in SKIP: continue
        path = os.path.join(SPR, f'frame-{p:02d}.png')
        a = np.array(Image.open(path).convert('RGBA'))
        m = find_mouth2(a, 338, 367)
        if m is None:
            print(f'pose {p}: no mouth found'); continue
        out = paint(erase(a, m), t, m, 1.0)
        out.save(path, optimize=True)
        done.append(p)
    print('new mouths on poses', done)

if __name__ == '__main__':
    main([int(x) for x in sys.argv[1:]] or list(range(33)))
