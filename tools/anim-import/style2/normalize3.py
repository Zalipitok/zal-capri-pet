"""One shared scale for the whole sheet (it was drawn at a consistent size), face at the same spot
on Capri's 677x897 canvas; poses that would run off the canvas are nudged, not shrunk."""
import os, json
import numpy as np
from PIL import Image
W, H, FX, FY, S0, M = 677, 897, 338, 367, 1.28, 6
F = json.load(open('face.json'))
os.makedirs('norm', exist_ok=True)
rep = {}
# Some drawings on the sheet are smaller than the rest: even out by hair size (most of what you see).
pink = {}
for f in os.listdir('keyed'):
    a = np.array(Image.open('keyed/' + f)).astype(int)
    pink[f[:-4]] = ((a[..., 0] > 170) & (a[..., 1] < 120) & (a[..., 3] > 128)).sum()
PM = float(np.median(list(pink.values())))
for f in sorted(os.listdir('keyed')):
    n = f[:-4]; rgba = np.array(Image.open('keyed/' + f))
    fx, fy = F[n][0], F[n][1]
    S = S0 * min(1.4, max(0.85, (PM / pink[n]) ** 0.5))
    ys, xs = np.nonzero(rgba[..., 3] > 20)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    ox, oy = FX - fx * S, FY - fy * S
    l, r, t, b = ox + x0 * S, ox + x1 * S, oy + y0 * S, oy + y1 * S
    if l < M: ox += M - l
    if r > W - M: ox -= r - (W - M)
    if t < M: oy += M - t
    if b > H - M: oy -= b - (H - M)
    crop = Image.fromarray(rgba).crop((x0, y0, x1, y1))
    pm = np.array(crop).astype(float); pm[..., :3] *= pm[..., 3:4] / 255
    big = Image.fromarray(pm.clip(0, 255).astype(np.uint8)).resize((round((x1 - x0) * S), round((y1 - y0) * S)), Image.LANCZOS)
    bb = np.array(big).astype(float); al = bb[..., 3:4] / 255
    with np.errstate(divide='ignore', invalid='ignore'):
        bb[..., :3] = np.where(al > 0, bb[..., :3] / np.maximum(al, 1e-3), 0)
    canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    canvas.alpha_composite(Image.fromarray(bb.clip(0, 255).astype(np.uint8)), (int(round(ox + x0 * S)), int(round(oy + y0 * S))))
    out = np.array(canvas); out[out[..., 3] < 8] = 0
    Image.fromarray(out).save('norm/' + f)
    yy, _ = np.nonzero(out[..., 3] > 128)
    moved = (ox + fx * S - FX, oy + fy * S - FY)
    rep[n] = {'feet': round(float((yy.max() + 1) / H), 4), 'face': [round(ox + fx * S), round(oy + fy * S)]}
    if abs(moved[0]) > 1 or abs(moved[1]) > 1: print('nudged', n, [round(m) for m in moved])
json.dump(rep, open('norm.json', 'w'), indent=1)
