"""Polishes Capri's frames and redraws her eyes in the show's style.
Run after normalize_sprites.py and before the other make_*_frames.py tools:
    python tools/enhance_sprites.py
    python tools/make_music_frames.py
    python tools/make_mouth_frames.py
    python tools/make_eye_frames.py

Every frame gets one neon visor green (the sheets were drawn with different greens) and a light
sharpen. Every open-eyed frame gets the show's eyes: a black socket with a spike up toward the nose
and one down and out, a thin green ring, a blue iris and a white pupil, tilted with the visor.
Closed, squinting and blinking eyes (KEEP_EYES) stay as drawn.
The untouched frames are kept in sprites-originals\ and every run starts from them, so running this
again changes nothing. The eye positions it found are stored in frames.json ("eyeShapes") and
make_eye_frames.py uses them to draw the irises looking in 8 directions.
"""
import os, json, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPR = os.path.join(ROOT, 'sprites')
VISOR_GREEN = np.array([3, 240, 70], dtype=float)
SS = 4  # supersampling for the drawn eyes
# The frames as normalize_sprites.py made them; every run starts from these.
ORIGINALS = os.path.join(ROOT, 'sprites-originals')

# Eye colours, from the show.
SOCKET = (6, 8, 14, 255)
IRIS = (44, 112, 226, 255)
PUPIL = (250, 252, 255, 255)
# Frames whose eyes stay as drawn: blink (13), squint and laughing (3, 10, 11), asleep (28).
KEEP_EYES = [3, 10, 11, 13, 28]

def load(i):
    return np.array(Image.open(os.path.join(SPR, f'frame-{i:02d}.png')).convert('RGBA'))


def green_mask(a):
    r, g, b, al = [a[..., k].astype(int) for k in range(4)]
    return (g > 110) & (g > r + 60) & (g > b + 20) & (al > 150)


def visor_component(a, vx, vy):
    """The visor: the largest green region near where the visor sits."""
    h, w = a.shape[:2]
    y0, y1, x0, x1 = max(0, vy - 120), min(h, vy + 90), max(0, vx - 170), min(w, vx + 170)
    m = np.zeros(a.shape[:2], bool)
    m[y0:y1, x0:x1] = green_mask(a)[y0:y1, x0:x1]
    lab, n = ndimage.label(m)
    if n == 0:
        return m
    sizes = ndimage.sum(m, lab, range(1, n + 1))
    return lab == (np.argmax(sizes) + 1)


def visor_angle(visor):
    ys, xs = np.nonzero(visor)
    xs = xs - xs.mean(); ys = ys - ys.mean()
    cov = np.cov(np.vstack([xs, ys]))
    vals, vecs = np.linalg.eigh(cov)
    v = vecs[:, np.argmax(vals)]
    ang = math.degrees(math.atan2(v[1], v[0]))
    if ang > 90: ang -= 180
    if ang < -90: ang += 180
    return ang


def find_ring_eyes(a, vx, vy):
    """The two ring eyes: blue rings with a white pupil inside, near the visor."""
    h, w = a.shape[:2]
    x0, x1, y0, y1 = max(0, vx - 160), min(w, vx + 160), max(0, vy - 100), min(h, vy + 60)
    r, g, b, al = [a[y0:y1, x0:x1, k].astype(int) for k in range(4)]
    blue = (b > 130) & (r < 120) & (b > g + 25) & (al > 200)
    lab, n = ndimage.label(blue, structure=np.ones((3, 3)))
    eyes = []
    for k in range(1, n + 1):
        ring = lab == k
        if ring.sum() < 40:
            continue
        filled = ndimage.binary_fill_holes(ring)
        ys, xs = np.nonzero(filled)
        rad = math.sqrt(filled.sum() / math.pi)
        eyes.append((float(xs.mean() + x0), float(ys.mean() + y0), float(rad), int(ring.sum())))
    # The two eyes are similar in size and sit a little apart on the visor.
    best, score = None, -1
    for i in range(len(eyes)):
        for j in range(i + 1, len(eyes)):
            a, b = eyes[i], eyes[j]
            d = math.hypot(a[0] - b[0], a[1] - b[1])
            if not (3.0 * max(a[2], b[2]) <= d <= 9.0 * max(a[2], b[2])):
                continue
            similar = min(a[2], b[2]) / max(a[2], b[2])
            sc = (a[3] + b[3]) * similar
            if sc > score:
                best, score = (a, b), sc
    if best is None:
        return []
    eyes = sorted(best, key=lambda e: e[0])
    return [e[:3] for e in eyes]


def unify_green(a):
    """Every frame gets the same neon visor green; darker greens keep their relative shade."""
    out = a.astype(float)
    m = green_mask(a)
    if m.sum() < 50:
        return a
    med = np.median(out[m][:, :3], axis=0)
    gain = VISOR_GREEN / np.maximum(med, 1)
    # Feather the change over near-green pixels so edges don't band.
    r, g, b = out[..., 0], out[..., 1], out[..., 2]
    greenness = np.clip(((g - np.maximum(r, b)) - 20) / 60.0, 0, 1) * (a[..., 3] > 0)
    for c in range(3):
        target = np.clip(out[..., c] * gain[c], 0, 255)
        out[..., c] = out[..., c] * (1 - greenness) + target * greenness
    return np.clip(out, 0, 255).astype(np.uint8)


def sharpen(a):
    rgb = Image.fromarray(a[..., :3])
    rgb = rgb.filter(ImageFilter.UnsharpMask(radius=1.4, percent=70, threshold=2))
    out = a.copy()
    out[..., :3] = np.array(rgb)
    return out


def spike(cx, cy, ca, sa, dirx, diry, base_r, tip_r, half):
    """A sharp triangle sticking out of the socket toward (dirx, diry) in eye-local coordinates."""
    n = math.hypot(dirx, diry); dx, dy = dirx / n, diry / n
    px, py = -dy, dx
    pts = [(dx * tip_r, dy * tip_r), (dx * base_r + px * half, dy * base_r + py * half), (dx * base_r * 0.6 - px * half, dy * base_r * 0.6 - py * half)]
    pts = [(dx * tip_r, dy * tip_r), (dx * base_r * 0.7 + px * half, dy * base_r * 0.7 + py * half), (dx * base_r * 0.7 - px * half, dy * base_r * 0.7 - py * half)]
    return [(cx + x * ca - y * sa, cy + x * sa + y * ca) for x, y in pts]


def draw_eye(layer, cx, cy, R, angle, inner_dir, ring_colour, gaze=(0.0, 0.0)):
    """Her eye as in the show: a black socket with a spike up toward the nose and one down and out,
    a thin green ring, a blue iris and a white pupil. R is the green ring's radius. inner_dir is +1
    when the eye's inner corner points right. gaze moves the iris (each component -1..1)."""
    ca, sa = math.cos(math.radians(angle)), math.sin(math.radians(angle))
    S = lambda pts: [(x * SS, y * SS) for x, y in pts]
    box = lambda x, y, r: [(x - r) * SS, (y - r) * SS, (x + r) * SS, (y + r) * SS]
    d = ImageDraw.Draw(layer)
    # Socket: a slightly wide oval plus two spikes.
    oval = []
    for k in range(64):
        t = 2 * math.pi * k / 64
        x, y = math.cos(t) * R * 1.6, math.sin(t) * R * 1.36
        oval.append((cx + x * ca - y * sa, cy + x * sa + y * ca))
    d.polygon(S(oval), fill=SOCKET)
    d.polygon(S(spike(cx, cy, ca, sa, 0.55 * inner_dir, -1.0, R * 1.2, R * 2.35, R * 0.38)), fill=SOCKET)
    d.polygon(S(spike(cx, cy, ca, sa, -0.80 * inner_dir, 1.0, R * 1.2, R * 2.25, R * 0.38)), fill=SOCKET)
    # Thin green ring.
    w = max(1.0, R * 0.11)
    d.ellipse(box(cx, cy, R), outline=tuple(int(c) for c in ring_colour), width=max(1, int(round(w * SS))))
    # Iris and pupil, turned toward the gaze.
    gx, gy = gaze
    ix, iy = cx + gx * R * 0.24, cy + gy * R * 0.24
    d.ellipse(box(ix, iy, R * 0.68), fill=IRIS)
    d.ellipse(box(ix, iy, R * 0.30), fill=PUPIL)


def find_eyes(a, visor):
    """Both eyes of a frame: the ring eyes of the old frames, or the blue irises of the newer ones.
    Returns ((x, y), (x, y)) left to right, or None."""
    ys, xs = np.nonzero(visor)
    if len(xs) < 100:
        return None
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    span = math.hypot(x1 - x0, y1 - y0)
    r, g, b, al = [a[y0:y1, x0:x1, k].astype(int) for k in range(4)]
    blue = (b > 110) & (r < 130) & (b > g + 20) & (al > 200)
    lab, n = ndimage.label(blue, structure=np.ones((3, 3)))
    blobs = []
    for k in range(1, n + 1):
        m = lab == k
        if m.sum() < 15:
            continue
        yy, xx = np.nonzero(ndimage.binary_fill_holes(m))
        blobs.append((xx.mean() + x0, yy.mean() + y0, m.sum()))
    best, score = None, -1
    for i in range(len(blobs)):
        for j in range(i + 1, len(blobs)):
            p, q = blobs[i], blobs[j]
            dist = math.hypot(p[0] - q[0], p[1] - q[1])
            if not (0.18 * span <= dist <= 0.62 * span):
                continue
            sc = min(p[2], q[2])
            if sc > score:
                best, score = (p, q), sc
    if best is None:
        return None
    p, q = sorted(best, key=lambda e: e[0])
    return (p[0], p[1]), (q[0], q[1])


def hull_of(visor):
    try:
        from skimage.morphology import convex_hull_image
        return convex_hull_image(visor)
    except Exception:
        return ndimage.binary_fill_holes(visor)


# Per-frame adjustments: a bigger erase or eye size where the old eyes were drawn unusually.
TUNE = {7: {'erase': 1.7, 'R': 0.33}}


def cup_mask(a):
    """Frame 27 drinks from a cup that covers part of her left eye: everything under the cup's
    white rim, so the new eye goes behind the cup like the old one did."""
    h, w = a.shape[:2]
    r, g, b = [a[..., k].astype(int) for k in range(3)]
    rim = (r > 200) & (g > 200) & (b > 200) & (a[..., 3] > 200)
    lab, n = ndimage.label(rim, structure=np.ones((3, 3)))
    if n == 0:
        return np.zeros((h, w), bool)
    sizes = ndimage.sum(rim, lab, range(1, n + 1))
    biggest = lab == (np.argmax(sizes) + 1)
    ys, xs = np.nonzero(biggest)
    mask = np.zeros((h, w), bool)
    for x in range(xs.min(), xs.max() + 1):
        col = ys[xs == x]
        if len(col):
            mask[col.min():, x] = True
    return mask


OCCLUDERS = {27: cup_mask}


def visor_green(a, visor):
    inside = a[visor & (a[..., 3] > 200)]
    g = np.median(inside, axis=0).astype(np.uint8) if len(inside) else np.array([3, 240, 70, 255], np.uint8)
    g[3] = 255
    return g


def eye_shapes(meta, i, a):
    """Eye positions for a frame: stored in frames.json (from the first run), or found now."""
    store = meta.setdefault('eyeShapes', {})
    if str(i) in store:
        return store[str(i)]
    W, H = a.shape[1], a.shape[0]
    vx, vy = int(meta['visor'][0] * W), int(meta['visor'][1] * H)
    visor = visor_component(a, vx, vy)
    found = find_eyes(a, visor)
    if found is None:
        return None
    (x0, y0), (x1, y1) = found
    store[str(i)] = {'eyes': [[x0, y0], [x1, y1]], 'angle': math.degrees(math.atan2(y1 - y0, x1 - x0)), 'sep': math.hypot(x1 - x0, y1 - y0)}
    return store[str(i)]


def redraw_eyes(meta, i, a, gaze=(0.0, 0.0)):
    """Erases the eyes inside the visor and draws the show's eyes in their place."""
    W, H = a.shape[1], a.shape[0]
    vx, vy = int(meta['visor'][0] * W), int(meta['visor'][1] * H)
    shapes = eye_shapes(meta, i, a)
    if shapes is None:
        return a
    visor = visor_component(a, vx, vy)
    hull = hull_of(visor)
    green = visor_green(a, visor)
    sep, ang = shapes['sep'], shapes['angle']
    ca, sa = math.cos(math.radians(ang)), math.sin(math.radians(ang))
    ys, xs = np.mgrid[0:H, 0:W]
    out = a.copy()
    for ex, ey in shapes['eyes']:
        u = (xs - ex) * ca + (ys - ey) * sa
        v = -(xs - ex) * sa + (ys - ey) * ca
        k = TUNE.get(i, {}).get('erase', 1.0)
        out[((u / (0.64 * sep * k)) ** 2 + (v / (0.52 * sep * k)) ** 2 <= 1) & hull] = green
    layer = Image.new('RGBA', (W * SS, H * SS), (0, 0, 0, 0))
    R = TUNE.get(i, {}).get('R', 0.22) * sep
    (lx, ly), (rx, ry) = shapes['eyes']
    draw_eye(layer, lx, ly, R, ang, +1, green, gaze)
    draw_eye(layer, rx, ry, R, ang, -1, green, gaze)
    layer = layer.resize((W, H), Image.LANCZOS)
    # Nothing outside the visor.
    clip = Image.fromarray((ndimage.binary_dilation(hull, iterations=1) * 255).astype(np.uint8))
    alpha = np.minimum(np.array(layer.getchannel('A')), np.array(clip))
    layer.putalpha(Image.fromarray(alpha))
    base = Image.fromarray(out)
    base.alpha_composite(layer)
    result = np.array(base)
    if i in OCCLUDERS:
        cover = OCCLUDERS[i](a)
        result[cover] = a[cover]
    return result


def enhanced(meta, i, gaze=(0.0, 0.0)):
    """The finished frame i (from its original), with the eyes looking toward gaze."""
    a = np.array(Image.open(os.path.join(ORIGINALS, f'frame-{i:02d}.png')).convert('RGBA'))
    a = unify_green(a)
    if i not in KEEP_EYES:
        a = redraw_eyes(meta, i, a, gaze)
    return sharpen(a)


def main():
    meta_path = os.path.join(SPR, 'frames.json')
    meta = json.load(open(meta_path))
    originals = ORIGINALS
    os.makedirs(originals, exist_ok=True)
    for i in range(33):
        path = os.path.join(SPR, f'frame-{i:02d}.png')
        keep = os.path.join(originals, f'frame-{i:02d}.png')
        # (normalize_sprites.py writes fresh frames: delete sprites-originals after running it.)
        # Always start from the untouched frame, so running this twice changes nothing.
        if not os.path.exists(keep):
            Image.open(path).save(keep)
        a = enhanced(meta, i)
        Image.fromarray(a).save(path)
        print('frame', i, 'eyes redrawn' if str(i) in meta.get('eyeShapes', {}) else 'eyes kept')
    meta['enhanced'] = 1
    json.dump(meta, open(meta_path, 'w'), indent=1)


if __name__ == '__main__':
    main()
