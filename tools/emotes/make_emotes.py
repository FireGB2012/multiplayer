#!/usr/bin/env python3
"""
Builds the dance / emote clips for the mod.

Sources:
  * CMU Graphics Lab Motion Capture Database (http://mocap.cs.cmu.edu), BVH conversion by Bruce Hahne
    (cgspeed), mirrored at github.com/una-dinosauria/cmu-mocap. Free for any use, including commercial.
  * Floss and Worm are made here from scratch (no source animation).

The game's diver has a different skeleton than the mocap actors, so clips aren't stored as bone rotations.
Each frame stores what the game can map onto any skeleton:
  * torso rotation (relative to facing forward, standing upright)
  * hip offset (in leg lengths, so it scales to the diver)
  * the direction each limb segment points (upper/lower arm, thigh/shin, head), in the player's facing space
    (x = right, y = up, z = forward, Unity's left-handed axes)

Output: src/SubnauticaMP.Plugin/Emotes.bin (embedded in the plugin) and
        src/SubnauticaMP.Shared/EmoteCatalog.g.cs (names / labels / categories, so the server knows them too).

Usage: python3 make_emotes.py <folder with CMU .bvh files, downloaded if missing>
"""
import math, os, struct, sys, urllib.request, zlib
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FPS = 30
MIRROR = "https://raw.githubusercontent.com/una-dinosauria/cmu-mocap/master/data/{:03d}/{}.bvh"

# name, label, category, source, start sec, length sec, loops
# source = CMU clip id, or "made:<fn>" for the hand-made ones. start None = pick the liveliest part by itself.
CLIPS = [
    ("floss",       "Floss",            "Dances", "made:floss", 0, 0, True),
    ("robot",       "Robot",            "Dances", "120_21", None, 6.0, True),
    ("breakdance",  "Breakdance",       "Dances", "90_28", None, 5.0, True),
    ("helicopter",  "Helicopter",       "Dances", "85_08", None, 5.0, True),
    ("footwork",    "Footwork",         "Dances", "85_04", None, 6.0, True),
    ("worm",        "Worm",             "Dances", "made:worm", 0, 0, True),
    ("moonwalk",    "Moonwalk",         "Dances", "90_32", None, 4.0, True),
    ("macarena",    "Macarena",         "Dances", "143_35", None, 8.0, True),
    ("chickendance","Chicken Dance",    "Dances", "143_34", None, 5.0, True),
    ("twist",       "The Twist",        "Dances", "141_12", None, 4.0, True),
    ("charleston",  "Charleston",       "Dances", "93_03", None, 3.0, True),
    ("lambada",     "Lambada",          "Dances", "55_02", None, 6.0, True),
    ("salsa",       "Salsa",            "Dances", "60_01", None, 6.0, True),
    ("russian",     "Russian Dance",    "Dances", "90_30", None, 5.0, True),
    ("cartoon",     "Cartoon Dance",    "Dances", "120_05", None, 6.0, True),
    ("groove",      "Groove",           "Dances", "111_05", None, 6.0, True),
    ("zombie",      "Zombie",           "Fun",    "120_22", None, 5.0, True),
    ("monkey",      "Monkey",           "Fun",    "55_08", None, 6.0, True),
    ("jumpingjacks","Jumping Jacks",    "Fun",    "13_29", None, 4.0, True),
    ("sit",         "Sit Down",         "Poses",  "82_05", "low", 6.0, True),
    ("cartwheel",   "Cartwheel",        "Fun",    "90_02", None, 3.0, False),
    ("monkeyflip",  "Monkey Backflip",  "Fun",    "90_19", None, 3.0, False),
    ("handstand",   "Handstand Kicks",  "Fun",    "85_05", None, 5.0, False),
    ("breakflips",  "Break Combo",      "Fun",    "85_14", None, 7.0, False),
    ("bow",         "Bow",              "Gestures", "111_02", None, 3.0, False),
    ("biglaugh",    "Big Laugh",        "Gestures", "13_14", None, 4.0, False),
    ("wavehello",   "Big Wave",         "Gestures", "111_37", None, 2.5, False),
    # Fortnite-style dances, hand-made lookalikes (keep new ones at the end: an emote's number is its place here)
    ("default",     "Default Dance",    "Dances", "made:default_dance", 0, 0, True),
    ("takethel",    "Take the L",       "Dances", "made:take_the_l", 0, 0, True),
    ("orangejustice","Orange Justice",  "Dances", "made:orange_justice", 0, 0, True),
    ("electroshuffle","Electro Shuffle","Dances", "made:electro_shuffle", 0, 0, True),
    ("griddy",      "Griddy",           "Dances", "made:griddy", 0, 0, True),
    ("hype",        "Hype",             "Dances", "made:hype", 0, 0, True),
]

LENGTHS = {}
DID = {'floss': 'is flossing', 'robot': 'is doing the Robot', 'breakdance': 'is breakdancing', 'helicopter': 'does the Helicopter', 'footwork': 'is breakdancing', 'worm': 'is doing the Worm', 'moonwalk': 'is moonwalking', 'macarena': 'is doing the Macarena', 'chickendance': 'is doing the Chicken Dance', 'twist': 'is doing the Twist', 'charleston': 'is doing the Charleston', 'lambada': 'is dancing the Lambada', 'salsa': 'is dancing Salsa', 'russian': 'is doing a Russian dance', 'cartoon': 'is doing a cartoon dance', 'groove': 'is grooving', 'zombie': 'is being a zombie', 'monkey': 'is being a monkey', 'jumpingjacks': 'is doing jumping jacks', 'sit': 'sits down', 'cartwheel': 'does a cartwheel', 'monkeyflip': 'does a monkey backflip', 'handstand': 'does handstand kicks', 'breakflips': 'pulls off a break combo', 'bow': 'bows', 'biglaugh': 'is dying of laughter', 'wavehello': 'waves', 'default': 'is doing the Default Dance', 'takethel': 'says take the L', 'orangejustice': 'is doing Orange Justice', 'electroshuffle': 'is doing the Electro Shuffle', 'griddy': 'is hitting the Griddy', 'hype': 'is getting hype'}

SEGMENTS = ["armUpL", "armLowL", "armUpR", "armLowR", "legUpL", "legLowL", "legUpR", "legLowR", "head"]
BONES = {  # segment -> (from joint, to joint) in the CMU skeleton
    "armUpL": ("LeftArm", "LeftForeArm"), "armLowL": ("LeftForeArm", "LeftHand"),
    "armUpR": ("RightArm", "RightForeArm"), "armLowR": ("RightForeArm", "RightHand"),
    "legUpL": ("LeftUpLeg", "LeftLeg"), "legLowL": ("LeftLeg", "LeftFoot"),
    "legUpR": ("RightUpLeg", "RightLeg"), "legLowR": ("RightLeg", "RightFoot"),
    "head": ("Neck1", "Head"),
}


# ---------------- BVH ----------------

def rot(axis, deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    if axis == "X": return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
    if axis == "Y": return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def load_bvh(path):
    tokens = open(path).read().split()
    joints, stack, i = [], [], 0
    while tokens[i] != "MOTION":
        t = tokens[i]
        if t in ("ROOT", "JOINT"):
            joints.append({"name": tokens[i + 1], "parent": stack[-1] if stack else -1, "offset": None, "channels": []})
            i += 2
        elif t == "End":
            joints.append({"name": "_end", "parent": stack[-1], "offset": None, "channels": []}); i += 2
        elif t == "{":
            stack.append(len(joints) - 1); i += 1
        elif t == "}":
            stack.pop(); i += 1
        elif t == "OFFSET":
            joints[stack[-1]]["offset"] = np.array([float(x) for x in tokens[i + 1:i + 4]]); i += 4
        elif t == "CHANNELS":
            n = int(tokens[i + 1]); joints[stack[-1]]["channels"] = tokens[i + 2:i + 2 + n]; i += 2 + n
        else:
            i += 1
    frames = int(tokens[i + 2]); dt = float(tokens[i + 5])
    data = np.array(tokens[i + 6:], dtype=float).reshape(frames, -1)
    return joints, data, dt


def positions(joints, data):
    """World positions of every joint, every frame -> dict name -> (frames, 3), in Unity axes."""
    n = len(data)
    pos = [None] * len(joints); rots = [None] * len(joints)
    col = 0
    for j, jt in enumerate(joints):
        chans = jt["channels"]
        vals = data[:, col:col + len(chans)]; col += len(chans)
        local_pos = np.tile(jt["offset"], (n, 1))
        local_rot = np.tile(np.eye(3), (n, 1, 1))
        for k, ch in enumerate(chans):
            if ch.endswith("position"):
                local_pos[:, "XYZ".index(ch[0])] = vals[:, k] + (jt["offset"]["XYZ".index(ch[0])] if jt["parent"] >= 0 else 0)
        rot_chans = [(k, ch[0]) for k, ch in enumerate(chans) if ch.endswith("rotation")]
        if rot_chans:
            for f in range(n):
                m = np.eye(3)
                for k, axis in rot_chans: m = m @ rot(axis, vals[f, k])
                local_rot[f] = m
        if jt["parent"] < 0:
            pos[j], rots[j] = local_pos, local_rot
        else:
            p = jt["parent"]
            pos[j] = pos[p] + np.einsum("fij,fj->fi", rots[p], local_pos)
            rots[j] = np.einsum("fij,fjk->fik", rots[p], local_rot)
    out = {}
    for j, jt in enumerate(joints):
        if jt["name"] != "_end":
            p = pos[j].copy(); p[:, 0] *= -1  # right-handed BVH -> Unity's left-handed axes
            out[jt["name"]] = p
    return out


# ---------------- turning positions into clip frames ----------------

def norm(v):
    l = np.linalg.norm(v, axis=-1, keepdims=True); return v / np.maximum(l, 1e-9)


def torso_basis(P):
    right = norm((P["RightArm"] - P["LeftArm"]) + (P["RightUpLeg"] - P["LeftUpLeg"]))
    up = norm(P["Neck"] - P["Hips"])
    up = norm(up - right * np.sum(up * right, axis=-1, keepdims=True))
    fwd = np.cross(right, up)  # same formula as Unity's Vector3.Cross: (1,0,0)x(0,1,0) = (0,0,1)
    return right, up, fwd


def quat_from_basis(r, u, f):
    """Rotation whose x/y/z axes are r/u/f (Unity quaternion x,y,z,w)."""
    m = np.stack([r, u, f], axis=-1)  # columns
    q = np.zeros((len(m), 4))
    for i, a in enumerate(m):
        tr = a[0, 0] + a[1, 1] + a[2, 2]
        if tr > 0:
            s = math.sqrt(tr + 1.0) * 2
            q[i] = [(a[2, 1] - a[1, 2]) / s, (a[0, 2] - a[2, 0]) / s, (a[1, 0] - a[0, 1]) / s, 0.25 * s]
        elif a[0, 0] > a[1, 1] and a[0, 0] > a[2, 2]:
            s = math.sqrt(1.0 + a[0, 0] - a[1, 1] - a[2, 2]) * 2
            q[i] = [0.25 * s, (a[0, 1] + a[1, 0]) / s, (a[0, 2] + a[2, 0]) / s, (a[2, 1] - a[1, 2]) / s]
        elif a[1, 1] > a[2, 2]:
            s = math.sqrt(1.0 + a[1, 1] - a[0, 0] - a[2, 2]) * 2
            q[i] = [(a[0, 1] + a[1, 0]) / s, 0.25 * s, (a[1, 2] + a[2, 1]) / s, (a[0, 2] - a[2, 0]) / s]
        else:
            s = math.sqrt(1.0 + a[2, 2] - a[0, 0] - a[1, 1]) * 2
            q[i] = [(a[0, 2] + a[2, 0]) / s, (a[1, 2] + a[2, 1]) / s, 0.25 * s, (a[1, 0] - a[0, 1]) / s]
    # keep neighbours on the same side so interpolation never flips
    for i in range(1, len(q)):
        if np.dot(q[i], q[i - 1]) < 0: q[i] = -q[i]
    return q


def resample(arr, src_dt, n_out):
    t_src = np.arange(len(arr)) * src_dt
    t_out = np.arange(n_out) / FPS
    return np.stack([np.interp(t_out, t_src, arr[:, k]) for k in range(arr.shape[1])], axis=1)


def smooth(x, window):
    if window <= 1: return x
    k = np.ones(window) / window
    pad = np.pad(x, ((window // 2, window - 1 - window // 2), (0, 0)), mode="edge")
    return np.stack([np.convolve(pad[:, c], k, mode="valid") for c in range(x.shape[1])], axis=1)


def from_mocap(path, start, length, loops):
    joints, data, dt = load_bvh(path)
    P = positions(joints, data)
    # frame 0 is the actor standing in a T pose: that's "standing" height
    floor0 = min(P["LeftFoot"][0, 1], P["RightFoot"][0, 1])
    stand = P["Hips"][0, 1] - floor0
    names = {j["name"] for j in joints}
    need = {"Hips", "Neck", "Neck1", "Head", "LeftArm", "RightArm", "LeftUpLeg", "RightUpLeg"} | {b for s in BONES.values() for b in s}
    missing = need - names
    if missing: raise SystemExit(f"{path}: missing joints {missing}")

    # skip the T-pose frame(s) at the start, pick the part of the clip where the most happens
    skip = int(1.0 / dt)
    frames = len(data)
    want = int(length / dt)
    if start == "low":  # the part where the hips are lowest (sitting)
        h = P["Hips"][:, 1]
        a = min(range(skip, max(skip + 1, frames - want), max(1, int(0.25 / dt))), key=lambda s0: h[s0:s0 + want].mean())
    elif start is None:
        speed = np.zeros(frames)
        for name in ("LeftHand", "RightHand", "LeftFoot", "RightFoot", "Head", "Hips"):
            v = np.linalg.norm(np.diff(P[name], axis=0), axis=1); speed[1:] += v
        best, best_s = skip, -1
        for s in range(skip, max(skip + 1, frames - want), max(1, int(0.25 / dt))):
            e = speed[s:s + want].sum()
            if e > best_s: best, best_s = s, e
        a = best
    if start in (None, "low"):
        pass
    else:
        a = int(start / dt)
    b = min(frames, a + want)
    P = {k: v[a:b] for k, v in P.items()}

    leg = np.linalg.norm(P["LeftUpLeg"][0] - P["LeftLeg"][0]) + np.linalg.norm(P["LeftLeg"][0] - P["LeftFoot"][0])

    # face forward: the average facing over the clip becomes +z
    r, u, f = torso_basis(P)
    fwd = f.mean(axis=0); yaw = math.atan2(fwd[0], fwd[2])
    c, s = math.cos(-yaw), math.sin(-yaw)
    Y = np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
    P = {k: v @ Y.T for k, v in P.items()}
    r, u, f = torso_basis(P)

    # hip offset: no travelling around (smoothed path removed), height relative to standing
    hips = P["Hips"].copy()
    hips[:, 1] -= floor0
    travel = smooth(hips[:, [0, 2]], int(1.2 / dt))
    off = np.stack([hips[:, 0] - travel[:, 0], hips[:, 1] - stand, hips[:, 2] - travel[:, 1]], axis=1) / leg

    q = quat_from_basis(r, u, f)
    dirs = [norm(P[BONES[sg][1]] - P[BONES[sg][0]]) for sg in SEGMENTS]

    n_out = max(2, int((b - a) * dt * FPS))
    q = resample(q, dt, n_out); q = q / np.linalg.norm(q, axis=1, keepdims=True)
    off = resample(off, dt, n_out)
    dirs = [norm(resample(d, dt, n_out)) for d in dirs]
    if loops: q, off, dirs = make_loop(q, off, dirs)
    return q, off, dirs


def make_loop(q, off, dirs):
    """Ends where it starts: pick the frame near the end that looks most like the first one, blend into it."""
    n = len(q)
    def pose(i): return np.concatenate([q[i], off[i]] + [d[i] for d in dirs])
    first = pose(0)
    lo = max(2, int(n * 0.6))
    end = min(range(lo, n), key=lambda i: np.linalg.norm(pose(i) - first))
    q, off, dirs = q[:end], off[:end], [d[:end] for d in dirs]
    blend = min(int(0.4 * FPS), len(q) // 3)
    for k in range(blend):
        w = (k + 1) / (blend + 1)  # 0 -> 1 toward the start pose
        i = len(q) - blend + k
        qs = q[0] if np.dot(q[0], q[i]) >= 0 else -q[0]
        q[i] = q[i] * (1 - w) + qs * w; q[i] /= np.linalg.norm(q[i])
        off[i] = off[i] * (1 - w) + off[0] * w
        for d in dirs:
            d[i] = d[i] * (1 - w) + d[0] * w; d[i] /= np.linalg.norm(d[i])
    return q, off, dirs


# ---------------- hand-made ----------------

def euler_quat(pitch, yaw, roll):
    """Unity Quaternion.Euler(pitch, yaw, roll) (degrees), as x,y,z,w."""
    x, y, z = [math.radians(a) / 2 for a in (pitch, yaw, roll)]
    cx, sx, cy, sy, cz, sz = math.cos(x), math.sin(x), math.cos(y), math.sin(y), math.cos(z), math.sin(z)
    # Unity applies z, then x, then y
    qw = cy * cx * cz + sy * sx * sz
    qx = cy * sx * cz + sy * cx * sz
    qy = sy * cx * cz - cy * sx * sz
    qz = cy * cx * sz - sy * sx * cz
    return [qx, qy, qz, qw]


def made(fn):
    q, off, dirs = [], [], [[] for _ in SEGMENTS]
    period, frames = fn("period"), None
    frames = int(round(period * FPS))
    for i in range(frames):
        t = i / FPS
        pose = fn(t)
        q.append(pose["rot"]); off.append(pose["off"])
        for k, sg in enumerate(SEGMENTS): dirs[k].append(pose[sg])
    q = np.array(q)
    for i in range(1, len(q)):
        if np.dot(q[i], q[i - 1]) < 0: q[i] = -q[i]
    return q, np.array(off), [norm(np.array(d, dtype=float)) for d in dirs]


def floss(t):
    beat = 0.42  # seconds per swing
    if t == "period": return beat * 4
    w = math.pi / beat
    s = math.sin(w * t)                 # arms side to side
    sw = math.sin(w * t / 2)            # which arm is in front flips every other swing
    front = 0.5 if sw >= 0 else -0.5
    arm_l = [0.75 * s, -1.0, front * abs(s) + 0.05]
    arm_r = [0.75 * s, -1.0, -front * abs(s) + 0.05]
    hip = -0.09 * s
    bounce = 0.02 * abs(math.sin(w * t))
    return {
        "rot": euler_quat(0, 14 * s, -6 * s), "off": [hip, -0.03 - bounce, 0],
        "armUpL": arm_l, "armLowL": arm_l, "armUpR": arm_r, "armLowR": arm_r,
        "legUpL": [-0.12 - 0.06 * s, -1, 0.05], "legLowL": [-0.05, -1, -0.05],
        "legUpR": [0.12 - 0.06 * s, -1, 0.05], "legLowR": [0.05, -1, -0.05],
        "head": [0.1 * s, 1, 0.1],
    }


def worm(t):
    period = 1.1
    if t == "period": return period * 2
    ph = 2 * math.pi * t / period
    chest = max(0.0, math.sin(ph))          # chest pops up first...
    hips = max(0.0, math.sin(ph - 1.6))     # ...then the wave rolls down to the hips
    pitch = 90 - 22 * chest + 16 * hips     # lying face down, chest lifting / hips lifting
    return {
        "rot": euler_quat(pitch, 0, 0), "off": [0, -0.72 + 0.18 * hips + 0.08 * chest, 0.25 * math.sin(ph)],
        "armUpL": [-0.35, -0.9 + 0.5 * chest, 0.15], "armLowL": [0.1, -1, 0.45],
        "armUpR": [0.35, -0.9 + 0.5 * chest, 0.15], "armLowR": [-0.1, -1, 0.45],
        "legUpL": [-0.08, -0.25 + 0.55 * hips, -1], "legLowL": [-0.05, 0.1 + 0.6 * max(0, math.sin(ph - 2.6)), -1],
        "legUpR": [0.08, -0.25 + 0.55 * hips, -1], "legLowR": [0.05, 0.1 + 0.6 * max(0, math.sin(ph - 2.6)), -1],
        "head": [0, 0.3 + 0.6 * chest, 1],
    }


# ---------------- Fortnite-style dances ----------------
# Poses are built as dicts of plain tuples (rot = Unity Euler degrees) so they can be blended and mirrored,
# then turned into what made() wants by done(). Each one loops on a whole number of beats.

REST = dict(rot=(0, 0, 0), off=(0, 0, 0),
            armUpL=(-0.25, -0.9, 0.2), armLowL=(-0.05, -0.4, 0.9),   # elbows bent, forearms forward: ready to dance
            armUpR=(0.25, -0.9, 0.2), armLowR=(0.05, -0.4, 0.9),
            legUpL=(-0.08, -1, 0), legLowL=(-0.03, -1, 0), legUpR=(0.08, -1, 0), legLowR=(0.03, -1, 0),
            head=(0, 1, 0.08))


def pose(base=REST, **kw):
    p = dict(base); p.update(kw); return p


def mix(a, b, w):
    return {k: tuple(x * (1 - w) + y * w for x, y in zip(a[k], b[k])) for k in a}


def mirror(p):
    """The same move on the other side."""
    out = {}
    for k, v in p.items():
        k2 = k[:-1] + {"L": "R", "R": "L"}[k[-1]] if k[-1] in "LR" else k
        out[k2] = (v[0], -v[1], -v[2]) if k == "rot" else (-v[0], v[1], v[2])
    return out


def done(p):
    out = {k: list(v) for k, v in p.items()}
    out["rot"] = euler_quat(*p["rot"])
    return out


def ease(x):
    x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


def squat(p, depth):
    """Hips down, knees bent forward to match."""
    k = depth / 0.1
    return pose(p, off=(p["off"][0], p["off"][1] - depth, p["off"][2]),
                legUpL=(p["legUpL"][0], -1, p["legUpL"][2] + 0.35 * k), legLowL=(p["legLowL"][0], -1, p["legLowL"][2] - 0.3 * k),
                legUpR=(p["legUpR"][0], -1, p["legUpR"][2] + 0.35 * k), legLowR=(p["legLowR"][0], -1, p["legLowR"][2] - 0.3 * k))


def default_dance(t):
    beat = 0.5
    if t == "period": return beat * 8
    b, ph = divmod(t / beat, 1.0)
    base = squat(REST, 0.03)
    if b < 4:  # step-punch: knee up, opposite arm punches up across the body, every beat the other side
        punch = pose(base, rot=(0, -12, 4), off=(0.02, 0.03, 0),
                     armUpR=(-0.1, 0.75, 0.65), armLowR=(-0.25, 0.9, 0.3),
                     armUpL=(-0.35, -0.85, -0.2), armLowL=(-0.1, -0.3, 0.95),
                     legUpR=(0.1, -0.25, 1), legLowR=(0.08, -1, 0.2), head=(-0.1, 1, 0.15))
        if int(b) % 2: punch = mirror(punch)
        return done(mix(base, punch, math.sin(math.pi * ph)))
    # swing: both arms swing side to side, hips the other way, knees dip on every beat
    s = math.sin(math.pi * (t / beat - 4) / 2)
    swing = pose(base, rot=(0, 10 * s, -5 * s), off=(-0.05 * s, 0, 0),
                 armUpL=(0.75 * s - 0.15, -0.55, 0.45), armLowL=(0.85 * s - 0.1, -0.35, 0.5),
                 armUpR=(0.75 * s + 0.15, -0.55, 0.45), armLowR=(0.85 * s + 0.1, -0.35, 0.5),
                 head=(0.15 * s, 1, 0.1))
    return done(squat(mix(base, swing, abs(s)), 0.05 * abs(math.sin(math.pi * ph))))


def take_the_l(t):
    beat = 0.375
    if t == "period": return beat * 8
    b, ph = divmod(t / beat, 1.0)
    kick = math.sin(math.pi * ph)              # every beat: hop and kick the left leg out to the side
    turn = math.sin(2 * math.pi * t / (beat * 8))
    p = pose(REST, rot=(0, 18 * turn, -6 * kick), off=(0.04 * kick, 0.05 * kick, 0),
             armUpR=(0.75, 0.45, 0.45), armLowR=(-0.75, 0.35, 0.3),           # the L on the forehead
             armUpL=(-0.35 - 0.5 * kick, -0.85 + 0.6 * kick, 0.25), armLowL=(-0.2 - 0.7 * kick, -0.4 + 0.5 * kick, 0.6),
             legUpL=(-0.08 - 0.75 * kick, -1 + 0.35 * kick, 0.1), legLowL=(-0.03 - 0.8 * kick, -1 + 0.4 * kick, 0.05),
             legUpR=(0.06, -1, 0.15), legLowR=(0.04, -1, -0.1), head=(0.1 * kick, 1, 0.1))
    return done(p)


def orange_justice(t):
    beat = 0.4
    if t == "period": return beat * 4
    s = math.sin(math.pi * t / beat)           # swings to one side and back every two beats
    right = squat(pose(REST, rot=(6, 0, 10), off=(-0.06, 0, 0),
                       armUpL=(0.55, -0.15, 0.8), armLowL=(0.7, 0.15, 0.7),       # left arm swings up across
                       armUpR=(0.5, -0.6, -0.6), armLowR=(0.45, -0.8, -0.4),      # right arm swings back and out
                       legUpR=(0.6, -0.75, 0.2), legLowR=(0.25, -0.95, -0.2),     # right leg out, knee bent
                       legUpL=(-0.05, -1, 0.1), head=(-0.15, 1, 0.12)), 0.07)
    p = mix(mirror(right), right, (s + 1) / 2)
    return done(squat(p, 0.04 * (1 - abs(s))))  # dips through the middle


def electro_shuffle(t):
    beat = 0.375
    if t == "period": return beat * 8
    side = math.sin(math.pi * t / (beat * 4))  # two bars: right, then left
    pump = abs(math.sin(math.pi * t / beat))   # arm pushes out on every beat
    right = pose(REST, rot=(0, 12, -4), off=(0.08, 0, 0),
                 armUpR=(0.55 + 0.4 * pump, 0.05, 0.35 - 0.1 * pump), armLowR=(0.35 + 0.65 * pump, 0.1, 0.8 - 0.65 * pump),
                 armUpL=(-0.5, -0.3, 0.6), armLowL=(0.6, 0.1, 0.75),                 # other arm across the chest
                 legUpR=(0.3 + 0.1 * pump, -0.9, 0.2), legLowR=(0.3, -0.9, 0.35 * pump),  # heel taps out
                 head=(0.2, 1, 0.1))
    p = mix(mirror(right), right, ease(0.5 + 1.5 * side))  # switches sides quick, then holds
    return done(squat(p, 0.03 + 0.04 * pump))


def griddy(t):
    beat = 0.3
    if t == "period": return beat * 8
    pos = t / beat
    s = math.sin(math.pi * pos)                # one leg each beat
    right = squat(pose(REST, rot=(12, -6, 0),
                       legUpR=(0.08, -0.45, 0.9), legLowR=(0.05, -1, -0.2),          # right knee up, heel tap
                       armUpL=(-0.15, -0.6, 0.8), armLowL=(0.1, 0.3, 0.95),          # opposite arm forward
                       armUpR=(0.2, -0.6, -0.8), armLowR=(0.1, -0.9, -0.4),          # same-side arm back
                       head=(0, 1, 0.3)), 0.06)
    p = mix(mirror(right), right, (s + 1) / 2)
    # last two beats: hands around the eyes like goggles
    g = ease((pos - 5.6) / 0.6) * ease((8 - pos) / 0.4)
    goggles = pose(p, armUpL=(-0.8, 0.3, 0.45), armLowL=(0.55, 0.5, 0.65), armUpR=(0.8, 0.3, 0.45), armLowR=(-0.55, 0.5, 0.65),
                   head=(0, 1, 0.05))
    return done(mix(p, goggles, g))


def hype(t):
    beat = 0.4
    if t == "period": return beat * 8
    b, ph = divmod(t / beat, 1.0)
    hop = math.sin(math.pi * ph)
    a = 2 * math.pi * 2 * ((t / beat) % 4) / 4     # two big arm circles per half
    kick_l = int(b) % 2 == 0
    p = pose(REST, off=(0, 0.04 * hop, 0),
             armUpR=(0.2 + 0.8 * math.sin(a), -math.cos(a), 0.35), armLowR=(0.2 + 0.8 * math.sin(a), -math.cos(a), 0.45),
             armUpL=(-0.6, -0.6, 0.1), armLowL=(0.3, 0.2, 0.9),             # other fist pumping at the chest
             head=(0.2 * math.sin(a), 1, 0.1))
    side = "L" if kick_l else "R"
    x = -0.08 if kick_l else 0.08
    p = pose(p, **{"legUp" + side: (x, -1, -0.15 * hop), "legLow" + side: (x, -1 + 0.8 * hop, -hop)})
    p = squat(p, 0.03 * (1 - hop))
    return done(p if b < 4 else mirror(p))


# ---------------- output ----------------

def sb(v): return int(max(-127, min(127, round(v * 127))))


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "bvh")
    os.makedirs(src, exist_ok=True)
    out = bytearray(b"SNEM"); out += struct.pack("<BH", 1, len(CLIPS))
    for name, label, cat, source, start, length, loops in CLIPS:
        if source.startswith("made:"):
            q, off, dirs = made(globals()[source[5:]])
        else:
            path = os.path.join(src, source + ".bvh")
            if not os.path.exists(path):
                urllib.request.urlretrieve(MIRROR.format(int(source.split("_")[0]), source), path)
            q, off, dirs = from_mocap(path, start, length, loops)
        n = len(q)
        LENGTHS[name] = n / FPS
        enc = name.encode()
        out += struct.pack("<B", len(enc)) + enc + struct.pack("<BHB", FPS, n, 1 if loops else 0)
        for i in range(n):
            out += struct.pack("<4b", *[sb(x) for x in q[i]])
            out += struct.pack("<3h", *[int(max(-32767, min(32767, round(x * 1000)))) for x in off[i]])
            for d in dirs: out += struct.pack("<3b", *[sb(x) for x in d[i]])
        print(f"{name:14s} {n / FPS:5.1f}s {'loop' if loops else 'once'}  hips y {off[:, 1].min():+.2f}..{off[:, 1].max():+.2f}")
    packed = zlib.compress(bytes(out), 9)
    open(os.path.join(ROOT, "src", "SubnauticaMP.Plugin", "Emotes.bin"), "wb").write(packed)
    print(f"Emotes.bin: {len(out) // 1024} KB raw, {len(packed) // 1024} KB packed")

    lines = ["// Generated by tools/emotes/make_emotes.py. Don't edit by hand.",
             "namespace SubnauticaMP.Shared", "{", "    public static partial class Emotes", "    {",
             "        // name, button text, category, chat text, loops, length (the animation data is in the plugin's Emotes.bin)",
             "        static readonly (string name, string label, string category, string did, bool loops, float seconds)[] Clips =", "        {"]
    for name, label, cat, source, start, length, loops in CLIPS:
        lines.append(f'            ("{name}", "{label}", "{cat}", "{DID[name]}", {"true" if loops else "false"}, {LENGTHS[name]:.2f}f),')
    lines += ["        };", "    }", "}", ""]
    open(os.path.join(ROOT, "src", "SubnauticaMP.Shared", "EmoteCatalog.g.cs"), "w").write("\n".join(lines))


if __name__ == "__main__":
    main()
