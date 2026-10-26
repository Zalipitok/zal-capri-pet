"""Builds Capri's eye-direction frames so her eyes can follow the cursor.
Run after enhance_sprites.py:  python tools/make_eye_frames.py
For each pose in POSES the eyes (drawn by enhance_sprites.py) are drawn again with the irises
turned toward one of 8 directions. Writes sprites/eye-<pose>-<dir>.png, dir 0..7 = right,
up-right, up, up-left, left, down-left, down, down-right (in the drawing's own orientation).
"""
import os, json, math
from PIL import Image
import enhance_sprites as E

POSES = [12, 16, 4, 19]  # hover, beckon, wave, watching


def main():
    meta_path = os.path.join(E.SPR, 'frames.json')
    meta = json.load(open(meta_path))
    eyes = {}
    for p in POSES:
        if p in E.KEEP_EYES:
            continue
        for d in range(8):
            ang = math.radians(d * 45)
            gaze = (math.cos(ang), -math.sin(ang))
            Image.fromarray(E.enhanced(meta, p, gaze)).save(os.path.join(E.SPR, f'eye-{p}-{d}.png'))
        eyes[str(p)] = 8
        print('eyes for pose', p)
    meta['eyes'] = eyes
    json.dump(meta, open(meta_path, 'w'), indent=1)


if __name__ == '__main__':
    main()
