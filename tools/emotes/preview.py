#!/usr/bin/env python3
"""Draws stick figures from Emotes.bin (the same way the game rebuilds a pose) to check the clips by eye.

Usage: python3 preview.py out_folder [--gif] [--check] [clip names...]
  (default)  a PNG per clip: 8 frames, facing you on top, from the side below
  --gif      an animated GIF per clip instead, at the real speed (facing you, from the front-side, from the side)
  --check    prints, per clip: how fast a planted foot slides, how far a foot sinks into the floor, and how big
             the jump is from the last frame back to the first (a loop that doesn't join up hitches every time)"""
import math, os, struct, sys, zlib
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SEG = ["armUpL", "armLowL", "armUpR", "armLowR", "legUpL", "legLowL", "legUpR", "legLowR", "head"]
# same proportions as dances.py, in leg lengths
HIP_W, THIGH, SHIN, SHOULDER_UP, SHOULDER_W, UPPER_ARM, FOREARM, NECK, HEAD = 0.10, 0.5, 0.5, 0.62, 0.22, 0.36, 0.34, 0.72, 0.14


def load():
    raw = zlib.decompress(open(os.path.join(ROOT, "src", "SubnauticaMP.Plugin", "Emotes.bin"), "rb").read())
    assert raw[:4] == b"SNEM"
    ver, count = struct.unpack_from("<BH", raw, 4); i = 7
    clips = {}
    for _ in range(count):
        n = raw[i]; name = raw[i + 1:i + 1 + n].decode(); i += 1 + n
        fps, frames, loops = struct.unpack_from("<BHB", raw, i); i += 4
        fr = []
        for _ in range(frames):
            q = np.array(struct.unpack_from("<4b", raw, i)) / 127; i += 4
            off = np.array(struct.unpack_from("<3h", raw, i)) / 1000; i += 6
            d = [np.array(struct.unpack_from("<3b", raw, i + 3 * k)) / 127 for k in range(9)]; i += 27
            fr.append((q / np.linalg.norm(q), off, [v / np.linalg.norm(v) for v in d]))
        clips[name] = (fps, loops, fr)
    return clips


def mix(a, b, u):
    """Between two frames (u = 0..1)."""
    qb = b[0] if np.dot(a[0], b[0]) >= 0 else -b[0]
    q = a[0] * (1 - u) + qb * u
    dirs = [x * (1 - u) + y * u for x, y in zip(a[2], b[2])]
    return q / np.linalg.norm(q), a[1] * (1 - u) + b[1] * u, [d / np.linalg.norm(d) for d in dirs]


def qrot(q, v):
    x, y, z, w = q; u = np.array([x, y, z])
    return 2 * np.dot(u, v) * u + (w * w - np.dot(u, u)) * v + 2 * w * np.cross(u, v)


def joints(frame):
    """Joint positions of the frame, in leg lengths, the floor at y = 0."""
    q, off, d = frame
    hips = np.array([0, 1.0, 0]) + off
    up, right = qrot(q, np.array([0, 1.0, 0])), qrot(q, np.array([1.0, 0, 0]))
    j = {"hips": hips, "chest": hips + up * SHOULDER_UP, "neck": hips + up * NECK}
    for side, s in (("L", -1), ("R", 1)):
        j["shoulder" + side] = sh = j["chest"] + right * SHOULDER_W * s
        j["elbow" + side] = el = sh + d[SEG.index("armUp" + side)] * UPPER_ARM
        j["hand" + side] = el + d[SEG.index("armLow" + side)] * FOREARM
        j["hip" + side] = hp = hips + right * HIP_W * s
        j["knee" + side] = kn = hp + d[SEG.index("legUp" + side)] * THIGH
        j["foot" + side] = kn + d[SEG.index("legLow" + side)] * SHIN
    j["head"] = j["neck"] + d[8] * HEAD
    return j


LINES = [("hips", "chest"), ("chest", "neck"), ("neck", "head")]
for _s in "LR":
    LINES += [("chest", "shoulder" + _s), ("shoulder" + _s, "elbow" + _s), ("elbow" + _s, "hand" + _s),
              ("hips", "hip" + _s), ("hip" + _s, "knee" + _s), ("knee" + _s, "foot" + _s)]


def project(v, view):
    if view == "front": return -v[0], v[1]  # we look at their face
    if view == "side": return v[2], v[1]    # from their left: forward = right
    a = math.radians(40)                     # "3/4": from in front and off to one side, a little from above
    x, z = v[0] * math.cos(a) + v[2] * math.sin(a), -v[0] * math.sin(a) + v[2] * math.cos(a)
    return -x, v[1] - 0.18 * z


def draw(ax, frame, view, color):
    j = joints(frame)
    def p(v): return project(v, view)
    ax.plot([-1.2, 1.2], [0, 0], "-", color="0.75", lw=1)
    for a, b in LINES:
        (x0, y0), (x1, y1) = p(j[a]), p(j[b])
        ax.plot([x0, x1], [y0, y1], "-", color="tab:gray" if b.endswith("L") else color, lw=2.5)  # left side grey
    hx, hy = p(j["head"])
    ax.add_patch(plt.Circle((hx, hy), 0.11, fill=False, color=color, lw=2))
    for h in ("handL", "handR"):
        x, y = p(j[h]); ax.plot([x], [y], "o", color="black", ms=3)
    ax.set_xlim(-1.2, 1.2); ax.set_ylim(-0.25, 2.45); ax.set_aspect("equal"); ax.axis("off")


def check(name, fps, loops, fr):
    feet = {s: np.array([joints(f)["foot" + s] for f in fr]) for s in "LR"}
    slide, sink = 0.0, 0.0
    for s, p in feet.items():
        sink = max(sink, -p[:, 1].min())
        n = len(p) if loops else len(p) - 1
        for i in range(n):
            a, b = p[i], p[(i + 1) % len(p)]
            if a[1] < 0.02 and b[1] < 0.02:  # on the floor both frames: shouldn't move
                slide = max(slide, float(np.linalg.norm((b - a)[[0, 2]])) * fps)
    def vec(f): return np.concatenate([f[0], f[1]] + list(f[2]))
    def step(a, b): return float(np.linalg.norm(vec(fr[b]) - vec(fr[a])))
    # the jump from the last frame to the first, against the frames either side of it (about 1 = joins up)
    seam = step(-1, 0) / max(1e-6, (step(-2, -1) + step(0, 1)) / 2) if loops else 0
    print(f"{name:14s} {len(fr) / fps:4.1f}s  foot slide {slide:4.2f} legs/s  sinks {sink:4.2f}  "
          f"loop seam {seam:4.1f}x the frames next to it")


def main():
    args = sys.argv[1:]
    gif, chk = "--gif" in args, "--check" in args
    args = [a for a in args if not a.startswith("--")]
    out = args[0]; os.makedirs(out, exist_ok=True)
    clips = load(); names = args[1:] or list(clips)
    for name in names:
        fps, loops, fr = clips[name]
        if chk: check(name, fps, loops, fr); continue
        if gif:
            from matplotlib.animation import FuncAnimation, PillowWriter
            rate = 25  # a GIF counts in hundredths of a second, so 30 fps would play too fast
            fig, axes = plt.subplots(1, 3, figsize=(7.2, 3.0))
            fig.subplots_adjust(left=0, right=1, bottom=0, top=0.92, wspace=0)
            def show(i):
                at = i * fps / rate
                a = int(at) % len(fr)
                b = (a + 1) % len(fr) if loops else min(a + 1, len(fr) - 1)
                f = mix(fr[a], fr[b], at - int(at))
                for ax, view, c in zip(axes, ("front", "3/4", "side"), ("tab:orange", "tab:green", "tab:blue")):
                    ax.clear(); draw(ax, f, view, c)
                fig.suptitle(f"{name}  {i / rate:4.2f}s", fontsize=9)
            FuncAnimation(fig, show, frames=int(round(len(fr) / fps * rate))).save(
                os.path.join(out, name + ".gif"), writer=PillowWriter(fps=rate), dpi=80)
            plt.close(fig)
            continue
        k = 8; fig, axes = plt.subplots(2, k, figsize=(k * 1.6, 4.4))
        for j in range(k):
            f = fr[int(j * len(fr) / k)]
            draw(axes[0][j], f, "front", "tab:orange"); draw(axes[1][j], f, "side", "tab:blue")
        fig.suptitle(f"{name} ({len(fr) / fps:.1f}s{' loop' if loops else ''})  top: facing you, bottom: side (forward = right)")
        fig.savefig(os.path.join(out, name + ".png"), dpi=60); plt.close(fig)


if __name__ == "__main__":
    main()
