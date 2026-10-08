#!/usr/bin/env python3
"""
Previews the Titanium Bat swing (the keys in src/SubnauticaMP.Shared/Bat.cs) from first person, so you can
check the bat is on screen where it should be without starting the game. Same maths as BatPose.cs (eased
keys, slerped directions, two-bone IK), on a stand-in diver arm (sizes measured off the dive suit, roughly).

Output: a contact sheet (first person frames + a top view per frame).
Usage: python3 tools/bat/preview_swing.py out.png [pitch_degrees]
"""
import math, os, re, sys
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "src", "SubnauticaMP.Shared", "Bat.cs")

SHOULDER_R = np.array([0.19, -0.24, -0.06])   # camera space (x right, y up, z forward)
SHOULDER_L = np.array([-0.19, -0.24, -0.06])
UPPER, LOWER = 0.29, 0.27                      # arm bone lengths
TIP, BUTT = 0.683, -0.159                      # from tools/bat/make_bat.py
VFOV, W, H = 65.0, 480, 270
CURVES = {"Linear": (0, 0, 1, 1), "OutCubic": (.33, 1, .68, 1), "InCubic": (.32, 0, .67, 0),
          "InOutCubic": (.645, .045, .355, 1), "OutQuint": (.23, 1, .32, 1), "OutBack": (.34, 1.56, .64, 1)}


def bezier(c, t):
    x1, y1, x2, y2 = c
    if t <= 0 or t >= 1: return t
    f = lambda p1, p2, u: ((1 - 3 * p2 + 3 * p1) * u + (3 * p2 - 6 * p1)) * u * u + 3 * p1 * u
    lo, hi = 0.0, 1.0
    for _ in range(40):
        u = (lo + hi) / 2
        if f(x1, x2, u) < t: lo = u
        else: hi = u
    return f(y1, y2, (lo + hi) / 2)


def parse():
    src = open(SRC).read()
    consts = dict(re.findall(r"public const float (\w+) = ([\d.]+)f;", src))
    vec = r"new Vec3\(([-\d.f, ]+)\)"
    def v(s): return np.array([float(x.strip().rstrip("f")) for x in s.split(",")])
    idle_m = re.search(r"Key Idle = new Key\(0f, " + vec + r", ([\d.]+)f, " + vec + ", " + vec, src)
    idle = dict(t=0, hand=v(idle_m.group(1)), reach=float(idle_m.group(2)), bat=v(idle_m.group(3)), pole=v(idle_m.group(4)), twist=0, left=0, ease="Linear")
    keys = [idle]
    for m in re.finditer(r"new Key\((\w+|[\d.]+f), " + vec + r", ([\d.]+)f, " + vec + ", " + vec + r", (-?[\d.]+)f, ([\d.]+)f, Curve\.(\w+)\)", src):
        t = m.group(1)
        t = float(consts[t]) if t in consts else float(t.rstrip("f"))
        keys.append(dict(t=t, hand=v(m.group(2)), reach=float(m.group(3)), bat=v(m.group(4)), pole=v(m.group(5)), twist=float(m.group(6)), left=float(m.group(7)), ease=m.group(8)))
    keys.append(dict(idle, t=float(consts["Seconds"]), ease="InOutCubic"))
    return keys, float(consts["Contact"])


def norm(x): return x / np.linalg.norm(x)


def slerp(a, b, f):
    a, b = norm(a), norm(b)
    d = np.clip(np.dot(a, b), -1, 1)
    ang = math.acos(d)
    if ang < 1e-5: return a
    return (math.sin((1 - f) * ang) * a + math.sin(f * ang) * b) / math.sin(ang)


def sample(keys, t):
    for i in range(1, len(keys)):
        if t <= keys[i]["t"]:
            a, b = keys[i - 1], keys[i]
            f = bezier(CURVES[b["ease"]], (t - a["t"]) / max(1e-4, b["t"] - a["t"]))
            lerp = lambda k: a[k] + (b[k] - a[k]) * f
            return dict(hand=slerp(a["hand"], b["hand"], f), bat=slerp(a["bat"], b["bat"], f), pole=slerp(a["pole"], b["pole"], f),
                        reach=lerp("reach"), twist=lerp("twist"), left=lerp("left"))
    return sample(keys, keys[-1]["t"])


def rot_y(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rot_x(deg):  # + = look down (Unity euler x)
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def two_bone(a, target, pole):
    d = np.clip(np.linalg.norm(target - a), abs(UPPER - LOWER) + 1e-3, (UPPER + LOWER) * 0.999)
    direction = norm(target - a)
    cos = np.clip((UPPER ** 2 + d ** 2 - LOWER ** 2) / (2 * UPPER * d), -1, 1)
    side = pole - direction * np.dot(pole, direction)
    side = norm(side) if np.linalg.norm(side) > 1e-6 else np.array([0, -1, 0])
    elbow = a + direction * cos * UPPER + side * math.sqrt(1 - cos * cos) * UPPER
    return elbow, a + direction * d


def frame(keys, t, pitch, twist_scale=0.4):
    p = sample(keys, t)
    look = rot_x(-pitch)                         # the swing's space relative to... the camera (= look)
    chest = rot_y(p["twist"] * twist_scale)      # chest turn moves the shoulders (camera stays)
    sR, sL = chest @ SHOULDER_R, chest @ SHOULDER_L
    # the camera looks along `look`; express everything in camera space: camera space = look space here
    hand_target = sR + p["hand"] * p["reach"] * (UPPER + LOWER)
    elbowR, handR = two_bone(sR, hand_target, p["pole"])
    bat = norm(p["bat"])
    tip, butt = handR + bat * TIP, handR + bat * BUTT
    elbowL = handL = None
    if p["left"] > 0.01:
        lt = handR - bat * 0.11
        elbowL, handL = two_bone(sL, lt, np.array([-0.6, -0.8, 0]))
        w = p["left"]
        handL = handL * w + (sL + np.array([0, -0.5, 0.05])) * (1 - w)
        elbowL = elbowL * w + (sL + np.array([-0.03, -0.27, 0])) * (1 - w)
    return dict(sR=sR, elbowR=elbowR, handR=handR, tip=tip, butt=butt, sL=sL, elbowL=elbowL, handL=handL)


def project(p):
    f = (H / 2) / math.tan(math.radians(VFOV / 2))
    z = max(p[2], 0.02)
    return (W / 2 + p[0] / z * f, H / 2 - p[1] / z * f), p[2] > 0.02


def draw_fp(fr, label):
    img = Image.new("RGB", (W, H), (14, 52, 74))
    d = ImageDraw.Draw(img)
    def seg(a, b, col, width):
        (pa, va), (pb, vb) = project(a), project(b)
        if va and vb: d.line([pa, pb], fill=col, width=width)
    seg(fr["sR"], fr["elbowR"], (230, 140, 40), 10); seg(fr["elbowR"], fr["handR"], (230, 140, 40), 8)
    if fr["elbowL"] is not None:
        seg(fr["sL"], fr["elbowL"], (200, 120, 40), 10); seg(fr["elbowL"], fr["handL"], (200, 120, 40), 8)
    seg(fr["butt"], fr["handR"], (20, 20, 25), 7)
    seg(fr["handR"], fr["tip"], (120, 160, 190), 9)
    (pt, vt) = project(fr["tip"])
    if vt: d.ellipse([pt[0] - 4, pt[1] - 4, pt[0] + 4, pt[1] + 4], fill=(90, 240, 255))
    d.text((6, 4), label, fill=(255, 255, 255))
    return img


def draw_top(fr):
    S = 270
    img = Image.new("RGB", (S, S), (30, 30, 36))
    d = ImageDraw.Draw(img)
    sc = 160
    P = lambda p: (S / 2 + p[0] * sc, S * 0.62 - p[2] * sc)
    d.polygon([P(np.array([0, 0, 0])), P(np.array([-1.2, 0, 1.0])), P(np.array([1.2, 0, 1.0]))], outline=(70, 70, 90))
    d.line([P(fr["sR"]), P(fr["elbowR"]), P(fr["handR"])], fill=(230, 140, 40), width=4)
    if fr["elbowL"] is not None: d.line([P(fr["sL"]), P(fr["elbowL"]), P(fr["handL"])], fill=(200, 120, 40), width=4)
    d.line([P(fr["butt"]), P(fr["tip"])], fill=(120, 160, 190), width=5)
    d.ellipse([S / 2 - 4, S * 0.62 - 4, S / 2 + 4, S * 0.62 + 4], fill=(255, 255, 255))
    return img


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "swing.png"
    pitch = float(sys.argv[2]) if len(sys.argv) > 2 else 0.0
    keys, contact = parse()
    times = [0, 0.08, 0.16, 0.20, 0.23, 0.255, contact, 0.33, 0.40, 0.48, 0.6, keys[-1]["t"]]
    cols = 4
    sheet = Image.new("RGB", (cols * (W + 270), math.ceil(len(times) / cols) * H), (0, 0, 0))
    for i, t in enumerate(times):
        fr = frame(keys, t, pitch)
        tag = f"t={t:.3f}" + ("  CONTACT" if abs(t - contact) < 1e-4 else "")
        x, y = (i % cols) * (W + 270), (i // cols) * H
        sheet.paste(draw_fp(fr, tag), (x, y))
        sheet.paste(draw_top(fr), (x + W, y))
    sheet.save(out)
    print("wrote", out)


if __name__ == "__main__":
    main()
