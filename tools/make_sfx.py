"""Capri's sound effects, synthesized (no samples): python tools/make_sfx.py writes sounds/*.wav.
Short, bright, a bit glitchy, so they fit her netrunner look and don't get annoying."""
import os, wave
import numpy as np

SR = 44100
rng = np.random.default_rng(7)
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'sounds')

def t(sec): return np.arange(int(SR * sec)) / SR
def env(n, a=0.005, r=0.1, total=None):
    """Attack/exponential-release envelope of n samples."""
    x = np.arange(n) / SR
    e = np.minimum(1.0, x / max(a, 1e-4))
    return e * np.exp(-x / max(r, 1e-4))
def tone(freq, sec, kind='sine', phase=0.0):
    x = t(sec)
    f = np.full_like(x, freq) if np.isscalar(freq) else freq
    ph = 2 * np.pi * np.cumsum(f) / SR + phase
    if kind == 'sine': return np.sin(ph)
    if kind == 'tri': return 2 / np.pi * np.arcsin(np.sin(ph))
    if kind == 'square': return np.sign(np.sin(ph)) * 0.6
    if kind == 'saw': return 2 * ((ph / (2 * np.pi)) % 1.0) - 1
def glide(f0, f1, sec, curve=1.0):
    k = np.linspace(0, 1, int(SR * sec)) ** curve
    return f0 * (f1 / f0) ** k
def noise(sec): return rng.uniform(-1, 1, int(SR * sec))
def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR); y = np.zeros_like(x); p = 0.0
    for i, v in enumerate(x): p = (1 - a) * v + a * p; y[i] = p
    return y
def bandpass(x, lo, hi): return lowpass(x, hi) - lowpass(x, lo)
def mix(*parts):
    n = max(int(at * SR) + len(p) for at, p in parts); y = np.zeros(n)
    for at, p in parts: s = int(at * SR); y[s:s + len(p)] += p[:max(0, n - s)]
    return y
def crush(x, steps=24, hold=3):
    y = np.round(x * steps) / steps
    return np.repeat(y[::hold], hold)[:len(x)]
def save(name, x, peak=0.7):
    x = np.concatenate([x, np.zeros(int(SR * 0.02))])
    fade = min(len(x), int(SR * 0.01)); x[-fade:] *= np.linspace(1, 0, fade)
    x = x / (np.abs(x).max() + 1e-9) * peak
    with wave.open(os.path.join(OUT, name), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype('<i2').tobytes())
    print(name, round(len(x) / SR, 2), 's')

def chime(freq, sec, decay):
    x = tone(freq, sec) + 0.35 * tone(freq * 2.01, sec) + 0.12 * tone(freq * 3.0, sec)
    return x * env(len(x), 0.003, decay)

# head pat: a happy little purr-warble under a soft rising sparkle
purr = tone(glide(180, 230, 0.6), 0.6, 'tri') * (0.55 + 0.45 * np.sin(2 * np.pi * 22 * t(0.6))) * env(int(SR * 0.6), 0.04, 0.35)
save('headpat.wav', mix((0, 0.5 * lowpass(purr, 1800)), (0.05, chime(1318, 0.5, 0.18)), (0.16, chime(1568, 0.5, 0.18)), (0.27, chime(2093, 0.7, 0.3))))

# swat: a slap (noise crack) and a falling cartoon "bwoing"
crack = bandpass(noise(0.08), 900, 6000) * env(int(SR * 0.08), 0.001, 0.02)
bwoing = tone(glide(520, 140, 0.32, 0.6), 0.32, 'square') * env(int(SR * 0.32), 0.002, 0.12)
save('swat.wav', mix((0, crack), (0.015, 0.6 * lowpass(bwoing, 2500))))

# caught (a click on her): a startled "eep!" chirp with a glitch tick
eep = tone(glide(700, 1700, 0.11, 0.5), 0.11, 'tri') * env(int(SR * 0.11), 0.002, 0.06)
tick = crush(tone(3200, 0.03, 'square'), 6, 4) * env(int(SR * 0.03), 0.001, 0.008)
save('caught.wav', mix((0, tick * 0.5), (0.02, eep), (0.12, 0.6 * eep[: int(SR * 0.07)])))

# throw: a whoosh
wsec = 0.45; wn = noise(wsec); k = np.linspace(0, 1, len(wn))
def vary_lowpass(x, cut):
    y = np.zeros_like(x); p = 0.0
    for i, v in enumerate(x): a = np.exp(-2 * np.pi * cut[i] / SR); p = (1 - a) * v + a * p; y[i] = p
    return y
hump = np.sin(np.pi * k)   # the pitch rises then falls as she flies past
sweep = vary_lowpass(wn, 900 + 5000 * hump) - vary_lowpass(wn, 250 + 1500 * hump)
save('throw.wav', sweep * hump ** 1.5, 0.55)

# landing: a soft thump and a tiny bounce
thump = mix((0, lowpass(noise(0.12), 400) * env(int(SR * 0.12), 0.001, 0.04)), (0, tone(glide(110, 55, 0.15), 0.15) * env(int(SR * 0.15), 0.001, 0.05)))
save('land.wav', mix((0, thump), (0.13, 0.3 * thump[: int(SR * 0.08)])), 0.65)

# ball: a rubbery boing
boing = tone(glide(300, 520, 0.35, 0.3) * (1 + 0.06 * np.sin(2 * np.pi * 28 * t(0.35))), 0.35, 'tri') * env(int(SR * 0.35), 0.002, 0.12)
save('ballhit.wav', mix((0, lowpass(noise(0.02), 3000) * 0.5), (0.005, boing)), 0.6)

# catch-me point: a bright double ping
save('catch.wav', mix((0, chime(1760, 0.18, 0.05)), (0.07, chime(2637, 0.3, 0.09))), 0.6)

# her own cursor clicking: a tiny digital tick-blip
save('cursorclick.wav', mix((0, crush(tone(2400, 0.04, 'square'), 4, 5) * env(int(SR * 0.04), 0.001, 0.01)), (0.03, 0.6 * chime(3520, 0.08, 0.02))), 0.45)

# possess: a glitchy, wobbling digital buzz
ps = 1.6; base = tone(glide(90, 60, ps) * (1 + 0.3 * np.sign(np.sin(2 * np.pi * 7 * t(ps)))), ps, 'saw')
gl = crush(base, 8, 9) * (0.6 + 0.4 * (rng.uniform(0, 1, len(base)) > 0.97).astype(float).cumsum() % 2)
save('possess.wav', lowpass(gl, 3500) * env(len(gl), 0.05, 0.9), 0.5)

# sitting down on a window: a soft "plop"
plop = tone(glide(520, 260, 0.1), 0.1) * env(int(SR * 0.1), 0.001, 0.035)
save('sit.wav', mix((0, plop), (0.06, 0.4 * chime(1046, 0.2, 0.05))), 0.55)

# peeking out: a sneaky "boop-bip"
save('peek.wav', mix((0, tone(392, 0.09, 'tri') * env(int(SR * 0.09), 0.003, 0.05)), (0.11, tone(784, 0.08, 'tri') * env(int(SR * 0.08), 0.003, 0.04))), 0.5)

# cursor glitch trail starting: a rising zap
zap = tone(glide(200, 2400, 0.35, 2.0), 0.35, 'saw') * env(int(SR * 0.35), 0.005, 0.2)
save('zap.wav', lowpass(crush(zap, 10, 3), 5000), 0.45)

# something unlocked: a sparkly arpeggio
notes = [1046, 1318, 1568, 2093, 2637]
save('unlock.wav', mix(*[(i * 0.07, chime(f, 0.6, 0.2)) for i, f in enumerate(notes)]), 0.6)
