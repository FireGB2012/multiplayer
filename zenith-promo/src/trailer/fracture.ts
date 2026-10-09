import { random } from "remotion";

// Breaks a rounded-rectangle pane into Voronoi cells, seeded in jittered rings around
// the impact point: small pieces near the hit, long wedges further out — the way
// annealed glass actually breaks.

export type V2 = [number, number];
export type Cell = { poly: V2[]; cx: number; cy: number; dist: number; area: number };

export const PANE_W = 2.9;
export const PANE_H = 6.18;
export const PANE_R = 0.34;
export const IMPACT: V2 = [0.42, 0.55];

/** Convex rounded rectangle as a polygon (counter-clockwise). */
export const roundedRect = (w: number, h: number, r: number, seg = 8): V2[] => {
  const pts: V2[] = [];
  const corners: [number, number, number][] = [
    [w / 2 - r, h / 2 - r, 0],
    [-w / 2 + r, h / 2 - r, Math.PI / 2],
    [-w / 2 + r, -h / 2 + r, Math.PI],
    [w / 2 - r, -h / 2 + r, (3 * Math.PI) / 2],
  ];
  for (const [cx, cy, a0] of corners) {
    for (let i = 0; i <= seg; i++) {
      const a = a0 + (i / seg) * (Math.PI / 2);
      pts.push([cx + Math.cos(a) * r, cy + Math.sin(a) * r]);
    }
  }
  return pts;
};

/** Keep the part of `poly` on the side of the bisector closer to `a` than to `b`. */
const clipHalfPlane = (poly: V2[], a: V2, b: V2): V2[] => {
  const mx = (a[0] + b[0]) / 2;
  const my = (a[1] + b[1]) / 2;
  const nx = b[0] - a[0];
  const ny = b[1] - a[1];
  const side = (p: V2) => (p[0] - mx) * nx + (p[1] - my) * ny; // <= 0 means keep
  const out: V2[] = [];
  for (let i = 0; i < poly.length; i++) {
    const p = poly[i];
    const q = poly[(i + 1) % poly.length];
    const sp = side(p);
    const sq = side(q);
    if (sp <= 0) out.push(p);
    if ((sp <= 0) !== (sq <= 0)) {
      const k = sp / (sp - sq);
      out.push([p[0] + (q[0] - p[0]) * k, p[1] + (q[1] - p[1]) * k]);
    }
  }
  return out;
};

const polyArea = (poly: V2[]) => {
  let a = 0;
  for (let i = 0; i < poly.length; i++) {
    const [x1, y1] = poly[i];
    const [x2, y2] = poly[(i + 1) % poly.length];
    a += x1 * y2 - x2 * y1;
  }
  return a / 2;
};

const centroid = (poly: V2[]): V2 => {
  const a = polyArea(poly);
  let cx = 0;
  let cy = 0;
  for (let i = 0; i < poly.length; i++) {
    const [x1, y1] = poly[i];
    const [x2, y2] = poly[(i + 1) % poly.length];
    const f = x1 * y2 - x2 * y1;
    cx += (x1 + x2) * f;
    cy += (y1 + y2) * f;
  }
  return [cx / (6 * a), cy / (6 * a)];
};

export const fracture = (): Cell[] => {
  const pane = roundedRect(PANE_W, PANE_H, PANE_R);
  const seeds: V2[] = [];
  const rings: [number, number][] = [
    [0.07, 5],
    [0.2, 7],
    [0.38, 9],
    [0.62, 11],
    [0.95, 12],
    [1.38, 13],
    [1.95, 13],
    [2.7, 13],
    [3.6, 13],
    [4.7, 13],
  ];
  rings.forEach(([r, n], ri) => {
    const off = random(`ro${ri}`) * Math.PI * 2;
    for (let k = 0; k < n; k++) {
      const a = off + (k / n) * Math.PI * 2 + (random(`ra${ri}-${k}`) - 0.5) * (Math.PI / n) * 0.9;
      const rr = r * (0.85 + random(`rr${ri}-${k}`) * 0.3);
      const x = IMPACT[0] + Math.cos(a) * rr;
      const y = IMPACT[1] + Math.sin(a) * rr;
      if (Math.abs(x) < PANE_W / 2 - 0.02 && Math.abs(y) < PANE_H / 2 - 0.02) seeds.push([x, y]);
    }
  });
  const cells: Cell[] = [];
  seeds.forEach((s, i) => {
    let poly = pane;
    seeds.forEach((o, j) => {
      if (i !== j && poly.length) poly = clipHalfPlane(poly, s, o);
    });
    if (poly.length < 3) return;
    const area = Math.abs(polyArea(poly));
    if (area < 1e-4) return;
    const [cx, cy] = centroid(poly);
    cells.push({ poly: poly.map(([x, y]) => [x - cx, y - cy] as V2), cx, cy, dist: Math.hypot(cx - IMPACT[0], cy - IMPACT[1]), area });
  });
  return cells;
};
