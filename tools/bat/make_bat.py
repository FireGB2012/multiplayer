#!/usr/bin/env python3
"""
Builds the Titanium Bat's in-game model + inventory icon from Gabriel's model (titanium-bat-v2.obj/.mtl).

The original has ~144k triangles (the grip wrap alone is 61k), way too heavy for a held tool that every
player can see, so each part is simplified on its own (small glowing strips keep their shape, the big
smooth parts lose the most), then smooth normals are rebuilt with a crease angle so the edges stay sharp.

Output: src/SubnauticaMP.Plugin/Bat.bin (embedded in the plugin), zlib:
  "SNBT" version(1)
  vertexCount(u32) then per vertex: position float32 x3, normal float32 x3
      model space: grip in the middle of the wrap at (0,0,0), bat pointing +Y, the tip at y = TipY
  tipY(float32) gripHalfLength(float32) radius(float32)
  submeshCount(u8) then per submesh: name, linear colour rgb float32 x3, glow(u8), smoothness(float32),
      indexCount(u32) then u16 indices (Unity triangles, clockwise front faces)
  icon: width(u16) height(u16) RGBA bytes, bottom row first (what Texture2D.LoadRawTextureData wants)

Usage: python3 tools/bat/make_bat.py [--preview <folder>]   (needs numpy, Pillow, fast_simplification)
"""
import gzip, math, os, struct, sys, zlib
import numpy as np
import fast_simplification
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
SRC_OBJ = os.path.join(HERE, "titanium-bat-v2.obj.gz")
SRC_MTL = os.path.join(HERE, "titanium-bat-v2.mtl")
OUT = os.path.join(ROOT, "src", "SubnauticaMP.Plugin", "Bat.bin")

TARGET_TRIS = 9000     # whole bat
KEEP_SMALL = 160       # parts this small are kept as they are
CREASE_DEG = 38
ICON = 256

# The order of the submeshes (= the material slots in game) and how each one looks.
MATERIALS = ["titanium_dark", "chrome_trim", "grip_rubber", "inlay_blue", "cyan_glow"]
SMOOTHNESS = {"titanium_dark": 0.75, "chrome_trim": 0.92, "grip_rubber": 0.15, "inlay_blue": 0.8, "cyan_glow": 0.5}
GLOW = {"cyan_glow"}


def read_mtl(path):
    mats, cur = {}, None
    for line in open(path):
        p = line.split()
        if not p: continue
        if p[0] == "newmtl": cur = p[1]; mats[cur] = {}
        elif p[0] == "Kd": mats[cur]["kd"] = tuple(float(x) for x in p[1:4])
    return mats


def read_obj(path):
    opener = gzip.open if path.endswith(".gz") else open
    verts, parts, cur = [], [], None
    with opener(path, "rt") as f:
        for line in f:
            if line.startswith("v "):
                verts.append([float(x) for x in line.split()[1:4]])
            elif line.startswith("o "):
                cur = {"name": line.split()[1], "mat": None, "faces": []}
                parts.append(cur)
            elif line.startswith("usemtl"):
                cur["mat"] = line.split()[1]
            elif line.startswith("f "):
                idx = [int(t.split("/")[0]) - 1 for t in line.split()[1:]]
                for k in range(1, len(idx) - 1):
                    cur["faces"].append((idx[0], idx[k], idx[k + 1]))
    return np.array(verts, np.float64), parts


def weld(points, faces):
    key = np.round(points / 1e-6).astype(np.int64)
    _, first, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
    f = inverse.reshape(-1)[faces]
    f = f[(f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])]
    return points[first], f


def to_unity(p, grip_x, axis_y, axis_z):
    # OBJ (right-handed, bat along +X) -> Unity (left-handed, bat along +Y). Swapping two axes is a mirror,
    # which is exactly the handedness change, so the triangle winding stays as it is.
    return np.stack([p[:, 1] - axis_y, p[:, 0] - grip_x, p[:, 2] - axis_z], axis=1)


def crease_normals(points, faces):
    """Split vertices where faces meet at more than CREASE_DEG; returns new points, normals, faces."""
    fn = np.cross(points[faces[:, 1]] - points[faces[:, 0]], points[faces[:, 2]] - points[faces[:, 0]])
    area = np.linalg.norm(fn, axis=1, keepdims=True)
    unit = fn / np.maximum(area, 1e-12)
    incident = [[] for _ in range(len(points))]
    for fi, tri in enumerate(faces):
        for v in tri: incident[v].append(fi)
    cos = math.cos(math.radians(CREASE_DEG))
    out_p, out_n, cache = [], [], {}
    new_faces = np.zeros_like(faces)
    for fi, tri in enumerate(faces):
        for c, v in enumerate(tri):
            group = [g for g in incident[v] if np.dot(unit[g], unit[fi]) >= cos]
            n = np.sum(fn[group], axis=0)  # area weighted
            n = n / max(np.linalg.norm(n), 1e-12)
            key = (v, tuple(np.round(n, 3)))
            if key not in cache:
                cache[key] = len(out_p)
                out_p.append(points[v]); out_n.append(n)
            new_faces[fi, c] = cache[key]
    return np.array(out_p), np.array(out_n), new_faces


def srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def render(meshes, colors, size, yaw, pitch, roll, light=(-0.45, 0.65, -0.6), fit=0.86, ss=4):
    """Tiny z-buffer rasteriser: orthographic view, lambert + spec, glowing parts unlit. Returns RGBA image."""
    S = size * ss
    def rot(a, axis):
        c, s = math.cos(a), math.sin(a)
        if axis == 0: return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
        if axis == 1: return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
        return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])
    R = rot(roll, 2) @ rot(pitch, 0) @ rot(yaw, 1)
    allp = np.concatenate([m[0] for m in meshes]) @ R.T
    lo, hi = allp[:, :2].min(0), allp[:, :2].max(0)
    scale = fit * S / (hi - lo).max()
    center = (lo + hi) / 2
    color = np.zeros((S, S, 3)); alpha = np.zeros((S, S)); zbuf = np.full((S, S), np.inf)
    L = np.array(light, float); L /= np.linalg.norm(L)
    V = np.array([0, 0, -1.0]); H = (L + V) / np.linalg.norm(L + V)
    for (pts, nrm, faces), (name, kd) in zip(meshes, colors):
        p = pts @ R.T
        n = nrm @ R.T
        sx = (p[:, 0] - center[0]) * scale + S / 2
        sy = S / 2 - (p[:, 1] - center[1]) * scale
        z = p[:, 2]
        base = np.array(kd)
        for tri in faces:
            xs, ys = sx[tri], sy[tri]
            x0, x1 = int(max(0, np.floor(xs.min()))), int(min(S - 1, np.ceil(xs.max())))
            y0, y1 = int(max(0, np.floor(ys.min()))), int(min(S - 1, np.ceil(ys.max())))
            if x1 < x0 or y1 < y0: continue
            d = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
            if abs(d) < 1e-9: continue
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
            w0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / d
            w1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / d
            w2 = 1 - w0 - w1
            inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
            if not inside.any(): continue
            zz = w0 * z[tri[0]] + w1 * z[tri[1]] + w2 * z[tri[2]]
            sub = zbuf[y0:y1 + 1, x0:x1 + 1]
            win = inside & (zz < sub)
            if not win.any(): continue
            nn = (w0[..., None] * n[tri[0]] + w1[..., None] * n[tri[1]] + w2[..., None] * n[tri[2]])
            nn /= np.maximum(np.linalg.norm(nn, axis=-1, keepdims=True), 1e-9)
            if name in GLOW:
                c = np.broadcast_to(base * 1.4, nn.shape)
            else:
                diff = np.clip(nn @ L, 0, 1)[..., None]
                sm = SMOOTHNESS[name]
                spec = np.power(np.clip(nn @ H, 0, 1), 60 * sm + 8)[..., None] * (0.05 + 0.7 * sm * sm)
                rim = np.power(1 - np.clip(-nn[..., 2], 0, 1), 3)[..., None] * 0.12
                c = base * (0.35 + 1.1 * diff) + spec + rim * np.array([0.35, 0.8, 1.0])
            sub[win] = zz[win]
            color[y0:y1 + 1, x0:x1 + 1][win] = c[win]
            alpha[y0:y1 + 1, x0:x1 + 1][win] = 1
    rgb = (srgb(color) * 255).astype(np.uint8)
    img = Image.fromarray(np.dstack([rgb, (alpha * 255).astype(np.uint8)]), "RGBA")
    return img.resize((size, size), Image.LANCZOS)


def main():
    preview = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    mtl = read_mtl(SRC_MTL)
    verts, parts = read_obj(SRC_OBJ)
    total = sum(len(p["faces"]) for p in parts)
    print(f"source: {len(parts)} parts, {total} triangles")

    # whole-bat measurements in OBJ space
    grip = [p for p in parts if p["name"] == "grip_wrap"][0]
    gv = verts[np.unique(np.array(grip["faces"]).reshape(-1))]
    grip_x = (gv[:, 0].min() + gv[:, 0].max()) / 2
    grip_half = (gv[:, 0].max() - gv[:, 0].min()) / 2
    axis_y = (verts[:, 1].min() + verts[:, 1].max()) / 2
    axis_z = (verts[:, 2].min() + verts[:, 2].max()) / 2

    big = sum(len(p["faces"]) for p in parts if len(p["faces"]) > KEEP_SMALL)
    small = total - big
    ratio = max(0.01, (TARGET_TRIS - small) / big)

    groups = {m: [] for m in MATERIALS}
    for p in parts:
        faces = np.array(p["faces"], np.int64)
        pts, f = weld(verts, faces)
        used = np.unique(f.reshape(-1)); remap = np.full(len(pts), -1); remap[used] = np.arange(len(used))
        pts, f = pts[used], remap[f]
        if len(f) > KEEP_SMALL:
            keep = max(KEEP_SMALL, int(len(f) * ratio))
            pts32, f32 = fast_simplification.simplify(pts.astype(np.float32), f.astype(np.int32), target_reduction=1 - keep / len(f))
            pts, f = pts32.astype(np.float64), f32.astype(np.int64)
        groups[p["mat"]].append((to_unity(pts, grip_x, axis_y, axis_z), f))

    meshes, colors = [], []
    for m in MATERIALS:
        pts_all, f_all, off = [], [], 0
        for pts, f in groups[m]:
            pts_all.append(pts); f_all.append(f + off); off += len(pts)
        p, n, f = crease_normals(np.concatenate(pts_all), np.concatenate(f_all))
        meshes.append((p, n, f)); colors.append((m, mtl[m]["kd"]))
        print(f"  {m:14s} {len(f):6d} tris {len(p):6d} verts")

    allp = np.concatenate([m[0] for m in meshes])
    tip_y = float(allp[:, 1].max())
    radius = float(np.sqrt(allp[:, 0] ** 2 + allp[:, 2] ** 2).max())
    print(f"tip {tip_y:.3f} m from the grip, butt {allp[:, 1].min():.3f}, radius {radius:.3f}, grip half {grip_half:.3f}")

    # icon: the bat on a diagonal, tip up-right, tilted a bit so the panels show
    icon = render(meshes, colors, ICON, yaw=math.radians(35), pitch=math.radians(-18), roll=math.radians(-45))

    out = bytearray(b"SNBT"); out += struct.pack("<B", 1)
    nverts = sum(len(m[0]) for m in meshes)
    out += struct.pack("<I", nverts)
    for p, n, _ in meshes:
        out += np.hstack([p, n]).astype("<f4").tobytes()
    out += struct.pack("<fff", tip_y, grip_half, radius)
    out += struct.pack("<B", len(meshes))
    off = 0
    for (p, n, f), (name, kd) in zip(meshes, colors):
        nb = name.encode(); out += struct.pack("<B", len(nb)) + nb
        out += struct.pack("<fffBf", *kd, 1 if name in GLOW else 0, SMOOTHNESS[name])
        idx = (f + off).reshape(-1)
        assert idx.max() < 65536
        out += struct.pack("<I", len(idx)) + idx.astype("<u2").tobytes()
        off += len(p)
    px = np.asarray(icon)[::-1].copy()  # bottom row first
    out += struct.pack("<HH", ICON, ICON) + px.tobytes()
    with open(OUT, "wb") as f:
        f.write(zlib.compress(bytes(out), 9))
    print(f"wrote {OUT} ({os.path.getsize(OUT) // 1024} KB)")

    if preview:
        os.makedirs(preview, exist_ok=True)
        icon.save(os.path.join(preview, "icon.png"))
        render(meshes, colors, 900, yaw=0, pitch=0, roll=math.radians(-90)).save(os.path.join(preview, "side.png"))
        render(meshes, colors, 900, yaw=math.radians(90), pitch=0, roll=math.radians(-90)).save(os.path.join(preview, "side90.png"))
        # the untouched original next to it, to compare
        orig = []
        for m in MATERIALS:
            fs = [np.array(p["faces"]) for p in parts if p["mat"] == m]
            pts, f = weld(verts, np.concatenate(fs))
            pts = to_unity(pts, grip_x, axis_y, axis_z)
            fn = np.cross(pts[f[:, 1]] - pts[f[:, 0]], pts[f[:, 2]] - pts[f[:, 0]])
            nrm = np.zeros_like(pts)
            for k in range(3): np.add.at(nrm, f[:, k], fn)
            nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-12)
            orig.append((pts, nrm, f))
        render(orig, colors, 900, yaw=0, pitch=0, roll=math.radians(-90)).save(os.path.join(preview, "side_original.png"))
        print("previews in", preview)


if __name__ == "__main__":
    main()
