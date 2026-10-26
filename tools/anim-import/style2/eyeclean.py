"""Cleans up the eyes: AI-drawn eyes often have doubled outlines and stray colours (orange,
yellow, purple smears). Around each eye every pixel is snapped to her eye palette (outline,
two blues, white, visor green), then tiny specks are smoothed out."""
import os, sys, numpy as np
from PIL import Image
from scipy import ndimage
FX, FY = 338, 367
PAL = np.array([(14, 8, 22), (28, 46, 150), (55, 105, 225), (246, 247, 252), (36, 232, 112)], float)

def eye_centres(A):
    blue = (A[..., 2] > 120) & (A[..., 2] > A[..., 0] + 40) & (A[..., 3] > 200)
    yy, xx = np.mgrid[0:A.shape[0], 0:A.shape[1]]
    blue &= (abs(xx - FX) < 75) & (abs(yy - FY) < 60)
    lab, n = ndimage.label(ndimage.binary_closing(blue, iterations=2))
    out = []
    for k in range(1, n + 1):
        m = lab == k; s = m.sum()
        if s < 15: continue
        ys, xs = np.nonzero(m)
        out.append((xs.mean(), ys.mean(), max(xs.max() - xs.min(), ys.max() - ys.min()) / 2 + 1, s))
    out.sort(key=lambda e: -e[3])
    return out[:2]

def clean(a):
    """Only touches the stray colours right around each eye: orange/yellow smears are painted
    over with the nearest clean neighbouring colour, and pink/purple tints are turned into her
    eye blue (or white, for the brightest highlights). Everything else stays as drawn."""
    eyes = eye_centres(a.astype(int))
    if not eyes: return a, 0
    A = a[..., :3].astype(int)
    R, G, B = A[..., 0], A[..., 1], A[..., 2]
    yy, xx = np.mgrid[0:a.shape[0], 0:a.shape[1]]
    near = np.zeros(a.shape[:2], bool); core = near.copy()
    for ex, ey, r, _ in eyes:
        near |= np.hypot(xx - ex, yy - ey) < r * 1.35 + 3
        core |= np.hypot(xx - ex, yy - ey) < r * 0.85 + 1
    near &= a[..., 3] > 200
    # keep away from her red/pink hair
    hair = (R > 150) & (G < 90) & (B < 140) & (R > B + 60)
    near &= ~ndimage.binary_dilation(hair, iterations=2)
    warm = near & (R > 95) & (G > 55) & (B < 110) & (R > B + 40) & (G < R + 15)
    lime = core & near & (R > 110) & (G > 180) & (B < 150) & ~warm
    tan = core & near & (R > 110) & (G > 110) & (B < 140) & (abs(R - G) < 40) & ~lime
    warm |= tan
    pink = near & (R > 110) & (B > 110) & (G < np.minimum(R, B) - 25) & ~warm
    out = a.copy()
    if warm.any():
        clean_ = ~ndimage.binary_dilation(warm, iterations=1)
        _, (iy, ix) = ndimage.distance_transform_edt(~clean_, return_indices=True)
        fill = ndimage.binary_dilation(warm, iterations=1) & near
        out[fill, :3] = a[iy[fill], ix[fill], :3]
    out[lime, :3] = (246, 247, 252)
    if pink.any():
        V = np.maximum(np.maximum(R, G), B).astype(float)
        bv = V[pink]
        col = np.stack([0.27 * bv, 0.47 * bv, bv], -1)
        wh = bv > 225
        col[wh] = (246, 247, 252)
        out[pink, :3] = np.clip(col, 0, 255).astype(np.uint8)
    return out, len(eyes)

if __name__ == '__main__':
    for f in sys.argv[2:]:
        a = np.array(Image.open(os.path.join(sys.argv[1], f'{f}.png')).convert('RGBA'))
        o, n = clean(a)
        Image.fromarray(o).save(os.path.join(sys.argv[1], f'{f}.png'))
