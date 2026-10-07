"""
The hand-made dances for make_emotes.py: Floss, Worm and the Fortnite-style lookalikes (made here from
scratch, no Fortnite files).

They're keyframed on the beat the way an animator would do it: a pose on (or between) beats, an easing curve
into each pose, and the legs solved with IK every frame, so a planted foot stays put while the hips move.
The curves are the CSS cubic-beziers from the animate skill: ease-out-back for moves that hit the beat,
ease-in-out for moves that travel, ease-in for drops and landings, ease-out for lifts.

Units are leg lengths (hip joint to ankle = 1), in the player's facing space: x = right, y = up, z = forward
(Unity's axes). Standing straight, the middle of the hips is at y = 1 and the ankles at y = 0.
A dance is a function of t in seconds (or "period") giving the frame make_emotes.made() wants.
"""
import math
import numpy as np

FPS = 30

# Rough diver proportions in leg lengths. The game points its own bones the way these point, so they only
# need to be close: they decide where a foot lands or where a hand meets the face.
HIP_W = 0.10                    # hip joint, out from the middle of the hips
THIGH = SHIN = 0.5
SHOULDER_UP, SHOULDER_W = 0.62, 0.22
UPPER_ARM, FOREARM = 0.36, 0.34
HEAD_UP = 0.86                  # middle of the head, above the middle of the hips

X, Y, Z = np.array([1.0, 0, 0]), np.array([0, 1.0, 0]), np.array([0, 0, 1.0])


# ---------------- easing (cubic-bezier, from the animate skill) ----------------

def bezier(x1, y1, x2, y2):
    """CSS cubic-bezier(x1, y1, x2, y2): progress 0..1 -> eased progress."""
    def bx(t): return 3 * x1 * t * (1 - t) ** 2 + 3 * x2 * t * t * (1 - t) + t ** 3
    def by(t): return 3 * y1 * t * (1 - t) ** 2 + 3 * y2 * t * t * (1 - t) + t ** 3

    def ease(x):
        if x <= 0: return 0.0
        if x >= 1: return 1.0
        lo, hi = 0.0, 1.0
        for _ in range(40):
            mid = (lo + hi) / 2
            if bx(mid) < x: lo = mid
            else: hi = mid
        return by((lo + hi) / 2)
    return ease


HIT = bezier(.34, 1.56, .64, 1)       # ease-out-back: gets there fast, overshoots a touch, settles. Moves on the beat.
IN_OUT = bezier(.645, .045, .355, 1)  # ease-in-out-cubic: travelling from one spot to another
OUT = bezier(.33, 1, .68, 1)          # ease-out-cubic: lifts, and contacts that mustn't overshoot (a clap, a heel)
IN = bezier(.32, 0, .67, 0)           # ease-in-cubic: drops and landings (speeding up like falling)
HOLD = lambda u: 0.0                  # stays at the previous key until this one
# per axis (x, y, z) for feet, so they come down on their spot instead of skidding onto it
LAND = (OUT, IN, OUT)                 # over the spot first, then straight down
LIFT = (IN, OUT, IN)                  # straight up first, then away


# ---------------- vectors and turns ----------------

def unit(v):
    v = np.asarray(v, dtype=float); return v / np.linalg.norm(v)


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


def turn(fwd=0.0, right=0.0, lean=0.0):
    """Body turn in degrees: fwd = bend forward, right = face to the right, lean = lean over to the right."""
    return euler_quat(fwd, right, -lean)


def qrot(q, v):
    """Unity's Quaternion * Vector3."""
    x, y, z, w = q
    u = np.array([x, y, z]); v = np.asarray(v, dtype=float)
    return 2 * np.dot(u, v) * u + (w * w - np.dot(u, u)) * v + 2 * w * np.cross(u, v)


def slerp(a, b, u):
    """Swings direction a toward b (u may go a bit past 0..1 for overshoot)."""
    a, b = unit(a), unit(b)
    d = max(-1.0, min(1.0, float(np.dot(a, b))))
    w = math.acos(d)
    if w < 1e-4: return a + (b - a) * u
    if w > math.pi - 1e-3:  # straight opposite: swing over the top
        b = unit(b + Y * 1e-2)
        d = float(np.dot(a, b)); w = math.acos(max(-1.0, min(1.0, d)))
    return (math.sin((1 - u) * w) * a + math.sin(u * w) * b) / math.sin(w)


def arm(upper, lower):
    """An arm pose: which way the upper arm and forearm point (one 6-number value)."""
    return np.concatenate([unit(upper), unit(lower)])


def flip(v):
    """The same arm pose (or foot spot) on the other side."""
    v = np.array(v, dtype=float); v[0::3] *= -1; return v


def side(v, s):
    """v as given for the right side (s = 1), mirrored for the left (s = -1)."""
    return np.array(v, dtype=float) if s > 0 else flip(v)


def blend_arm(a, b, u):
    return np.concatenate([slerp(a[:3], b[:3], u), slerp(a[3:], b[3:], u)])


# ---------------- keys ----------------

class Track:
    """Values keyed on beats: (beat, value, ease into it). Loops every `beats`. With dirs=True the values are
    directions (arm poses) and swing along the arc between keys instead of cutting straight through.
    The ease can be a tuple of three (x, y, z) to ease each axis on its own."""

    def __init__(self, beats, keys, dirs=False):
        self.beats, self.dirs = beats, dirs
        self.keys = sorted(((b % beats, np.array(v, dtype=float), e) for b, v, e in keys), key=lambda k: k[0])
        at = [k[0] for k in self.keys]
        assert len(set(at)) == len(at), f"two keys on the same beat: {at}"

    def __call__(self, b):
        keys, n = self.keys, len(self.keys)
        b %= self.beats
        i = max((k for k in range(n) if keys[k][0] <= b), default=n - 1)
        j = (i + 1) % n
        b0, v0, _ = keys[i]
        b1, v1, ease = keys[j]
        if b0 > b: b0 -= self.beats  # before the first key: coming from the last one
        if b1 <= b0: b1 += self.beats
        x = (b - b0) / (b1 - b0)
        if self.dirs:
            u = ease(x)
            return np.concatenate([slerp(v0[k:k + 3], v1[k:k + 3], u) for k in range(0, len(v0), 3)])
        u = np.array([e(x) for e in ease]) if isinstance(ease, tuple) else ease(x)
        return v0 + (v1 - v0) * u


def window(b, start, full, end, stop, n):
    """0..1: rising from beat `start` to `full` (ease-out), holding, falling from `end` to `stop` (ease-in-out)."""
    b %= n
    if b < start or b >= stop: return 0.0
    if b < full: return OUT((b - start) / (full - start))
    if b < end: return 1.0
    return 1.0 - IN_OUT((b - end) / (stop - end))


# ---------------- putting a frame together ----------------

def two_bone(root, target, a, b, hint):
    """Directions of two bones of lengths a, b from root reaching target, the middle joint bending toward hint."""
    d = np.asarray(target, dtype=float) - root
    dist = float(np.linalg.norm(d))
    u = d / dist
    if dist >= a + b - 1e-6: return u, u  # out of reach: straight at it
    cos_a = (a * a + dist * dist - b * b) / (2 * a * dist)
    ang = math.acos(max(-1.0, min(1.0, cos_a)))
    v = np.asarray(hint, dtype=float) - np.dot(hint, u) * u
    v = v / np.linalg.norm(v)
    joint = root + a * (math.cos(ang) * u + math.sin(ang) * v)
    return (joint - root) / a, (target - joint) / b


def hips_mid(hip):
    return np.array([0.0, 1.0, 0.0]) + np.asarray(hip, dtype=float)


def body_point(rot, hip, local):
    """A spot on the body (in body space: x right, y up from the middle of the hips, z forward), in facing space."""
    return hips_mid(hip) + qrot(turn(*rot), local)


def reach(rot, hip, right, target, elbow):
    """Arm pose that puts the wrist on `target` (facing space), the elbow pointing toward `elbow` (body space)."""
    q = turn(*rot)
    s = 1 if right else -1
    shoulder = body_point(rot, hip, (s * SHOULDER_W, SHOULDER_UP, 0))
    up, low = two_bone(shoulder, target, UPPER_ARM, FOREARM, qrot(q, elbow))
    return np.concatenate([up, low])


def frame(rot, hip, foot_l, foot_r, arm_l, arm_r, head=(0, 0, 0), knees_out=0.3, knee_l=None, knee_r=None):
    """The frame for make_emotes: legs reach the feet (IK), arms as given, the head tilted (fwd, -, lean) on the body."""
    q = turn(*rot)
    mid = hips_mid(hip)
    right, fwd = qrot(q, X), qrot(q, Z)
    out = {"rot": q, "off": list(np.asarray(hip, dtype=float))}
    for sd, s, foot, hint in (("L", -1, foot_l, knee_l), ("R", 1, foot_r, knee_r)):
        joint = mid + right * HIP_W * s
        bend = fwd + right * knees_out * s if hint is None else qrot(q, hint)
        thigh, shin = two_bone(joint, foot, THIGH, SHIN, bend)
        out["legUp" + sd], out["legLow" + sd] = list(thigh), list(shin)
    out["armUpL"], out["armLowL"] = list(arm_l[:3]), list(arm_l[3:])
    out["armUpR"], out["armLowR"] = list(arm_r[:3]), list(arm_r[3:])
    out["head"] = list(qrot(q, qrot(turn(*head), Y)))
    return out


def keyed(beat, make, **extra):
    """A dance made of Tracks: make() gives (beats, {frame argument: Track})."""
    n, tracks = make()

    def dance(t):
        if t == "period": return beat * n
        b = t / beat
        return frame(**{k: tr(b) for k, tr in tracks.items()}, **extra)
    return dance


def tracks(n, feet, arms, hip, rot, head):
    return n, dict(foot_r=Track(n, feet[1]), foot_l=Track(n, feet[-1]), hip=Track(n, hip), rot=Track(n, rot),
                   head=Track(n, head), arm_r=Track(n, arms[1], dirs=True), arm_l=Track(n, arms[-1], dirs=True))


# ---------------- arm shapes (right arm; side() / flip() for the left) ----------------

GUARD = arm((0.3, -0.82, 0.38), (-0.28, 0.42, 0.86))       # fist in front of the chest, elbow down


# ---------------- Floss ----------------

FLOSS_BEAT = 13 / FPS  # one swing


def floss(t):
    if t == "period": return FLOSS_BEAT * 4
    ph = 2 * math.pi * t / (FLOSS_BEAT * 2)
    s = math.sin(ph)
    swing = s * (1.25 - 0.25 * s * s)                  # a touch quicker through the middle, a beat of hang at each side
    front = 0.55 if math.sin(ph / 2) >= 0 else -0.55   # which arm passes in front swaps every swing
    z = front * abs(swing)
    arm_l = arm((0.78 * swing, -1, z + 0.05), (0.8 * swing, -1, z + 0.05))
    arm_r = arm((0.78 * swing, -1, -z + 0.05), (0.8 * swing, -1, -z + 0.05))
    hip = (-0.1 * swing, -0.05 - 0.025 * abs(s), 0)    # hips the other way, a little dip each swing
    rot = (0, 14 * swing, 6 * swing)
    return frame(rot, hip, (-0.15, 0, 0), (0.15, 0, 0), arm_l, arm_r, head=(4, 0, -6 * swing))


# ---------------- Worm (lying down: posed directly) ----------------

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


# ---------------- Default Dance ----------------
# Four knee pumps (knee up, the other fist punching up across, landing on the half beat), then four big arm
# swings side to side, the hips pushing through at the bottom of each swing.

DEFAULT_BEAT = 15 / FPS  # 120 bpm


def _default():
    n = 8
    st = {1: (0.16, 0, 0), -1: (-0.16, 0, 0)}
    knee = (0.12, 0.42, 0.22)                                   # right knee up (where the foot goes)
    punch = arm((-0.15, 0.75, 0.65), (-0.3, 0.9, 0.3))          # right fist punched up, across toward the left
    pull = arm((0.25, -0.8, -0.45), (0.1, -0.35, 0.95))         # right elbow pulled back, fist at the hip
    swung = arm((0.85, -0.35, 0.35), (0.9, -0.2, 0.35))         # both arms swung right: the right one out...
    across = arm((0.55, -0.6, 0.6), (0.75, -0.35, 0.55))        # ...the other one across in front
    bottom = arm((0.15, -0.95, 0.3), (0.05, -0.85, 0.5))        # swinging through the bottom
    feet, arms = {1: [], -1: []}, {1: [], -1: []}
    hip, rot, head = [], [], []
    for b in range(4):
        s = 1 if b % 2 == 0 else -1  # this beat's knee; the other fist punches
        feet[s] += [(b, side(knee, s), HIT), (b + 0.5, st[s], LAND)]
        feet[-s] += [(b, st[-s], HOLD), (b + 0.5, st[-s], HOLD)]
        arms[-s] += [(b, side(punch, -s), HIT), (b + 0.5, side(GUARD, -s), IN_OUT)]
        arms[s] += [(b, side(pull, s), HIT), (b + 0.5, side(GUARD, s), IN_OUT)]
        hip += [(b, (-0.05 * s, -0.03, 0), HIT), (b + 0.5, (0, -0.1, 0), IN)]
        rot += [(b, (-4, 12 * s, -4 * s), HIT), (b + 0.5, (6, 0, 0), IN)]
        head += [(b, (-8, 0, 3 * s), HIT), (b + 0.5, (10, 0, 0), IN)]
    for b in range(4, 8):
        s = 1 if b % 2 == 0 else -1  # swinging right on even beats
        for f in (1, -1): feet[f] += [(b, st[f], HOLD), (b + 0.5, st[f], HOLD)]
        arms[s] += [(b, side(swung, s), HIT), (b + 0.5, side(bottom, s), IN)]
        arms[-s] += [(b, side(across, s), HIT), (b + 0.5, side(bottom, -s), IN)]
        hip += [(b, (-0.06 * s, -0.04, -0.01), HIT), (b + 0.5, (0, -0.11, 0.04), IN)]
        rot += [(b, (2, 14 * s, 6 * s), HIT), (b + 0.5, (8, 0, 0), IN)]
        head += [(b, (0, 0, 8 * s), HIT), (b + 0.5, (8, 0, 0), IN)]
    return tracks(n, feet, arms, hip, rot, head)


default_dance = keyed(DEFAULT_BEAT, _default)


# ---------------- Take the L ----------------
# The L on the forehead the whole time; hopping on the right foot, the left leg flicking out to the side on
# every beat with the free arm swinging out with it, slowly turning side to side.

L_BEAT = 13 / FPS  # ~138 bpm


def _take_the_l():
    n = 8
    on, off = range(n), [b + 0.5 for b in range(n)]
    foot_r = Track(n, [(b, (0.1, 0, 0), LAND) for b in on] + [(b, (0.1, 0.07, 0), LIFT) for b in off])
    foot_l = Track(n, [(b, (-0.62, 0.3, 0.08), HIT) for b in on] + [(b, (-0.1, 0.14, 0.06), IN_OUT) for b in off])
    hip = Track(n, [(b, (0.06, -0.07, 0), IN) for b in on] + [(b, (0.05, 0.02, 0), OUT) for b in off])
    lean = Track(n, [(b, (0, 0, 10), HIT) for b in on] + [(b, (0, 0, 3), IN_OUT) for b in off])
    arm_l = Track(n, [(b, arm((-0.85, -0.05, 0.3), (-0.75, 0.3, 0.45)), HIT) for b in on] +
                  [(b, arm((-0.3, -0.9, 0.25), (-0.1, -0.55, 0.8)), IN_OUT) for b in off], dirs=True)
    head = Track(n, [(b, (-6, 0, 6), HIT) for b in on] + [(b, (2, 0, 2), IN_OUT) for b in off])
    return n, foot_r, foot_l, hip, lean, arm_l, head


def take_the_l(t, _k=_take_the_l()):
    n, foot_r, foot_l, hip_t, lean, arm_l, head = _k
    if t == "period": return L_BEAT * n
    b = t / L_BEAT
    rot = lean(b) + np.array([-3, 22 * math.sin(2 * math.pi * b / n), 0])
    hip = hip_t(b)
    forehead = body_point(rot, hip, (0.08, HEAD_UP + 0.06, 0.13))  # wrist at the right temple, the L over the forehead
    arm_r = reach(rot, hip, True, forehead, (1, 0.5, -0.1))
    return frame(rot, hip, foot_l(b), foot_r(b), arm_l(b), arm_r, head=head(b))


# ---------------- Orange Justice ----------------
# Knees bent and bouncy: arms criss-crossing low in front on every beat while a leg flicks out to the side,
# then a shrug and a clap over the head.

OJ_BEAT = 14 / FPS  # ~129 bpm


def _orange_justice():
    n = 8
    st = {1: (0.15, 0, 0), -1: (-0.15, 0, 0)}
    kick = (0.55, 0.1, 0.06)                                    # right foot flicked out to the side
    under = arm((-0.35, -0.85, 0.35), (-0.5, -0.75, 0.42))      # right arm swung low across to the left...
    over = arm((0.4, -0.78, 0.45), (0.6, -0.55, 0.58))          # ...the other one across to the right, over it
    out_low = arm((0.55, -0.8, 0.25), (0.6, -0.7, 0.38))        # swung back out to the side, low
    shrug = arm((0.4, -0.9, 0.1), (0.85, 0.25, 0.45))           # elbows in, hands out, palms up
    rising = arm((0.7, 0.6, 0.2), (0.5, 0.85, 0.1))
    clap_rot, clap_hip = (-6, 0, 0), (0, -0.01, 0)
    top = body_point(clap_rot, clap_hip, (0, HEAD_UP + 0.45, 0.1))
    clap = {1: reach(clap_rot, clap_hip, True, top + X * 0.03, (1, 0.3, -0.1)),
            -1: reach(clap_rot, clap_hip, False, top - X * 0.03, (-1, 0.3, -0.1))}
    feet, arms = {1: [], -1: []}, {1: [], -1: []}
    hip, rot, head = [], [], []
    for b in range(6):
        s = 1 if b % 2 == 0 else -1  # this beat's kicking foot
        feet[s] += [(b, side(kick, s), HIT), (b + 0.5, st[s], LAND)]
        feet[-s] += [(b, st[-s], HOLD), (b + 0.5, st[-s], HOLD)]
        arms[s] += [(b, side(under, s), HIT), (b + 0.5, side(out_low, s), IN_OUT)]
        arms[-s] += [(b, side(over, s), HIT), (b + 0.5, side(out_low, -s), IN_OUT)]
        hip += [(b, (-0.05 * s, -0.13, 0.02), HIT), (b + 0.5, (0, -0.07, 0), IN_OUT)]
        rot += [(b, (8, -8 * s, -12 * s), HIT), (b + 0.5, (2, 0, 0), IN_OUT)]
        head += [(b, (6, 0, -6 * s), HIT), (b + 0.5, (0, 0, 0), IN_OUT)]
    for f in (1, -1):
        feet[f] += [(b, st[f], HOLD) for b in (6, 6.5, 7, 7.5)]
        arms[f] += [(6, side(shrug, f), HIT), (6.5, side(rising, f), IN_OUT), (7, clap[f], OUT), (7.5, side(out_low, f), IN)]
    hip += [(6, (0, -0.04, 0), HIT), (6.5, (0, -0.07, 0), IN_OUT), (7, clap_hip, OUT), (7.5, (0, -0.1, 0), IN)]
    rot += [(6, (-4, 0, 0), HIT), (6.5, (2, 0, 0), IN_OUT), (7, clap_rot, OUT), (7.5, (6, 0, 0), IN)]
    head += [(6, (0, 0, 12), HIT), (6.5, (-4, 0, 0), IN_OUT), (7, (-14, 0, 0), OUT), (7.5, (6, 0, 0), IN)]
    return tracks(n, feet, arms, hip, rot, head)


orange_justice = keyed(OJ_BEAT, _orange_justice, knee_r=(0.6, 0.2, 1), knee_l=(-0.6, 0.2, 1))


# ---------------- Electro Shuffle ----------------
# Four beats kicking out to the right with the right arm punching out (low, then high), four to the left.

ES_BEAT = 13 / FPS


def _electro_shuffle():
    n = 8
    out_low = arm((0.9, -0.3, 0.25), (0.95, -0.25, 0.2))        # punched out to the side, a bit low
    out_high = arm((0.8, 0.35, 0.3), (0.75, 0.55, 0.25))        # punched out and up
    across = arm((-0.5, -0.45, 0.55), (0.8, 0.1, 0.6))          # the other forearm across the chest (left arm, punching right)
    tuck = arm((0.55, -0.55, 0.45), (-0.25, 0.25, 0.95))        # both fists back in at the chest
    feet, arms = {1: [], -1: []}, {1: [], -1: []}
    hip, rot, head = [], [], []
    for b in range(n):
        s = 1 if b < 4 else -1  # the side kicking and punching
        feet[s] += [(b, (0.42 * s, 0.1, 0.06), HIT), (b + 0.5, (0.13 * s, 0, 0), LAND)]
        feet[-s] += [(b, (-0.13 * s, 0, 0), HOLD), (b + 0.5, (-0.13 * s, 0, 0), HOLD)]
        arms[s] += [(b, side(out_low if b % 2 == 0 else out_high, s), HIT), (b + 0.5, side(tuck, s), IN_OUT)]
        arms[-s] += [(b, side(across, s), HIT), (b + 0.5, side(tuck, -s), IN_OUT)]
        hip += [(b, (-0.03 * s, -0.09, 0), HIT), (b + 0.5, (0, -0.04, 0), IN_OUT)]
        rot += [(b, (4, 10 * s, -6 * s), HIT), (b + 0.5, (4, 4 * s, 0), IN_OUT)]
        head += [(b, (8, 0, 5 * s), HIT), (b + 0.5, (0, 0, 0), IN_OUT)]
    return tracks(n, feet, arms, hip, rot, head)


electro_shuffle = keyed(ES_BEAT, _electro_shuffle)


# ---------------- Griddy ----------------
# Skipping from foot to foot, a heel tapping out in front on every beat, arms pumping opposite, leaning in;
# the last two beats the hands go round the eyes like goggles.

GRIDDY_BEAT = 11 / FPS  # ~164 bpm


def _griddy():
    n = 8
    x = 0.1
    fwd_arm = arm((0.12, -0.45, 0.88), (-0.02, 0.5, 0.86))      # this arm pumps forward, fist up
    back_arm = arm((0.2, -0.5, -0.85), (0.12, -0.7, -0.7))      # this one swings back
    pass_arm = arm((0.2, -0.95, 0.0), (0.15, -0.9, 0.3))        # passing the side
    feet, arms = {1: [], -1: []}, {1: [], -1: []}
    hip, rot, head = [], [], []
    for b in range(n):
        s = 1 if b % 2 == 0 else -1  # the heel tapping this beat: right on even beats
        feet[s] += [(b, (x * s, 0, 0.34), LAND), (b + 0.25, (x * s, 0.08, 0.2), LIFT), (b + 0.5, (x * s, 0, 0), LAND)]
        # the other foot holds us up, pushes off, and lifts its knee for the next tap
        feet[-s] += [(b, (-x * s, 0, 0), HOLD), (b + 0.25, (-x * s, 0.03, -0.02), LIFT), (b + 0.5, (-x * s, 0.17, 0.12), OUT)]
        goggles = b >= 6
        hip += [(b, (-0.03 * s, -0.09, -0.04), HIT), (b + 0.3, (0, -0.02, 0), OUT), (b + 0.5, (0, -0.06, -0.01), IN)]
        rot += [(b, (6 if goggles else 15, -9 * s, 0), HIT), (b + 0.5, (5 if goggles else 10, 0, 0), IN_OUT)]
        head += [(b, (-8 if goggles else 6, 0, 4 * s), HIT), (b + 0.5, (-4 if goggles else -2, 0, 0), IN_OUT)]
        # tapping the right heel: the left arm pumps forward, the right swings back (walking, but big)
        if not goggles:
            arms[s] += [(b, side(back_arm, s), HIT), (b + 0.5, side(pass_arm, s), IN)]
            arms[-s] += [(b, side(fwd_arm, -s), HIT), (b + 0.5, side(pass_arm, -s), IN)]
    for f in (1, -1):
        arms[f] += [(6, side(GUARD, f), OUT), (7.5, side(GUARD, f), HOLD)]  # (the goggles take over in between)
    return tracks(n, feet, arms, hip, rot, head)


def griddy(t, _k=_griddy()):
    n, k = _k
    if t == "period": return GRIDDY_BEAT * n
    b = t / GRIDDY_BEAT
    v = {name: tr(b) for name, tr in k.items()}
    g = window(b, 5.5, 6, 7.6, 8, n)
    if g > 0:  # hands round the eyes, worked out every frame so they stay on the face while the body bobs
        for s, key in ((1, "arm_r"), (-1, "arm_l")):
            eye = body_point(v["rot"], v["hip"], (0.07 * s, HEAD_UP - 0.06, 0.13))  # wrist under the eye, fingers ring it
            v[key] = blend_arm(v[key], reach(v["rot"], v["hip"], s > 0, eye, (s, -0.3, -0.1)), g)
    return frame(**v)


# ---------------- Hype ----------------
# The Shoot: weight back on one foot, the other knee cocked up with that fist raised, then the leg kicks out
# straight and the fist pounds down as the standing foot hops and lands. One shot a beat, four a side.

HYPE_BEAT = 12 / FPS  # 150 bpm


def _hype():
    n = 8
    cocked = arm((0.35, 0.1, 0.93), (0.05, 0.97, 0.25))         # right fist up by the head, elbow out front
    pound = arm((0.15, -0.25, 0.96), (0.1, -0.5, 0.86))         # ...pounded down in front
    back = arm((0.25, -0.85, -0.45), (0.15, -0.55, 0.45))       # the free arm swinging back
    feet, arms = {1: [], -1: []}, {1: [], -1: []}
    hip, rot, head = [], [], []
    for b in range(n):
        s = 1 if b < 4 else -1      # the shooting side
        last = b % 4 == 3           # swap sides after this one
        feet[s] += [(b, (0.1 * s, 0.2, 0.75), HIT)]          # kicked out straight
        feet[-s] += [(b, (-0.12 * s, 0, 0), LAND)]          # hopped and landed
        arms[s] += [(b, side(pound, s), HIT)]
        arms[-s] += [(b, side(back, -s), HIT)]
        if not last:
            feet[s] += [(b + 0.5, (0.11 * s, 0.38, 0.3), IN_OUT)]                                # knee cocked again
            feet[-s] += [(b + 0.5, (-0.12 * s, 0, 0), HOLD), (b + 0.75, (-0.12 * s, 0.06, 0), LIFT)]  # hop
            arms[s] += [(b + 0.5, side(cocked, s), IN_OUT)]
            arms[-s] += [(b + 0.5, side(GUARD, -s), IN_OUT)]
            rot += [(b, (-4, -16 * s, 0), HIT), (b + 0.5, (-10, -18 * s, 0), IN_OUT)]
        else:  # the kicking foot comes down to stand on, the other knee comes up
            feet[s] += [(b + 0.5, (0.12 * s, 0, 0), LAND), (b + 0.75, (0.12 * s, 0.06, 0), LIFT)]
            feet[-s] += [(b + 0.5, (-0.11 * s, 0.38, 0.3), OUT)]
            arms[s] += [(b + 0.5, side(GUARD, s), IN_OUT)]
            arms[-s] += [(b + 0.5, side(cocked, -s), IN_OUT)]
            rot += [(b, (-4, -16 * s, 0), HIT), (b + 0.5, (-10, 0, 0), IN_OUT)]
        hip += [(b, (-0.05 * s, -0.08, -0.02), IN), (b + 0.5, (-0.04 * s, -0.04, -0.04), OUT),
                (b + 0.75, (-0.04 * s, -0.01, -0.03), OUT)]
        head += [(b, (8, 0, 0), HIT), (b + 0.5, (-8, 0, 0), IN_OUT)]
    return tracks(n, feet, arms, hip, rot, head)


hype = keyed(HYPE_BEAT, _hype)
