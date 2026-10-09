import React from "react";
import { interpolate, random } from "remotion";
import { easeIn, easeInOut, easeOut, prog } from "../zenith/anim";
import { IRIS, TT } from "./tt";

// The glass that cracks, flies apart and re-forms into the Zenith icon.

export const IMPACT_POINT = { x: 540, y: 980 };
export const ICON_CENTER = { x: 540, y: 960 };

type Pt = { x: number; y: number };

// ---------------------------------------------------------------- crack lines

const CRACKS: { d: string; len: number; delay: number }[] = (() => {
  const out: { d: string; len: number; delay: number }[] = [];
  const P = IMPACT_POINT;
  const main = 11;
  for (let i = 0; i < main; i++) {
    let a = (i / main) * Math.PI * 2 + random(`ca${i}`) * 0.35;
    const L = 480 + random(`cl${i}`) * 760;
    const segs = 7;
    let x = P.x;
    let y = P.y;
    let d = `M ${x} ${y}`;
    let len = 0;
    const pts: Pt[] = [];
    for (let s = 0; s < segs; s++) {
      a += (random(`cj${i}-${s}`) - 0.5) * 0.6;
      const sl = (L / segs) * (0.7 + random(`cs${i}-${s}`) * 0.6);
      x += Math.cos(a) * sl;
      y += Math.sin(a) * sl;
      len += sl;
      d += ` L ${x.toFixed(1)} ${y.toFixed(1)}`;
      pts.push({ x, y });
    }
    out.push({ d, len, delay: random(`cd${i}`) * 0.05 });
    // a side branch off the main crack
    const from = pts[1 + Math.floor(random(`cb${i}`) * 3)];
    let ba = a + (random(`cba${i}`) > 0.5 ? 0.9 : -0.9);
    let bx = from.x;
    let by = from.y;
    let bd = `M ${bx.toFixed(1)} ${by.toFixed(1)}`;
    let blen = 0;
    for (let s = 0; s < 3; s++) {
      ba += (random(`cbj${i}-${s}`) - 0.5) * 0.5;
      const sl = 50 + random(`cbs${i}-${s}`) * 70;
      bx += Math.cos(ba) * sl;
      by += Math.sin(ba) * sl;
      blen += sl;
      bd += ` L ${bx.toFixed(1)} ${by.toFixed(1)}`;
    }
    out.push({ d: bd, len: blen, delay: 0.05 + random(`cbd${i}`) * 0.05 });
  }
  // jagged ring around the impact
  let ring = "";
  const rn = 16;
  for (let k = 0; k <= rn; k++) {
    const a = (k / rn) * Math.PI * 2;
    const r = 95 + random(`rr${k % rn}`) * 45;
    ring += `${k === 0 ? "M" : " L"} ${(P.x + Math.cos(a) * r).toFixed(1)} ${(P.y + Math.sin(a) * r).toFixed(1)}`;
  }
  out.push({ d: ring, len: 2 * Math.PI * 118, delay: 0.02 });
  return out;
})();

export const Crack: React.FC<{ t: number }> = ({ t }) => {
  if (t < TT.crack || t > TT.explode + 0.8) return null;
  const fade = 1 - prog(t, TT.explode + 0.05, TT.explode + 0.6, easeIn);
  const flash = 1 - prog(t, TT.crack, TT.crack + 0.25, easeOut);
  return (
    <>
      <div style={{ position: "absolute", inset: 0, background: `radial-gradient(circle at ${IMPACT_POINT.x}px ${IMPACT_POINT.y}px, rgba(255,255,255,0.9), rgba(200,220,255,0.25) 35%, transparent 70%)`, opacity: flash }} />
      <svg width={1080} height={1920} style={{ position: "absolute", inset: 0, opacity: fade, filter: "drop-shadow(0 0 10px rgba(190,210,255,0.9))" }}>
        {CRACKS.map((c, i) => {
          const draw = prog(t, TT.crack + c.delay, TT.crack + c.delay + 0.12, easeOut);
          return (
            <path
              key={i}
              d={c.d}
              fill="none"
              stroke="#fff"
              strokeWidth={i % 2 ? 1.6 : 3}
              strokeLinejoin="round"
              strokeLinecap="round"
              strokeDasharray={c.len}
              strokeDashoffset={c.len * (1 - draw)}
            />
          );
        })}
      </svg>
    </>
  );
};

// ---------------------------------------------------------------- shards

type Shard = { cx: number; cy: number; pts: string; rot: number; spin: number; out: number; tint: number };

const SHARDS: Shard[] = new Array(30).fill(0).map((_, i) => {
  const a = random(`sa${i}`) * Math.PI * 2;
  const r = 60 + random(`sr${i}`) * 440;
  const s = 40 + random(`ss${i}`) * 110;
  const th = random(`st${i}`) * Math.PI * 2;
  const v = [0, 2.1 + (random(`sv1${i}`) - 0.5) * 0.6, 4.2 + (random(`sv2${i}`) - 0.5) * 0.6].map((o, k) => {
    const rr = s * (0.55 + random(`sk${i}-${k}`) * 0.45);
    return `${(Math.cos(th + o) * rr).toFixed(1)},${(Math.sin(th + o) * rr).toFixed(1)}`;
  });
  return {
    cx: IMPACT_POINT.x + Math.cos(a) * r,
    cy: IMPACT_POINT.y + Math.sin(a) * r * 1.2,
    pts: v.join(" "),
    rot: random(`sro${i}`) * 360,
    spin: (random(`ssp${i}`) - 0.5) * 140,
    out: 90 + random(`so${i}`) * 200,
    tint: Math.floor(random(`stn${i}`) * 4),
  };
});

const ShardDefs: React.FC = () => (
  <defs>
    <linearGradient id="shard-w" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stopColor="#fff" stopOpacity={0.55} />
      <stop offset="100%" stopColor="#fff" stopOpacity={0.06} />
    </linearGradient>
    {IRIS.map((c, i) => (
      <linearGradient key={c} id={`shard-${i}`} x1="0" y1="0" x2="1" y2="1">
        <stop offset="0%" stopColor={c} stopOpacity={0.6} />
        <stop offset="100%" stopColor="#fff" stopOpacity={0.08} />
      </linearGradient>
    ))}
  </defs>
);

const fillFor = (tint: number) => (tint === 3 ? "url(#shard-w)" : `url(#shard-${tint})`);

/** Shards fly out of the crack in slow motion, then spiral in to form the icon. */
export const Shards: React.FC<{ t: number }> = ({ t }) => {
  if (t < TT.crack || t > TT.icon + 0.1) return null;
  const appear = prog(t, TT.crack + 0.05, TT.crack + 0.25);
  const ex = prog(t, TT.explode, TT.converge, easeOut);
  const cv = prog(t, TT.converge, TT.icon - 0.05, easeInOut);
  const gone = prog(t, TT.icon - 0.15, TT.icon, easeIn);
  return (
    <svg width={1080} height={1920} style={{ position: "absolute", inset: 0, filter: "drop-shadow(0 0 14px rgba(190,200,255,0.55))" }}>
      <ShardDefs />
      {SHARDS.map((s, i) => {
        const dx = s.cx - IMPACT_POINT.x;
        const dy = s.cy - IMPACT_POINT.y;
        const dl = Math.hypot(dx, dy) || 1;
        const exX = s.cx + (dx / dl) * s.out * ex;
        const exY = s.cy + (dy / dl) * s.out * ex;
        // spiral in: rotate the remaining offset around the icon centre as it closes
        const offX = exX - ICON_CENTER.x;
        const offY = exY - ICON_CENTER.y;
        const ang = cv * 1.6;
        const k = 1 - cv;
        const x = ICON_CENTER.x + (offX * Math.cos(ang) - offY * Math.sin(ang)) * k;
        const y = ICON_CENTER.y + (offX * Math.sin(ang) + offY * Math.cos(ang)) * k;
        const rot = s.rot + s.spin * ex + 220 * cv;
        const sc = interpolate(cv, [0, 1], [1, 0.25]);
        return (
          <g key={i} transform={`translate(${x.toFixed(1)} ${y.toFixed(1)}) rotate(${rot.toFixed(1)}) scale(${sc.toFixed(3)})`} opacity={appear * (0.85 - 0.3 * (1 - ex) * (t < TT.explode ? 1 : 0)) * (1 - gone)}>
            <polygon points={s.pts} fill={fillFor(s.tint)} stroke="rgba(255,255,255,0.85)" strokeWidth={1.6} vectorEffect="non-scaling-stroke" strokeLinejoin="round" />
          </g>
        );
      })}
    </svg>
  );
};

/** A few shards orbiting the icon in the hero shot. */
export const OrbitShards: React.FC<{ t: number; cx: number; cy: number; vis: number }> = ({ t, cx, cy, vis }) => {
  if (vis <= 0) return null;
  return (
    <svg width={1080} height={1920} style={{ position: "absolute", inset: 0, opacity: vis, filter: "drop-shadow(0 0 10px rgba(190,200,255,0.5))" }}>
      <ShardDefs />
      {SHARDS.slice(0, 12).map((s, i) => {
        const a = (i / 12) * Math.PI * 2 + t * (0.25 + (i % 3) * 0.05);
        const rx = 330 + (i % 4) * 40;
        const ry = 120 + (i % 3) * 30;
        const x = cx + Math.cos(a) * rx;
        const y = cy + Math.sin(a) * ry;
        const depth = (Math.sin(a) + 1) / 2;
        return (
          <g key={i} transform={`translate(${x.toFixed(1)} ${y.toFixed(1)}) rotate(${(s.rot + t * 40).toFixed(1)}) scale(${(0.25 + depth * 0.2).toFixed(3)})`} opacity={0.25 + depth * 0.5}>
            <polygon points={s.pts} fill={fillFor(s.tint)} stroke="rgba(255,255,255,0.8)" strokeWidth={1.4} vectorEffect="non-scaling-stroke" />
          </g>
        );
      })}
    </svg>
  );
};
