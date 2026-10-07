#!/usr/bin/env python3
"""
Builds the dance / emote clips for the mod.

Sources:
  * CMU Graphics Lab Motion Capture Database (http://mocap.cs.cmu.edu), BVH conversion by Bruce Hahne
    (cgspeed), mirrored at github.com/una-dinosauria/cmu-mocap. Free for any use, including commercial.
  * Floss, Worm and the Fortnite-style lookalikes are keyframed by hand in dances.py (no source animation).

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

import dances  # the hand-made ones (next to this file)

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

def made(fn):
    """Samples a hand-made dance from dances.py over exactly one loop, so the last frame runs straight into the first."""
    period = fn("period")
    frames = int(round(period * FPS))
    q, off, dirs = [], [], [[] for _ in SEGMENTS]
    for i in range(frames):
        pose = fn(i * period / frames)
        q.append(pose["rot"]); off.append(pose["off"])
        for k, sg in enumerate(SEGMENTS): dirs[k].append(pose[sg])
    q = np.array(q)
    for i in range(1, len(q)):
        if np.dot(q[i], q[i - 1]) < 0: q[i] = -q[i]
    return q, np.array(off), [norm(np.array(d, dtype=float)) for d in dirs]


# ---------------- output ----------------

def sb(v): return int(max(-127, min(127, round(v * 127))))


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "bvh")
    os.makedirs(src, exist_ok=True)
    out = bytearray(b"SNEM"); out += struct.pack("<BH", 1, len(CLIPS))
    for name, label, cat, source, start, length, loops in CLIPS:
        if source.startswith("made:"):
            q, off, dirs = made(getattr(dances, source[5:]))
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
