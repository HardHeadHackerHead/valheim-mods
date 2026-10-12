"""
Builds the real gun sounds that Quad's Guns ships (mods/Guns/sounds/*.wav) from free (CC0) recordings.

Sources (all CC0 / public domain, downloaded from OpenGameArt; see mods/Guns/sounds/LICENSES.txt):
  - The Free Firearm Sound Library (Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney): real gunshots, 96 kHz stereo.
  - Gun Reload Sounds (SpringySpringo): a pistol reload, an assault rifle reload, a shotgun pump.
  - Gun Reload Sound Effects and Metal Impact Sounds (BMacZero): a clip loaded, one round, metal clinks.
  - Equipment Clicks III (LFA): a bolt action rifle cocked, a stapler and a tape measure snapped: a bank of real mechanical clicks.

Usage:  python build.py <folder with the downloads>
        (<folder>/lib = the unpacked "Prepared SFX Library", <folder>/extra = the single files, <folder>/*.wav = the BMacZero reload files)
Each output is a mono 16-bit 44.1 kHz WAV, trimmed to the sound, with a short fade at the end.
"""
import os, sys, glob, struct
import numpy as np
from scipy.io import wavfile
from scipy.signal import resample_poly

SRC = sys.argv[1] if len(sys.argv) > 1 else '.'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'mods', 'Guns', 'sounds')
RATE = 44100
os.makedirs(OUT, exist_ok=True)


def read(path):
    """Any PCM wav (16/24/32-bit, any rate, stereo) as mono floats in -1..1, and its rate."""
    r, d = wavfile.read(path)
    d = d.astype(np.float64)
    if d.dtype.kind == 'i' or True:
        peak = {np.dtype('int16'): 32768.0}.get(np.dtype('int16'), 32768.0)
    raw = wavfile.read(path)[1]
    scale = {np.dtype('int16'): 32768.0, np.dtype('int32'): 2147483648.0, np.dtype('uint8'): 128.0}.get(raw.dtype, 1.0)
    d = raw.astype(np.float64) / scale
    if raw.dtype == np.dtype('uint8'):
        d -= 1.0
    if d.ndim > 1:
        d = d.mean(axis=1)
    return d, r


def to_rate(x, r):
    if r == RATE:
        return x
    from math import gcd
    g = gcd(r, RATE)
    return resample_poly(x, RATE // g, r // g)


def repitch(x, f):
    """Plays x f times as fast (higher and shorter for f > 1)."""
    if abs(f - 1.0) < 0.001:
        return x
    return resample_poly(x, int(round(100 / f)), 100)


def trim(x, length, pre=0.008, threshold=0.04):
    """From just before the first loud sample, length seconds, faded out over the last fifth."""
    pk = np.abs(x).max()
    hit = np.argmax(np.abs(x) > threshold * pk)
    start = max(0, hit - int(pre * RATE))
    y = x[start:start + int(length * RATE)].copy()
    n = len(y)
    fade = max(1, n // 5)
    y[-fade:] *= np.linspace(1, 0, fade) ** 2
    return y


def save(name, x, peak=0.9, gain=1.0):
    x = x / (np.abs(x).max() + 1e-9) * peak * gain
    wavfile.write(os.path.join(OUT, name), RATE, (np.clip(x, -1, 1) * 32767).astype(np.int16))
    print(f'{name:34} {len(x) / RATE:5.2f}s')


def lib(gun, name):
    d, r = read(os.path.join(SRC, 'lib', 'Prepared SFX Library', gun, name))
    return to_rate(d, r)


# ---- the shots ----------------------------------------------------------------------------------------------
# prefab: [(gun folder, file, seconds, pitch, gain), ...]  (A "near" recording is close to the muzzle, a "mid" one has more of the place in it)
SHOTS = {
    'dh_pistol':      [('1911', 'A_42P.wav', 1.0, 1.0, 0.85), ('Walther PPQ', 'X_39P.wav', 1.0, 1.0, 0.85), ('1911', 'A_34P.wav', 1.0, 1.0, 0.8)],
    'dh_revolver':    [('Smith & Wesson 642', 'V_27P.wav', 1.3, 0.92, 1.0), ('Smith & Wesson 642', 'V_22P.wav', 1.3, 0.92, 1.0)],
    'dh_dualpistols': [('Bersa', 'F_47P.wav', 0.9, 1.0, 0.8), ('Bersa', 'F_41P.wav', 0.9, 1.0, 0.8), ('Walther PPQ', 'X_31P.wav', 0.9, 1.05, 0.8)],
    'dh_smg':         [('Carl Gustav M45', 'G_31P.wav', 0.6, 1.0, 0.8), ('Carl Gustav M45', 'G_20P.wav', 0.6, 1.0, 0.8), ('PPSh', 'P_16P.wav', 0.6, 1.0, 0.8)],
    'dh_shotgun':     [('Model 12', 'K_22P.wav', 1.6, 1.0, 1.0), ('CD', 'H_21P.wav', 1.6, 1.0, 1.0), ('Model 12', 'K_17P.wav', 1.6, 1.0, 1.0)],
    'dh_rifle':       [('Tikka', 'W_29P.wav', 1.7, 1.0, 1.0), ('Tikka', 'W_24P.wav', 1.7, 1.0, 1.0)],
    'dh_leveraction': [('Marlin 336', 'I_22P.wav', 1.5, 1.0, 1.0), ('Model 1894', 'L_23P.wav', 1.5, 1.0, 1.0)],
    'dh_ar15':        [('AR-15', 'D_32P.wav', 1.0, 1.0, 0.95), ('AR-15', 'D_24P.wav', 1.0, 1.0, 0.95)],
    'dh_sniper':      [('1917', 'B_24P.wav', 2.2, 0.88, 1.0), ('Arisaka', 'E_25P.wav', 2.2, 0.88, 1.0)],
    'dh_machinegun':  [('AK-47', 'C_28P.wav', 0.7, 0.95, 0.9), ('AK-47', 'C_31P.wav', 0.7, 0.95, 0.9)],
    'dh_minigun':     [('AR-15', 'D_24P.wav', 0.35, 1.12, 0.85), ('AR-15', 'D_32P.wav', 0.35, 1.12, 0.85)],
}
for gun, files in SHOTS.items():
    for i, (folder, name, seconds, pitch, gain) in enumerate(files):
        save(f'shot_{gun}_{i}.wav', trim(repitch(lib(folder, name), pitch), seconds), gain=gain)

# ---- single files ---------------------------------------------------------------------------------------------
def single(path):
    d, r = read(os.path.join(SRC, path))
    return to_rate(d, r)


def clip(name, x, **kw):
    save(name, x, **kw)


# a pistol and a rifle reload, whole (the magazine out, the slide at the end)
clip('reload_pistol.wav', trim(single('extra/gunreload1.wav'), 1.6, pre=0.05), peak=0.8)
clip('reload_rifle.wav', trim(single('extra/assaultriflereload1_0.wav'), 1.6, pre=0.05), peak=0.8)
# the shotgun pump
clip('click_Pump_0.wav', trim(single('extra/shotguncock_0.wav'), 0.5, pre=0.02), peak=0.8)
clip('click_Pump_1.wav', repitch(trim(single('extra/shotguncock_0.wav'), 0.5, pre=0.02), 0.94), peak=0.8)
# a round going in, and a magazine
for i, f in enumerate((1.0, 0.92, 1.08)):
    clip(f'click_Shell_{i}.wav', repitch(trim(single('singlebullet1.wav'), 0.3, pre=0.1, threshold=0.2), f), peak=0.7)
clip('click_MagIn_0.wav', trim(single('clipload1.wav'), 0.25, pre=0.08, threshold=0.2), peak=0.8)
clip('click_MagIn_1.wav', trim(single('clipload2.wav'), 0.21, pre=0.03, threshold=0.2), peak=0.8)
# brass bouncing: a clink, then a smaller, higher one a moment later
for i, name in enumerate(('clink1_0.wav', 'clink2.wav', 'clink3.wav')):
    c = trim(single('extra/' + name), 0.3, pre=0.01)
    c = np.concatenate([c, np.zeros(int(0.35 * RATE))])
    b = repitch(c[:int(0.25 * RATE)], 1.18) * 0.45
    at = int((0.09 + 0.03 * i) * RATE)
    c[at:at + len(b)] += b[:len(c) - at]
    clip(f'click_Casing_{i}.wav', c[:int(0.45 * RATE)], peak=0.6)

# ---- the bank of real clicks (bolt, stapler, tape measure) ---------------------------------------------------
d = single('extra/equipment_clicks3.wav')
env = np.convolve(np.abs(d), np.ones(int(0.004 * RATE)) / int(0.004 * RATE), mode='same')
thr = env.max() * 0.12
events, i, quiet = [], 0, int(0.12 * RATE)
while i < len(d):
    if env[i] > thr:
        j = min(len(d), i + int(0.3 * RATE))
        seg = d[max(0, i - int(0.004 * RATE)):j]
        spec = np.abs(np.fft.rfft(seg * np.hanning(len(seg))))
        freqs = np.fft.rfftfreq(len(seg), 1 / RATE)
        centroid = float((spec * freqs).sum() / (spec.sum() + 1e-9))
        events.append((i / RATE, float(np.abs(seg).max()), float(np.sqrt((seg ** 2).mean())), centroid, seg))
        i = j + quiet
    else:
        i += 1
print(len(events), 'real clicks found')
loud = sorted(events, key=lambda e: -e[2])           # the heaviest first: the bolt and the big snaps
light = sorted(events, key=lambda e: e[3], reverse=True)   # the brightest first: the stapler and the tape
for k, e in enumerate(events):
    print(f'  click {k:2}: at {e[0]:5.2f}s peak {e[1]:.2f} rms {e[2]:.3f} centroid {e[3]:5.0f} Hz')


def seg_of(e, length=0.22):
    s = e[4][:int(length * RATE)].copy()
    n = len(s)
    fade = max(1, n // 4)
    s[-fade:] *= np.linspace(1, 0, fade) ** 2
    return s


def lay(parts, length):
    """parts: [(seconds, event, gain, pitch)] laid into one clip."""
    out = np.zeros(int(length * RATE))
    for at, e, g, p in parts:
        s = repitch(seg_of(e), p) * g
        a = int(at * RATE)
        out[a:a + len(s)] += s[:len(out) - a]
    return out


H = loud[:12]
Lt = light[:8]
# the mechanisms, each made of real clicks laid out as it would sound (pitch varies the repeats)
mech = {
    'Dry':    [[(0.0, Lt[0], 1.0, 1.0)], [(0.0, Lt[1], 1.0, 1.0)]],
    'Rack':   [[(0.0, H[0], 0.9, 1.0), (0.17, H[3], 1.0, 0.95)], [(0.0, H[1], 0.9, 1.0), (0.18, H[4], 1.0, 0.95)]],
    'Bolt':   [[(0.0, H[2], 0.8, 1.0), (0.14, H[5], 0.9, 0.95), (0.34, H[6], 0.9, 1.0), (0.46, H[7], 1.0, 0.9)],
               [(0.0, H[8], 0.8, 1.0), (0.14, H[9], 0.9, 0.95), (0.34, H[10], 0.9, 1.0), (0.46, H[11], 1.0, 0.9)]],
    'Lever':  [[(0.0, H[4], 0.9, 1.05), (0.20, H[1], 1.0, 0.95), (0.34, H[6], 0.9, 1.0)], [(0.0, H[5], 0.9, 1.05), (0.20, H[2], 1.0, 0.95), (0.34, H[7], 0.9, 1.0)]],
    'MagOut': [[(0.0, Lt[2], 0.8, 1.0), (0.10, H[8], 0.8, 1.0)], [(0.0, Lt[3], 0.8, 1.0), (0.10, H[9], 0.8, 1.0)]],
    'Draw':   [[(0.0, Lt[4], 0.6, 0.9), (0.07, H[10], 0.5, 1.1)], [(0.0, Lt[5], 0.6, 0.9), (0.07, H[11], 0.5, 1.1)]],
}
lengths = {'Dry': 0.2, 'Rack': 0.45, 'Bolt': 0.75, 'Lever': 0.62, 'MagOut': 0.4, 'Draw': 0.3}
for kind, variants in mech.items():
    for v, parts in enumerate(variants):
        save(f'click_{kind}_{v}.wav', lay(parts, lengths[kind]), peak=0.8)
print('done ->', os.path.abspath(OUT))


# ---- sustained fire: a real burst, looped, with a tail when it stops (the machine gun and the minigun) ---------
def onsets_of(d, r, refractory):
    env = np.abs(d)
    k = max(1, int(0.002 * r))
    env = np.convolve(env, np.ones(k) / k, mode='same')
    thr = env.max() * 0.25
    out, i = [], 0
    while i < len(env):
        if env[i] > thr:
            out.append(i)
            i += int(refractory * r)
        else:
            i += 1
    return out


def burst(gun_dir, name, out_name, refractory, want_intervals, tail_seconds=1.0):
    """The longest steady run of shots in a recorded burst: whole shots long, joined into a loop; and what follows the last shot."""
    path = os.path.join(SRC, 'lib', 'Prepared SFX Library', gun_dir, name)
    r0, raw = wavfile.read(path)
    d = (raw.astype(np.float64) / 2147483648.0)
    d = d.mean(axis=1) if d.ndim > 1 else d
    ons = onsets_of(d, r0, refractory)
    iv = np.diff(ons)
    med = np.median(iv)
    best, cur = (0, 0), 0
    for i, v in enumerate(iv):                      # runs of intervals within a fifth of the usual
        if abs(v - med) < 0.2 * med:
            cur += 1
            if cur > best[0]:
                best = (cur, i - cur + 1)
        else:
            cur = 0
    run_len, run_at = best
    n = min(run_len - 2, want_intervals)
    a = run_at + 1                                  # (not the first shot of the run: its attack is the start of the burst)
    pre = int(0.006 * r0)
    start, end = ons[a] - pre, ons[a + n] - pre
    xf = int(0.004 * r0)
    seg = d[start:end + xf].copy()
    body = seg[:end - start].copy()
    w = np.linspace(0, 1, xf)
    body[:xf] = body[:xf] * w + seg[end - start:end - start + xf] * (1 - w)
    last = ons[a + run_len - 1] if a + run_len - 1 < len(ons) else ons[-1]
    tail = d[last - pre:last - pre + int(tail_seconds * r0)].copy()
    f = len(tail) // 3
    tail[-f:] *= np.linspace(1, 0, f) ** 2
    rate = n / ((end - start) / r0)
    save(f'loop_{out_name}.wav', to_rate(body, r0), peak=0.9)
    save(f'tail_{out_name}.wav', to_rate(tail, r0), peak=0.9)
    print(f'  {out_name}: {n} shots in the loop, recorded at {rate:.1f} rounds a second (run of {run_len})')


burst('AK-47', 'C_27P.wav', 'dh_machinegun', 0.04, 10)
burst('PPSh', 'P_34P.wav', 'dh_minigun', 0.02, 10, 0.8)

# ---- where a round lands: a thud with a bright tick, three versions ------------------------------------------
thuds = [single('extra/thud3.wav'), single('extra/thud2.wav'), single('extra/thud3.wav')]
clinks = [single('extra/clink1_0.wav'), single('extra/clink2.wav'), single('extra/clink3.wav')]
for i in range(3):
    t = trim(repitch(thuds[i], (1.15, 1.0, 0.9)[i]), 0.16, pre=0.004, threshold=0.1)
    c = trim(repitch(clinks[i], 1.4), 0.12, pre=0.004, threshold=0.1) * 0.35
    out = t.copy()
    out[:len(c)] += c[:len(out)]
    save(f'impact_{i}.wav', out, peak=0.8)
print('impacts done')
