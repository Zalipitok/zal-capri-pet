import os, json, numpy as np
from PIL import Image
from scipy import ndimage
BG = np.array([255, 255, 255], float)
def key(rgb):
    a = rgb.astype(float)
    d = np.sqrt(((a - BG) ** 2).sum(-1))
    near = d < 45
    lab, n = ndimage.label(near)
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    bgmask = np.isin(lab, list(border))
    band = ndimage.binary_dilation(bgmask, iterations=2) & ~bgmask
    alpha = np.ones(d.shape); alpha[bgmask] = 0
    alpha[band] = np.clip((d[band] - 25) / 120.0, 0, 1)
    al = alpha[..., None]
    with np.errstate(divide='ignore', invalid='ignore'):
        fg = (a - (1 - al) * BG) / np.maximum(al, 1e-3)
    out = np.where(band[..., None], np.clip(fg, 0, 255), a)
    solid = alpha > 0.3
    lab2, n2 = ndimage.label(solid)
    if n2 > 1:
        sizes = ndimage.sum(solid, lab2, range(1, n2 + 1))
        keep = np.isin(lab2, [i + 1 for i, s in enumerate(sizes) if s >= max(150, sizes.max() * 0.003)])
        alpha[~ndimage.binary_dilation(keep, iterations=2)] = 0
    # Trim the outermost pixel ring: it carries the JPEG's white halo.
    solid = alpha > 0.5
    rim = solid & ~ndimage.binary_erosion(solid, iterations=1)
    alpha[rim] *= 0.45
    return np.dstack([out, alpha * 255]).clip(0, 255).astype(np.uint8)
if __name__ == '__main__':
    os.makedirs('keyed', exist_ok=True)
    for f in sorted(os.listdir('cells')):
        Image.fromarray(key(np.array(Image.open('cells/' + f).convert('RGB')))).save('keyed/' + f)
