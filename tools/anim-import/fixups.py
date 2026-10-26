"""Hand fixes after normalize.py: the toy ball is a separate on-screen object, so the balls drawn
in the chase and bat poses are removed (she'd have two)."""
import numpy as np
from PIL import Image
from scipy import ndimage
a = np.array(Image.open('norm/chaseball.png'))
lab, k = ndimage.label(a[..., 3] > 40)
big = np.argmax(ndimage.sum(a[..., 3] > 40, lab, range(1, k + 1))) + 1
a[~ndimage.binary_dilation(lab == big, iterations=3)] = 0
Image.fromarray(a).save('norm/chaseball.png')
a = np.array(Image.open('norm/ballbat.png')); rgb = a[..., :3].astype(int)
yy, xx = np.mgrid[0:a.shape[0], 0:a.shape[1]]
dark = rgb.sum(-1) < 220
a[(np.hypot(xx - 109, yy - 349) < 47) | ((xx < 208) & (yy > 300) & (yy < 420) & ~dark) | ((xx < 165) & (yy > 300) & (yy < 420))] = 0
Image.fromarray(a).save('norm/ballbat.png')
