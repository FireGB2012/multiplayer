import React from "react";
import { interpolate } from "remotion";
import { glass } from "../zenith/ui";
import { easeIn, easeInOut, easeOut, prog, springy } from "../zenith/anim";
import { GOLD1, GOLD2 } from "./t3";

// Shared building blocks for the Zenith Plus ad (screen UI in 336x716 "phone px").

export const SW = 336;
export const SH = 716;

export type Rect = { x: number; y: number; w: number; h: number };
export const lerpRect = (a: Rect, b: Rect, p: number): Rect => ({
  x: interpolate(p, [0, 1], [a.x, b.x]),
  y: interpolate(p, [0, 1], [a.y, b.y]),
  w: interpolate(p, [0, 1], [a.w, b.w]),
  h: interpolate(p, [0, 1], [a.h, b.h]),
});

export const sheetBase = (top: number): React.CSSProperties => ({
  position: "absolute",
  left: 6,
  right: 6,
  top,
  height: SH + 120,
  borderRadius: 24,
  background: "linear-gradient(180deg, rgba(58,40,70,0.78), rgba(20,14,28,0.92))",
  backdropFilter: "blur(24px) saturate(160%)",
  border: "0.7px solid rgba(255,255,255,0.18)",
  boxShadow: "inset 0 1px 0 rgba(255,255,255,0.25), 0 -10px 40px rgba(0,0,0,0.4)",
  overflow: "hidden",
});

export const Handle: React.FC = () => <div style={{ width: 30, height: 3, borderRadius: 2, background: "rgba(255,255,255,0.4)", margin: "8px auto 0" }} />;

export const GoldChip: React.FC<{ style?: React.CSSProperties }> = ({ style }) => (
  <span
    style={{
      display: "inline-flex",
      alignItems: "center",
      gap: 2,
      height: 12,
      padding: "0 5px",
      borderRadius: 6,
      background: `linear-gradient(135deg, ${GOLD1}, ${GOLD2})`,
      color: "#3a2300",
      fontSize: 6,
      fontWeight: 800,
      letterSpacing: 0.6,
      ...style,
    }}
  >
    ✦ PLUS
  </span>
);

/** Hint pill that drops in at the top of the screen (free placement prompts). */
export const Pill: React.FC<{ t: number; inAt: number; outAt: number; children: React.ReactNode }> = ({ t, inAt, outAt, children }) => {
  const p = prog(t, inAt, inAt + 0.35, springy) - prog(t, outAt, outAt + 0.22, easeIn);
  if (t < inAt || p <= 0.001) return null;
  return (
    <div
      style={{
        position: "absolute",
        left: "50%",
        top: 40,
        translate: `-50% ${interpolate(p, [0, 1], [-50, 0])}px`,
        opacity: Math.min(1, p * 1.5),
        height: 28,
        padding: "0 14px",
        borderRadius: 14,
        ...glass(1.2),
        background: "rgba(36,22,44,0.6)",
        display: "flex",
        alignItems: "center",
        gap: 6,
        color: "#fff",
        fontSize: 9,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      {children}
    </div>
  );
};

export const Slider: React.FC<{ x: number; y: number; w: number; bg: string; frac: number; active?: boolean; ring: string }> = ({
  x,
  y,
  w,
  bg,
  frac,
  active,
  ring,
}) => (
  <div style={{ position: "absolute", left: x, top: y, width: w, height: 14, borderRadius: 7, background: bg }}>
    <div
      style={{
        position: "absolute",
        left: frac * w - 8,
        top: -1,
        width: 16,
        height: 16,
        borderRadius: 8,
        background: "#fff",
        boxShadow: active ? `0 0 0 3px ${ring}, 0 2px 6px rgba(0,0,0,0.4)` : "0 2px 5px rgba(0,0,0,0.4)",
        scale: active ? 1.2 : 1,
      }}
    />
  </div>
);

// ------------------------------------------------------------ finger

export type Key = [number, number, number]; // time, x, y
export type Gesture = { keys: Key[]; tap?: boolean; cut?: number };
type FingerState = { x: number; y: number; vis: number; down: number; ripple: number; cutK: number };

/** The most visible gesture at time t, so overlapping fade windows hand over smoothly. */
export function fingerAt(gestures: Gesture[], t: number): FingerState | null {
  let best: FingerState | null = null;
  for (const g of gestures) {
    const start = g.keys[0][0];
    const end = g.keys[g.keys.length - 1][0];
    if (t < start - 0.2 || t > end + 0.3) continue;
    let x = g.keys[0][1];
    let y = g.keys[0][2];
    for (let i = 1; i < g.keys.length; i++) {
      const [t0, x0, y0] = g.keys[i - 1];
      const [t1, x1, y1] = g.keys[i];
      if (t >= t0) {
        const p = prog(t, t0, t1, easeInOut);
        x = interpolate(p, [0, 1], [x0, x1]);
        y = interpolate(p, [0, 1], [y0, y1]);
      }
    }
    const vis = prog(t, start - 0.2, start - 0.05) * (1 - prog(t, end + 0.08, end + 0.3));
    const down = g.tap ? 1 - prog(t, start + 0.08, start + 0.2) : t >= start && t <= end ? 1 : 0;
    const ripple = g.tap ? prog(t, start, start + 0.45, easeOut) : 0;
    const cutK = g.cut ? 1 - prog(t, g.cut, g.cut + 0.05) : 1;
    const st = { x, y, vis: vis * cutK, down, ripple, cutK };
    if (!best || st.vis >= best.vis) best = st;
  }
  return best;
}

/** Touch indicator that reads on both white icons and dark glass. */
export const Finger: React.FC<{ t: number; gestures: Gesture[] }> = ({ t, gestures }) => {
  const f = fingerAt(gestures, t);
  if (!f || f.vis <= 0) return null;
  return (
    <>
      {f.ripple > 0 && f.ripple < 1 ? (
        <div
          style={{
            position: "absolute",
            left: f.x - 26 * f.ripple,
            top: f.y - 26 * f.ripple,
            width: 52 * f.ripple,
            height: 52 * f.ripple,
            borderRadius: "50%",
            border: "2px solid rgba(255,255,255,0.9)",
            boxShadow: "0 0 0 1px rgba(0,0,0,0.3)",
            opacity: (1 - f.ripple) * f.cutK,
          }}
        />
      ) : null}
      <div
        style={{
          position: "absolute",
          left: f.x - 11,
          top: f.y - 11,
          width: 22,
          height: 22,
          borderRadius: 11,
          background: "rgba(20,20,32,0.3)",
          border: "2px solid rgba(255,255,255,0.95)",
          boxShadow: "0 0 0 1px rgba(0,0,0,0.35), 0 2px 10px rgba(0,0,0,0.4)",
          opacity: f.vis,
          scale: 1 - f.down * 0.15,
        }}
      />
    </>
  );
};

// ------------------------------------------------------------ glyphs

const stroke = { fill: "none", stroke: "#fff", strokeWidth: 2, strokeLinecap: "round", strokeLinejoin: "round" } as const;

export const Glyph = {
  torch: (s: number) => (
    <svg width={s} height={s} viewBox="0 0 24 24">
      <path {...stroke} d="M8 3h8v4l-2 3v10h-4V10L8 7z" />
      <path {...stroke} d="M12 13v2" />
    </svg>
  ),
  wifi: (s: number) => (
    <svg width={s} height={s} viewBox="0 0 24 24">
      <path {...stroke} d="M2 9a15 15 0 0 1 20 0M5.5 12.5a10 10 0 0 1 13 0M9 16a5 5 0 0 1 6 0" />
      <circle cx="12" cy="19" r="1.3" fill="#fff" />
    </svg>
  ),
  bluetooth: (s: number) => (
    <svg width={s} height={s} viewBox="0 0 24 24">
      <path {...stroke} d="M7 7l10 10-5 4V3l5 4L7 17" />
    </svg>
  ),
  dnd: (s: number) => (
    <svg width={s} height={s} viewBox="0 0 24 24">
      <circle cx="12" cy="12" r="9" fill="#fff" />
      <rect x="7" y="10.8" width="10" height="2.4" rx="1.2" fill="#2a1a30" />
    </svg>
  ),
  save: (s: number) => (
    <svg width={s} height={s} viewBox="0 0 24 24">
      <path fill="#fff" d="M5 3h11l3 3v13a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2zm2 2v4h8V5zm5 8a3 3 0 1 0 0 6 3 3 0 0 0 0-6z" />
    </svg>
  ),
  history: (s: number) => (
    <svg width={s} height={s} viewBox="0 0 24 24">
      <path {...stroke} d="M4 12a8 8 0 1 0 2.5-5.8M4 4v4h4" />
      <path {...stroke} d="M12 8v4l3 2" />
    </svg>
  ),
  cloudUp: (s: number, color = "#fff") => (
    <svg width={s} height={s} viewBox="0 0 24 24">
      <path fill={color} d="M7 19a5 5 0 0 1-.7-9.95A6 6 0 0 1 17.8 8.1 4.5 4.5 0 0 1 17.5 19z" />
      <path fill="none" stroke="#2a1a30" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" d="M12 16v-5M9.5 13l2.5-2.5 2.5 2.5" />
    </svg>
  ),
  cloudDown: (s: number, color = "#fff") => (
    <svg width={s} height={s} viewBox="0 0 24 24">
      <path fill={color} d="M7 19a5 5 0 0 1-.7-9.95A6 6 0 0 1 17.8 8.1 4.5 4.5 0 0 1 17.5 19z" />
      <path fill="none" stroke="#2a1a30" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" d="M12 10.5v5M9.5 13.5l2.5 2.5 2.5-2.5" />
    </svg>
  ),
  power: (s: number, color = "#ff6b6b") => (
    <svg width={s} height={s} viewBox="0 0 24 24">
      <path fill="none" stroke={color} strokeWidth={2.2} strokeLinecap="round" d="M12 3v8M6.3 6.6a8 8 0 1 0 11.4 0" />
    </svg>
  ),
  sun: (s: number) => (
    <svg width={s} height={s} viewBox="0 0 24 24">
      <circle cx="12" cy="12" r="4" fill="#fff" />
      <path {...stroke} d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
    </svg>
  ),
};
