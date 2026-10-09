"""Synthesizes the cinematic score for the Zenith launch trailer (generated from scratch).

Run: python3 scripts/make_trailer_audio.py  (writes public/trailer_music.wav)
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



# ---------------------------------------------------------------- trailer score
# Timeline (seconds) — keep in sync with src/trailer/tt.ts
DUR = 20.0
T_TICKS = 1.0
T_CRACK = 3.25
T_ICON = 6.0
T_MONTAGE = 6.9
T_FILL = 13.9
T_SILENCE = 14.3
T_HERO = 14.5
T_DATE = 17.6
BEAT = 0.5

N = int(DUR * SR)
drone = np.zeros(N)
fx = np.zeros(N)      # braams, hits, risers (mono, gets reverb)
drums = np.zeros(N)
bass = np.zeros(N)
pad = np.zeros(N)
bells = np.zeros(N)


def saw(freq, t, phase=0.0):
    return 2 * ((t * freq + phase) % 1) - 1


def braam(dur=3.2, notes=(26, 33, 38), bright=2400, amp=1.0):
    """Big trailer 'braam': stacked detuned saws, filter snaps open then closes."""
    t = t_arr(dur)
    sig = np.zeros(len(t))
    for n in notes:
        for det in (-0.15, 0.0, 0.13):
            sig += saw(midi(n) * 2 ** (det / 12), t, rng.random())
    sig /= len(notes) * 3
    env_b = np.exp(-t * 2.4)
    out = lp(sig, bright, 3) * env_b + lp(sig, 260, 2) * (1 - env_b)
    amp_env = np.minimum(1, t / 0.015) * np.exp(-t * 0.95)
    return np.tanh(out * amp_env * 3.2) * 0.55 * amp


def sub_drop(dur=2.2, f0=70, f1=28):
    t = t_arr(dur)
    f = f1 + (f0 - f1) * np.exp(-t * 5)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 1.6)


def tick(amp=1.0):
    t = t_arr(0.05)
    return (np.sin(2 * np.pi * 3200 * t) * 0.6 + hp(rng.standard_normal(len(t)), 5000) * 0.4) * np.exp(-t * 160) * amp


def taiko(pitch=1.0, amp=1.0):
    t = t_arr(0.7)
    f = 52 * pitch + 60 * pitch * np.exp(-t * 18)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 6)
    skin = lp(rng.standard_normal(len(t)), 1800) * np.exp(-t * 40) * 0.5
    return np.tanh((body + skin) * 1.6) * amp


def clap(amp=1.0):
    t = t_arr(0.3)
    n = bp(rng.standard_normal(len(t)), 900, 5000)
    env = np.zeros(len(t))
    for d in (0.0, 0.012, 0.024):
        env += np.exp(-np.clip(t - d, 0, None) * 45) * (t >= d)
    return n * env * 0.35 * amp


def hat(amp=1.0):
    t = t_arr(0.05)
    return hp(rng.standard_normal(len(t)), 8000) * np.exp(-t * 80) * amp


def crash(dur=3.0):
    t = t_arr(dur)
    return hp(rng.standard_normal(len(t)), 4000) * np.exp(-t * 1.6) * 0.5


def riser(dur=2.0):
    t = t_arr(dur)
    nz = hp(rng.standard_normal(len(t)), 1200) * (t / dur) ** 2.4
    f = 160 * 2 ** (3 * t / dur)
    tone = np.sin(2 * np.pi * np.cumsum(f) / SR) * (t / dur) ** 2 * 0.35
    return nz * 0.45 + tone


def swell(dur=1.2):
    """Reverse-cymbal style swell into a hit."""
    t = t_arr(dur)
    return hp(rng.standard_normal(len(t)), 2500) * (t / dur) ** 3 * 0.6


def pad_note(freq, dur, bright=1600):
    t = t_arr(dur)
    sig = np.zeros(len(t))
    for det in (-0.1, 0.0, 0.09):
        sig += saw(freq * 2 ** (det / 12), t, rng.random())
    sig = lp(sig / 3, bright)
    return sig * adsr(len(t), 0.8, 0.4, 0.85, 1.0, dur - 1.0)


def bell(freq, dur=2.4, amp=1.0):
    t = t_arr(dur)
    mod = np.sin(2 * np.pi * freq * 3.5 * t) * 2.0 * np.exp(-6 * t)
    return np.sin(2 * np.pi * freq * t + mod) * np.exp(-2.6 * t) * amp


tt = t_arr(DUR)

# --- intro: low drone + a clock ticking ("hasn't changed in years")
d = np.sin(2 * np.pi * midi(26) * tt) * 0.5 + lp(saw(midi(38), tt), 220) * 0.35
d *= np.clip(tt / 1.2, 0, 1) * (0.55 + 0.45 * np.clip(tt / T_CRACK, 0, 1))
drone += d * (tt < T_CRACK)
wind = lp(hp(rng.standard_normal(N), 300), 1400) * np.clip(tt / T_CRACK, 0, 1) ** 2 * 0.12
drone += wind * (tt < T_CRACK)
k = 0
tm = T_TICKS
while tm < T_CRACK - 0.05:
    place(drums, tick(0.5 if k % 2 else 0.8), tm)
    tm += BEAT
    k += 1

# --- the crack
place(fx, braam(3.4, (26, 33, 38), 2600, 1.0), T_CRACK)
place(bass, sub_drop(2.4) * 0.9, T_CRACK)
place(fx, crash(1.2) * 0.28, T_CRACK)

# --- suspense: glassy high cluster + riser into the icon
for n in (74, 76, 81):
    t2 = t_arr(T_ICON - 3.6)
    place(pad, np.sin(2 * np.pi * midi(n) * t2) * (0.5 + 0.5 * np.sin(2 * np.pi * 5.5 * t2)) * np.clip(t2 / 0.8, 0, 1) * 0.12, 3.6)
place(fx, riser(T_ICON - 4.0) * 0.8, 4.0)
place(fx, swell(1.0) * 0.8, T_ICON - 1.0)

# --- icon locks: bright hit + bells
place(fx, braam(2.6, (26, 38, 45), 3200, 0.8), T_ICON)
place(bass, sub_drop(1.8, 80, 32) * 0.8, T_ICON)
for i, n in enumerate((74, 78, 81, 86)):
    place(bells, bell(midi(n), 2.4, 0.35), T_ICON + 0.04 + i * 0.07)
place(drums, taiko(1.0, 0.6), T_MONTAGE - 0.25)
place(drums, taiko(1.15, 0.7), T_MONTAGE - 0.125)

# --- montage: drums + pulsing bass on the cut grid (cuts every 1.0s)
ROOTS = [38, 34, 41, 36]  # D, Bb, F, C (one bar = 2s)
CHORDS = [[50, 53, 57, 62], [46, 50, 53, 58], [41, 45, 48, 53], [48, 52, 55, 60]]
tm = T_MONTAGE
beat_i = 0
while tm < T_SILENCE - 0.01:
    rel = tm - T_MONTAGE
    bar = int(rel // 2.0) % 4
    late = rel > 4.0
    if beat_i % 2 == 0:
        place(drums, taiko(1.0, 1.0), tm)
    else:
        place(drums, clap(1.0), tm)
        if late:
            place(drums, taiko(1.3, 0.55), tm + 0.25)
    for e in range(2):
        if rel > 1.0:
            place(drums, hat(0.25 if e else 0.4), tm + e * 0.25)
    for s in range(4):
        st = tm + s * 0.125
        if st >= T_SILENCE:
            break
        n = ROOTS[bar] - (12 if s % 2 == 0 else 0)
        t3 = t_arr(0.12)
        place(bass, np.tanh(2.2 * lp(saw(midi(n), t3), 500)) * np.exp(-t3 * 14) * 0.5, st)
    if beat_i % 4 == 0:
        for n in CHORDS[bar]:
            place(pad, pad_note(midi(n), min(2.2, T_SILENCE - tm), 1100) * 0.11, tm)
    tm += BEAT
    beat_i += 1
# drum fill into the silence
for i in range(8):
    place(drums, taiko(1.0 + i * 0.08, 0.6 + i * 0.05), T_FILL + i * 0.05)
place(fx, riser(T_SILENCE - 12.6) * 0.7, 12.6)

# --- hard silence, then the hero hit
cut = (tt >= T_SILENCE) & (tt < T_HERO)
for buf in (drone, fx, drums, bass, pad, bells):
    buf[cut] = 0
place(fx, braam(4.5, (26, 38, 42, 45), 3400, 1.1), T_HERO)
place(bass, sub_drop(3.0, 90, 30), T_HERO)
place(fx, crash(2.2) * 0.4, T_HERO)
for n in (50, 54, 57, 61, 64, 69):  # Dmaj9 resolve
    place(pad, pad_note(midi(n), DUR - T_HERO, 2200) * 0.12, T_HERO + 0.05)
for i, n in enumerate((74, 78, 81, 85, 86, 90, 93)):
    place(bells, bell(midi(n), 2.8, 0.22), T_HERO + 0.6 + i * 0.32)

# --- release date hit
place(fx, braam(2.5, (38, 45, 50), 3000, 0.55), T_DATE)
place(bass, sub_drop(2.0, 70, 32) * 0.7, T_DATE)
for i, n in enumerate((78, 81, 86)):
    place(bells, bell(midi(n), 2.6, 0.3), T_DATE + i * 0.05)

mix = stereo_verb(fx, 0.3) + stereo_verb(pad, 0.4) + stereo_verb(bells, 0.5) + stereo_verb(drums, 0.15)
mix += np.stack([bass, bass], 1) * 0.9 + stereo_verb(drone, 0.2)
fade = np.clip((DUR - tt) / 1.4, 0, 1)[:, None]
mix = np.tanh(mix * 1.25) * fade
mix[cut] = 0
write("trailer_music.wav", mix)
print("ok")
