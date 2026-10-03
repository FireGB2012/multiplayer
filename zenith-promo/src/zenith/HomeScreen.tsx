import React from "react";
import { interpolate, random } from "remotion";
import { CameraGlyph, ChromeGlyph, GmailM, GoogleG, MessagesGlyph, PhoneGlyph, SearchGlyph, StatusIcons } from "./AppIcons";
import { glass } from "./ui";
import { prog, springy } from "./anim";

export const SCREEN_W = 336;
export const SCREEN_H = 716;

export type Palette = { accent: string; a: string; b: string; base: string };

export const Wallpaper: React.FC<{ pal: Palette; t: number }> = ({ pal, t }) => (
  <div style={{ position: "absolute", inset: 0, background: pal.base, overflow: "hidden" }}>
    <div
      style={{
        position: "absolute",
        left: -60 + Math.sin(t * 0.4) * 6,
        top: 40,
        width: 460,
        height: 230,
        borderRadius: "50%",
        background: pal.a,
        filter: "blur(38px)",
        opacity: 0.85,
      }}
    />
    <div
      style={{
        position: "absolute",
        left: -40,
        top: 190 + Math.cos(t * 0.35) * 5,
        width: 440,
        height: 150,
        borderRadius: "50%",
        background: pal.b,
        filter: "blur(34px)",
        opacity: 0.8,
        rotate: "-8deg",
      }}
    />
    <div
      style={{
        position: "absolute",
        inset: 0,
        background: `linear-gradient(180deg, transparent 0%, transparent 38%, ${pal.base} 62%)`,
      }}
    />
    {new Array(46).fill(0).map((_, i) => (
      <div
        key={i}
        style={{
          position: "absolute",
          left: random(`sx${i}`) * SCREEN_W,
          top: 280 + random(`sy${i}`) * 420,
          width: 1.2,
          height: 1.2,
          borderRadius: 1,
          background: "#fff",
          opacity: 0.15 + 0.45 * Math.abs(Math.sin(t * (0.6 + random(`st${i}`)) + i)),
        }}
      />
    ))}
  </div>
);

export const StatusBar: React.FC<{ time: string }> = ({ time }) => (
  <>
    <div
      style={{
        position: "absolute",
        top: 12,
        left: 26,
        right: 22,
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        color: "#fff",
        fontSize: 10,
        fontWeight: 600,
      }}
    >
      <span>{time}</span>
      <StatusIcons />
    </div>
    <div
      style={{
        position: "absolute",
        top: 10,
        left: SCREEN_W / 2 - 7,
        width: 14,
        height: 14,
        borderRadius: 7,
        background: "#050505",
        boxShadow: "inset 0 0 0 2px #181818",
      }}
    />
  </>
);

type Rect = { x: number; y: number; w: number; h: number };
const lerpRect = (a: Rect, b: Rect, p: number): Rect => ({
  x: interpolate(p, [0, 1], [a.x, b.x]),
  y: interpolate(p, [0, 1], [a.y, b.y]),
  w: interpolate(p, [0, 1], [a.w, b.w]),
  h: interpolate(p, [0, 1], [a.h, b.h]),
});

// Layout A = the small widget + small apps; layout B = the big clock widget + resized apps.
const WIDGET_A: Rect = { x: 22, y: 64, w: 128, h: 150 };
const WIDGET_B: Rect = { x: 20, y: 64, w: 296, h: 238 };
const GOOGLE_A: Rect = { x: 104, y: 244, w: 58, h: 58 };
const GOOGLE_B: Rect = { x: 22, y: 322, w: 58, h: 132 };
const GMAIL_A: Rect = { x: 174, y: 244, w: 140, h: 58 };
const GMAIL_B: Rect = { x: 182, y: 322, w: 132, h: 132 };

/** Staggered pop-in for home screen elements. */
const popIn = (t: number, at: number): React.CSSProperties => {
  const p = prog(t, at, at + 0.7, springy);
  return {
    opacity: prog(t, at, at + 0.25),
    translate: `0px ${interpolate(p, [0, 1], [24, 0])}px`,
    scale: interpolate(p, [0, 1], [0.86, 1]),
  };
};

const Analog: React.FC<{ size: number; accent: string }> = ({ size, accent }) => (
  <svg width={size} height={size} viewBox="0 0 100 100">
    <circle cx="50" cy="50" r="48" fill="rgba(255,255,255,0.06)" stroke="rgba(255,255,255,0.18)" strokeWidth="0.8" />
    {new Array(12).fill(0).map((_, i) => {
      const a = (i / 12) * Math.PI * 2;
      return (
        <line
          key={i}
          x1={50 + Math.sin(a) * 40}
          y1={50 - Math.cos(a) * 40}
          x2={50 + Math.sin(a) * 45}
          y2={50 - Math.cos(a) * 45}
          stroke="rgba(255,255,255,0.75)"
          strokeWidth={i % 3 === 0 ? 1.8 : 1}
          strokeLinecap="round"
        />
      );
    })}
    <line x1="50" y1="50" x2="18" y2="51" stroke="#fff" strokeWidth="2.4" strokeLinecap="round" />
    <line x1="50" y1="50" x2="16" y2="47" stroke={accent} strokeWidth="1.6" strokeLinecap="round" />
    <line x1="50" y1="50" x2="66" y2="84" stroke={accent} strokeWidth="0.8" strokeLinecap="round" />
    <circle cx="50" cy="50" r="2.4" fill={accent} />
  </svg>
);

export const HomeScreen: React.FC<{
  t: number;
  pal: Palette;
  appear: number; // time the home screen pops in
  big: number; // 0..1 morph to big widget / resized apps
  shapeRadius: number; // icon corner radius as a fraction of the short side
  gridScale: number;
}> = ({ t, pal, appear, big, shapeRadius, gridScale }) => {
  const widget = lerpRect(WIDGET_A, WIDGET_B, big);
  const google = lerpRect(GOOGLE_A, GOOGLE_B, big);
  const gmail = lerpRect(GMAIL_A, GMAIL_B, big);
  const r = (rect: Rect) => Math.min(rect.w, rect.h) * shapeRadius;
  const iconSize = 48 * gridScale;

  const tile = (rect: Rect, label: string, glyph: React.ReactNode, at: number) => (
    <div style={{ position: "absolute", left: rect.x, top: rect.y, width: rect.w, ...popIn(t, at) }}>
      <div
        style={{
          width: rect.w,
          height: rect.h,
          borderRadius: r(rect),
          background: "#fff",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          boxShadow: "0 6px 16px rgba(0,0,0,0.3)",
          scale: gridScale,
        }}
      >
        {glyph}
      </div>
      <div style={{ textAlign: "center", color: "#fff", fontSize: 8, marginTop: 5, fontWeight: 500 }}>{label}</div>
    </div>
  );

  return (
    <>
      <div style={{ position: "absolute", top: 38, left: 24, color: "rgba(255,255,255,0.85)", fontSize: 9, fontWeight: 500, ...popIn(t, appear) }}>
        Gabriel
      </div>
      {/* Date / clock widget */}
      <div
        style={{
          position: "absolute",
          left: widget.x,
          top: widget.y,
          width: widget.w,
          height: widget.h,
          borderRadius: 18,
          ...glass(0.9),
          overflow: "hidden",
          ...popIn(t, appear + 0.08),
        }}
      >
        <div style={{ position: "absolute", left: 14, top: 14, opacity: 1 - big }}>
          <div style={{ fontSize: 7, letterSpacing: 1.2, color: pal.accent, fontWeight: 700 }}>SATURDAY</div>
          <div style={{ fontSize: 30, color: "#fff", fontWeight: 700, marginTop: 18, lineHeight: 1 }}>20</div>
          <div style={{ fontSize: 30, color: pal.accent, fontWeight: 500, lineHeight: 1.05 }}>42</div>
          <div style={{ fontSize: 7, color: "rgba(255,255,255,0.7)", marginTop: 22 }}>3 October</div>
        </div>
        <div style={{ position: "absolute", inset: 0, opacity: prog(big, 0.45, 1) }}>
          <div style={{ position: "absolute", left: 16, top: 16 }}>
            <div style={{ fontSize: 7, letterSpacing: 1.2, color: pal.accent, fontWeight: 700 }}>SATURDAY</div>
            <div style={{ fontSize: 11, color: "#fff", fontWeight: 700, marginTop: 3 }}>3 October</div>
          </div>
          <div style={{ position: "absolute", right: 16, top: 16, fontSize: 15, color: pal.accent, fontWeight: 600 }}>20:44</div>
          <div style={{ position: "absolute", left: 296 / 2 - 62, top: 56 }}>
            <Analog size={124} accent={pal.accent} />
          </div>
          <div style={{ position: "absolute", left: 0, right: 0, bottom: 14, textAlign: "center", fontSize: 8, color: "rgba(255,255,255,0.8)" }}>
            Good evening, Gabriel
          </div>
        </div>
      </div>

      {tile(google, "Google", <GoogleG size={26 + big * 4} />, appear + 0.18)}
      {tile(gmail, "Gmail", <GmailM size={30 + big * 40} />, appear + 0.26)}

      {/* page dots */}
      <div style={{ position: "absolute", top: 562, left: 0, right: 0, display: "flex", justifyContent: "center", gap: 5, alignItems: "center", ...popIn(t, appear + 0.3) }}>
        <div style={{ width: 16, height: 4, borderRadius: 2, background: "rgba(255,255,255,0.85)" }} />
        <div style={{ width: 9, height: 9, borderRadius: 5, background: "rgba(255,255,255,0.2)", color: "#fff", fontSize: 7, display: "flex", alignItems: "center", justifyContent: "center" }}>+</div>
      </div>

      {/* search */}
      <div
        style={{
          position: "absolute",
          top: 580,
          left: 20,
          width: 296,
          height: 34,
          borderRadius: 17,
          ...glass(0.8),
          display: "flex",
          alignItems: "center",
          padding: "0 4px 0 12px",
          gap: 8,
          ...popIn(t, appear + 0.36),
        }}
      >
        <SearchGlyph size={11} />
        <div style={{ flex: 1, fontSize: 8.5, color: "rgba(255,255,255,0.6)" }}>Search apps or the web</div>
        <div style={{ width: 26, height: 26, borderRadius: 13, background: pal.accent, display: "flex", alignItems: "center", justifyContent: "center" }}>
          <div style={{ width: 10, height: 10, borderRadius: 5, border: "1.6px solid #2a1410" }} />
        </div>
      </div>

      {/* dock */}
      <div
        style={{
          position: "absolute",
          top: 626,
          left: 16,
          width: 304,
          height: 70,
          borderRadius: 24,
          ...glass(1),
          display: "flex",
          alignItems: "center",
          justifyContent: "space-around",
          padding: "0 8px",
          ...popIn(t, appear + 0.44),
        }}
      >
        {[
          { bg: "#fff", g: <PhoneGlyph size={26} /> },
          { bg: "#fff", g: <MessagesGlyph size={28} /> },
          { bg: "#fff", g: <ChromeGlyph size={30} /> },
          { bg: "#4c8df6", g: <CameraGlyph size={28} /> },
        ].map((ic, i) => (
          <div
            key={i}
            style={{
              width: iconSize,
              height: iconSize,
              borderRadius: iconSize * shapeRadius,
              background: ic.bg,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
            }}
          >
            {ic.g}
          </div>
        ))}
      </div>
    </>
  );
};
