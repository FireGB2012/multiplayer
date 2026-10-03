"""Synthesizes the background music and the whoosh/riser/impact SFX.

Everything here is generated from scratch, so there are no licensing concerns.
Run: python3 scripts/make_audio.py  (writes into public/)
"""

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt, fftconvolve

SR = 44100
rng = np.random.default_rng(7)
OUT = "public"


def t_arr(dur):
    return np.arange(int(dur * SR)) / SR


def lp(x, cutoff, order=2):
    return sosfilt(butter(order, cutoff, "low", fs=SR, output="sos"), x)


def hp(x, cutoff, order=2):
    return sosfilt(butter(order, cutoff, "high", fs=SR, output="sos"), x)


def bp(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], "band", fs=SR, output="sos"), x)


def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def adsr(n, a, d, s, r, sustain_len):
    a_n, d_n, r_n = int(a * SR), int(d * SR), int(r * SR)
    s_n = max(int(sustain_len * SR) - a_n - d_n, 0)
    env = np.concatenate(
        [
            np.linspace(0, 1, a_n, endpoint=False),
            np.linspace(1, s, d_n, endpoint=False),
            np.full(s_n, s),
            np.linspace(s, 0, r_n),
        ]
    )
    if len(env) < n:
        env = np.pad(env, (0, n - len(env)))
    return env[:n]


def reverb_ir(dur=2.8, decay=3.2, seed=0):
    r = np.random.default_rng(seed)
    t = t_arr(dur)
    ir = r.standard_normal(len(t)) * np.exp(-decay * t)
    ir = lp(ir, 6000)
    return ir / np.sqrt(np.sum(ir**2))


IR_L, IR_R = reverb_ir(seed=1), reverb_ir(seed=2)


def stereo_verb(mono, wet=0.3):
    l = fftconvolve(mono, IR_L)[: len(mono)]
    r = fftconvolve(mono, IR_R)[: len(mono)]
    return np.stack([mono * (1 - wet) + l * wet, mono * (1 - wet) + r * wet], 1)


def place(buf, sig, start):
    i = int(start * SR)
    if i >= len(buf):
        return
    end = min(len(buf), i + len(sig))
    buf[i:end] += sig[: end - i]


def write(name, stereo):
    peak = np.max(np.abs(stereo)) or 1
    stereo = stereo / peak * 0.89
    wavfile.write(f"{OUT}/{name}", SR, (stereo * 32767).astype(np.int16))


# ---------------------------------------------------------------- music

DUR = 30.0
BPM = 120
BEAT = 60 / BPM
BAR = BEAT * 4
DROP = 4.0  # beat kicks in on "This is Zenith"
BREAK = (20.75, 21.25)  # "Want more?" breath
OUTRO = 25.8  # drums out, big final chord

N = int(DUR * SR)
pad = np.zeros(N)
bells = np.zeros(N)
bass = np.zeros(N)
drums = np.zeros(N)
kick_times = []

# Fmaj9 -> Am7 -> Dm9 -> Bbmaj7(#11), 2 bars (4s) each
CHORDS = [
    [53, 57, 60, 64, 67],
    [57, 60, 64, 67, 71],
    [50, 57, 60, 64, 65],
    [46, 53, 57, 62, 64],
]
ROOTS = [41, 45, 38, 46]


def saw_pad_note(freq, dur):
    t = t_arr(dur)
    sig = np.zeros(len(t))
    for det in (-0.12, 0.0, 0.11):
        f = freq * 2 ** (det / 12)
        sig += 2 * ((t * f + rng.random()) % 1) - 1
    sig = lp(sig / 3, 1800)
    return sig * adsr(len(t), 0.9, 0.5, 0.8, 1.2, dur - 1.2)


def bell(freq, dur=1.6, amp=1.0):
    t = t_arr(dur)
    mod = np.sin(2 * np.pi * freq * 3.5 * t) * 2.2 * np.exp(-6 * t)
    sig = np.sin(2 * np.pi * freq * t + mod) * np.exp(-3.2 * t)
    return sig * amp


def kick():
    t = t_arr(0.5)
    f = 45 + 95 * np.exp(-22 * t)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-6.5 * t) + 0.15 * lp(rng.standard_normal(len(t)), 3000) * np.exp(-60 * t)


def snap():
    t = t_arr(0.35)
    noise = bp(rng.standard_normal(len(t)), 1200, 7000) * np.exp(-18 * t)
    tone = np.sin(2 * np.pi * 190 * t) * np.exp(-25 * t)
    return noise * 0.8 + tone * 0.4


def hat(open_=False):
    t = t_arr(0.25 if open_ else 0.06)
    return hp(rng.standard_normal(len(t)), 8000) * np.exp(-(14 if open_ else 70) * t)


def in_break(time):
    return BREAK[0] <= time < BREAK[1]


# pads: whole track, chord every 4s; final sustained chord at outro
time = 0.0
ci = 0
while time < OUTRO:
    dur = min(4.0, OUTRO - time) + 1.2
    for n in CHORDS[ci % 4]:
        place(pad, saw_pad_note(midi(n), dur) * 0.18, time)
    time += 4.0
    ci += 1
for n in [41, 53, 57, 60, 64, 67, 72]:  # big Fmaj9 resolve
    place(pad, saw_pad_note(midi(n), DUR - OUTRO) * 0.2, OUTRO)

# intro shimmer bells (sparse), then arpeggio from the drop
for i, n in enumerate([72, 76, 79, 84]):
    place(bells, bell(midi(n), 2.5, 0.35), 0.5 + i * 0.85)

step = BEAT / 2
time = DROP
pattern = [0, 2, 4, 3, 1, 4, 2, 3]
k = 0
while time < OUTRO - 0.01:
    if not in_break(time):
        chord = CHORDS[int((time) // 4.0) % 4]
        n = chord[pattern[k % 8] % len(chord)] + 12
        place(bells, bell(midi(n), 1.4, 0.32 if k % 2 == 0 else 0.22), time)
    time += step
    k += 1
# final bell flourish on the logo
for i, n in enumerate([77, 81, 84, 88, 91]):
    place(bells, bell(midi(n), 3.0, 0.4), OUTRO + 0.12 + i * 0.09)

# drums + bass, half-time feel
bar_start = DROP
while bar_start < OUTRO - 0.01:
    for beat_off, kind in [(0, "k"), (1.5, "k"), (2, "s"), (3.75, "k")]:
        tm = bar_start + beat_off * BEAT
        if tm >= OUTRO or in_break(tm):
            continue
        if kind == "k":
            place(drums, kick() * 0.9, tm)
            kick_times.append(tm)
        else:
            place(drums, snap() * 0.45, tm)
    for e in range(8):
        tm = bar_start + e * step
        if tm >= OUTRO or in_break(tm):
            continue
        place(drums, hat(open_=(e % 4 == 3)) * (0.16 if e % 2 else 0.1), tm)
    # bass follows the chord root
    root = ROOTS[int(bar_start // 4.0) % 4]
    for beat_off, length in [(0, 1.4), (1.5, 0.45), (2.5, 1.2)]:
        tm = bar_start + beat_off * BEAT
        if tm >= OUTRO or in_break(tm):
            continue
        tt = t_arr(length * BEAT)
        b = np.tanh(1.6 * np.sin(2 * np.pi * midi(root - 12) * tt)) * adsr(len(tt), 0.01, 0.1, 0.8, 0.08, len(tt) / SR - 0.08)
        place(bass, b * 0.5, tm)
    bar_start += BAR

# final low boom under the logo
tt = t_arr(3.5)
place(bass, np.sin(2 * np.pi * midi(29) * tt) * np.exp(-1.2 * tt) * 0.6, OUTRO)

# sidechain pump from the kicks
duck = np.ones(N)
for kt in kick_times:
    i = int(kt * SR)
    L = int(0.32 * SR)
    seg = 1 - 0.38 * np.exp(-np.arange(L) / SR * 11)
    duck[i : i + L] = np.minimum(duck[i : i + L], seg[: len(duck[i : i + L])])

# intro filter sweep: pads open up toward the drop
open_curve = np.clip(t_arr(DUR) / DROP, 0, 1)
pad_lo = lp(pad, 500)
pad = pad_lo * (1 - open_curve) + pad * open_curve

mix = stereo_verb(pad * duck, 0.35) + stereo_verb(bells, 0.45)
mix += np.stack([bass * duck, bass * duck], 1) + stereo_verb(drums, 0.12)
# gentle fade at the very end
fade = np.clip((DUR - t_arr(DUR)) / 1.5, 0, 1)[:, None]
mix = np.tanh(mix * 1.2) * fade
write("music.wav", mix)

# ---------------------------------------------------------------- sfx


def whoosh(dur=0.7, lo=300, hi=4000, rising=True):
    t = t_arr(dur)
    n = rng.standard_normal(len(t))
    # sweep a band-pass by mixing a stack of fixed bands with moving weights
    out = np.zeros(len(t))
    bands = np.geomspace(lo, hi, 10)
    pos = t / dur if rising else 1 - t / dur
    for i, f in enumerate(bands):
        w = np.exp(-((pos * 9 - i) ** 2) / 2.5)
        out += bp(n, f * 0.8, min(f * 1.25, SR / 2 - 100)) * w
    env = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 1.6
    return stereo_verb(out * env, 0.25)


def riser(dur=3.4):
    t = t_arr(dur)
    n = hp(rng.standard_normal(len(t)), 1500) * (t / dur) ** 2.2
    f = 220 * 2 ** (2.5 * t / dur)
    tone = np.sin(2 * np.pi * np.cumsum(f) / SR) * (t / dur) ** 2 * 0.3
    return stereo_verb(n * 0.5 + tone, 0.4)


def impact(dur=2.5):
    t = t_arr(dur)
    f = 38 + 80 * np.exp(-9 * t)
    boom = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-2.2 * t)
    crack = lp(rng.standard_normal(len(t)), 5000) * np.exp(-14 * t) * 0.35
    return stereo_verb(boom + crack, 0.35)


def shimmer(dur=1.8):
    t = t_arr(dur)
    out = np.zeros(len(t))
    for _ in range(26):
        f = rng.uniform(2500, 9000)
        st = rng.uniform(0, dur * 0.55)
        tt = np.clip(t - st, 0, None)
        out += np.sin(2 * np.pi * f * tt) * np.exp(-9 * tt) * (t >= st) * rng.uniform(0.2, 0.6)
    return stereo_verb(out * 0.3, 0.55)


write("sfx/whoosh.wav", whoosh(0.7))
write("sfx/whoosh-short.wav", whoosh(0.38, 600, 6000))
write("sfx/whoosh-down.wav", whoosh(0.55, 250, 3000, rising=False))
write("sfx/riser.wav", riser())
write("sfx/impact.wav", impact())
write("sfx/shimmer.wav", shimmer())
print("ok")
