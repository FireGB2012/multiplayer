#!/usr/bin/env python3
"""Draws stick figures from Emotes.bin (the same way the game rebuilds a pose) to check the clips by eye.
Usage: python3 preview.py out_folder [clip names...]"""
import math, os, struct, sys, zlib
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SEG = ["armUpL", "armLowL", "armUpR", "armLowR", "legUpL", "legLowL", "legUpR", "legLowR", "head"]


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
            fr.append((q / np.linalg.norm(q), off, d))
        clips[name] = (fps, loops, fr)
    return clips


def qrot(q, v):
    x, y, z, w = q; u = np.array([x, y, z])
    return 2 * np.dot(u, v) * u + (w * w - np.dot(u, u)) * v + 2 * w * np.cross(u, v)


def pose(frame, leg=0.9):
    q, off, d = frame
    hips = np.array([0, leg, 0]) + off * leg
    up, right = qrot(q, np.array([0, 1.0, 0])), qrot(q, np.array([1.0, 0, 0]))
    chest = hips + up * 0.5
    lines = [(hips, chest)]
    for side, s in (("L", -1), ("R", 1)):
        sh = chest + right * 0.18 * s; el = sh + d[SEG.index("armUp" + side)] * 0.3; ha = el + d[SEG.index("armLow" + side)] * 0.28
        hp = hips + right * 0.1 * s; kn = hp + d[SEG.index("legUp" + side)] * 0.45; ft = kn + d[SEG.index("legLow" + side)] * 0.45
        lines += [(chest, sh), (sh, el), (el, ha), (hips, hp), (hp, kn), (kn, ft)]
    head = chest + up * 0.08; lines.append((head, head + d[8] * 0.22))
    return lines


def main():
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    clips = load(); names = sys.argv[2:] or list(clips)
    for name in names:
        fps, loops, fr = clips[name]
        k = 8; fig, axes = plt.subplots(2, k, figsize=(k * 1.6, 4.4))
        for j in range(k):
            f = fr[int(j * (len(fr) - 1) / (k - 1))]
            for row, (a, b, title) in enumerate(((0, 1, "front"), (2, 1, "side"))):
                ax = axes[row][j]
                for p0, p1 in pose(f):
                    x0 = -p0[0] if a == 0 else p0[2]; x1 = -p1[0] if a == 0 else p1[2]  # front view: we look at their face
                    ax.plot([x0, x1], [p0[b], p1[b]], "-", color="tab:orange" if row == 0 else "tab:blue", lw=2)
                ax.set_xlim(-1.1, 1.1); ax.set_ylim(-0.1, 2.1); ax.set_aspect("equal"); ax.axis("off")
        fig.suptitle(f"{name} ({len(fr) / fps:.1f}s{' loop' if loops else ''})  top: facing you, bottom: side (forward = right)")
        fig.savefig(os.path.join(out, name + ".png"), dpi=60); plt.close(fig)


if __name__ == "__main__":
    main()
