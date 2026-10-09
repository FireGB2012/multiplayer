import React from "react";
import { interpolate, random } from "remotion";
import { CameraGlyph, ChromeGlyph, GmailM, GoogleG, MessagesGlyph, PhoneGlyph, SearchGlyph } from "../zenith/AppIcons";
import { glass } from "../zenith/ui";
import { easeIn, easeInOut, prog, springy, tw } from "../zenith/anim";
import { Glyph, SH, SW, lerpRect, type Rect } from "./kit";
import { T3, type Pal3 } from "./t3";

// ------------------------------------------------------------ layout over time

export const DATE_RECT: Rect = { x: 20, y: 64, w: 126, h: 150 };
export const QS = { x: 172, y: 64, w: 146 };
export const GOOGLE_A: Rect = { x: 24, y: 236, w: 54, h: 54 };
export const GOOGLE_B: Rect = { x: 50, y: 356, w: 54, h: 54 };
export const GMAIL_A: Rect = { x: 96, y: 236, w: 54, h: 54 };
export const GMAIL_B: Rect = { x: 240, y: 404, w: 54, h: 54 };
export const DOCK_ICON = (i: number): Rect => ({ x: 36 + i * 72, y: 635, w: 48, h: 48 });
export const ICON_RADIUS = 0.26;

/** Quick-settings widget height: shrinks to one row, then grows past its default. */
export const qsHeightAt = (t: number) => {
  let h = 150;
  if (t >= T3.resizeStart + 0.15) h = tw(t, T3.resizeStart + 0.15, T3.shrinkTo, 150, 78, easeInOut);
  if (t >= T3.shrinkTo + 0.05) h = tw(t, T3.shrinkTo + 0.05, T3.growTo, 78, 262, easeInOut);
  return h;
};
export const qsRectAt = (t: number): Rect => ({ ...QS, h: qsHeightAt(t) });
export const gmailRectAt = (t: number) => lerpRect(GMAIL_A, GMAIL_B, prog(t, T3.dropGmail, T3.dropGmail + 0.45, springy));
export const googleRectAt = (t: number) => lerpRect(GOOGLE_A, GOOGLE_B, prog(t, T3.dropGoogle, T3.dropGoogle + 0.45, springy));

/** 0 = in place, 1 = sucked up into the cloud badge (account restore). Staggered per item. */
export const CLOUD = { x: SW / 2, y: 36 };
export const restoreK = (t: number, i: number) => {
  if (t < T3.dissolve) return 0;
  if (t < T3.reassemble) return prog(t, T3.dissolve + i * 0.035, T3.dissolve + 0.32 + i * 0.035, easeIn);
  return 1 - prog(t, T3.reassemble + i * 0.06, T3.reassemble + 0.55 + i * 0.06, springy);
};
const toCloud = (t: number, i: number, r: Rect): React.CSSProperties => {
  const k = restoreK(t, i);
  if (k === 0) return {};
  const cx = r.x + r.w / 2;
  const cy = r.y + r.h / 2;
  const kk = Math.min(1, Math.max(0, k));
  return {
    translate: `${(CLOUD.x - cx) * k}px ${(CLOUD.y - cy) * k}px`,
    scale: 1 - 0.85 * k,
    opacity: 1 - kk,
  };
};

// ------------------------------------------------------------ wallpaper

/** Silk wallpaper: diagonal light streaks over a deep tint (follows the theme maker). */
export const Silk: React.FC<{ pal: Pal3; t: number }> = ({ pal, t }) => {
  const streak = (top: number, h: number, color: string, opacity: number, blur: number, phase: number, w = 620) => (
    <div
      style={{
        position: "absolute",
        left: -140 + Math.sin(t * 0.25 + phase) * 22,
        top,
        width: w,
        height: h,
        borderRadius: h,
        background: `linear-gradient(90deg, transparent, ${color} 42%, ${color} 58%, transparent)`,
        rotate: "-27deg",
        filter: `blur(${blur}px)`,
        opacity,
      }}
    />
  );
  return (
    <div style={{ position: "absolute", inset: 0, background: `linear-gradient(165deg, ${pal.glow} 0%, ${pal.base} 48%, #060308 100%)`, overflow: "hidden" }}>
      <div style={{ position: "absolute", left: -80, top: -60, width: 360, height: 300, borderRadius: "50%", background: pal.glow, filter: "blur(50px)", opacity: 0.55 }} />
      {streak(120, 26, pal.s1, 0.45, 14, 0)}
      {streak(190, 8, pal.s2, 0.7, 4, 1.2)}
      {streak(250, 40, pal.s1, 0.3, 20, 2.1)}
      {streak(330, 6, pal.s2, 0.65, 3, 3.3)}
      {streak(380, 22, pal.s1, 0.35, 12, 4.4)}
      {streak(470, 10, pal.s2, 0.5, 5, 5.2)}
      {streak(540, 34, pal.s1, 0.25, 18, 6.1)}
      {streak(620, 7, pal.s2, 0.45, 3, 7)}
      <div style={{ position: "absolute", left: -60, top: 300, width: 520, height: 120, rotate: "-27deg", background: "#000", filter: "blur(36px)", opacity: 0.35 }} />
      {new Array(24).fill(0).map((_, i) => (
        <div
          key={i}
          style={{
            position: "absolute",
            left: random(`sx3${i}`) * SW,
            top: random(`sy3${i}`) * SH,
            width: 1.2,
            height: 1.2,
            borderRadius: 1,
            background: "#fff",
            opacity: 0.15 + 0.35 * Math.abs(Math.sin(t * (0.5 + random(`st3${i}`)) + i)),
          }}
        />
      ))}
    </div>
  );
};

// ------------------------------------------------------------ widgets

const DateWidget: React.FC<{ pal: Pal3 }> = ({ pal }) => (
  <>
    <div style={{ position: "absolute", left: 14, top: 14, fontSize: 7, letterSpacing: 1.2, color: "rgba(255,255,255,0.75)", fontWeight: 700 }}>FRIDAY</div>
    <div style={{ position: "absolute", left: 14, top: 38, fontSize: 30, color: "#fff", fontWeight: 700, lineHeight: 1 }}>18</div>
    <div style={{ position: "absolute", left: 14, top: 70, fontSize: 30, color: pal.accent, fontWeight: 500, lineHeight: 1 }}>53</div>
    <div style={{ position: "absolute", left: 14, bottom: 14, fontSize: 7.5, color: "rgba(255,255,255,0.75)" }}>9 October</div>
  </>
);

const QS_ITEMS: { n: string; g: (s: number) => React.ReactNode; on?: boolean }[] = [
  { n: "Torch", g: Glyph.torch },
  { n: "Wi-Fi", g: Glyph.wifi, on: true },
  { n: "Bluetooth", g: Glyph.bluetooth },
  { n: "Do Not Disturb", g: Glyph.dnd },
];

/** Quick settings widget that re-lays itself out as it is resized. */
const QSWidget: React.FC<{ h: number; pal: Pal3 }> = ({ h, pal }) => {
  const w = QS.w;
  const compact = Math.min(1, Math.max(0, (110 - h) / 26));
  const big = Math.min(1, Math.max(0, (h - 190) / 50));
  const normal = Math.max(0, 1 - compact - big);
  const circle = (size: number, on?: boolean): React.CSSProperties => ({
    width: size,
    height: size,
    borderRadius: size / 2,
    background: on ? pal.accent : "rgba(255,255,255,0.12)",
    border: "0.6px solid rgba(255,255,255,0.18)",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  });
  return (
    <>
      {/* one row */}
      <div style={{ position: "absolute", inset: 0, display: "flex", alignItems: "center", justifyContent: "space-evenly", opacity: compact }}>
        {QS_ITEMS.map((q) => (
          <div key={q.n} style={circle(26, q.on)}>
            {q.g(12)}
          </div>
        ))}
      </div>
      {/* 2x2 with labels (default) */}
      <div style={{ position: "absolute", inset: 0, opacity: normal }}>
        {QS_ITEMS.map((q, i) => (
          <div
            key={q.n}
            style={{
              position: "absolute",
              left: (i % 2) * (w / 2),
              top: Math.floor(i / 2) * (h / 2) + h * 0.06,
              width: w / 2,
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
            }}
          >
            <div style={circle(30, q.on)}>{q.g(14)}</div>
            <div style={{ color: "rgba(255,255,255,0.85)", fontSize: 6.5, marginTop: 5, textAlign: "center", width: 56, lineHeight: 1.2 }}>{q.n}</div>
          </div>
        ))}
      </div>
      {/* big: larger buttons + brightness */}
      <div style={{ position: "absolute", inset: 0, opacity: big }}>
        {QS_ITEMS.map((q, i) => (
          <div
            key={q.n}
            style={{
              position: "absolute",
              left: (i % 2) * (w / 2),
              top: 16 + Math.floor(i / 2) * 84,
              width: w / 2,
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
            }}
          >
            <div style={circle(42, q.on)}>{q.g(19)}</div>
            <div style={{ color: "rgba(255,255,255,0.9)", fontSize: 7.5, marginTop: 6, textAlign: "center", width: 64, lineHeight: 1.2 }}>{q.n}</div>
          </div>
        ))}
        <div style={{ position: "absolute", left: 14, right: 14, bottom: 16, height: 24, borderRadius: 12, background: "rgba(255,255,255,0.1)", overflow: "hidden" }}>
          <div style={{ position: "absolute", left: 0, top: 0, bottom: 0, width: "62%", background: `linear-gradient(90deg, ${pal.accent}66, ${pal.accent}cc)` }} />
          <div style={{ position: "absolute", left: 8, top: 5 }}>{Glyph.sun(14)}</div>
        </div>
      </div>
    </>
  );
};

// ------------------------------------------------------------ home screen

export const HomeScreen3: React.FC<{ t: number; pal: Pal3 }> = ({ t, pal }) => {
  const appear = T3.homeAppear;
  const pop = (at: number): React.CSSProperties => {
    const p = prog(t, at, at + 0.7, springy);
    return { opacity: prog(t, at, at + 0.2), translate: `0px ${interpolate(p, [0, 1], [26, 0])}px`, scale: interpolate(p, [0, 1], [0.86, 1]) };
  };
  // the cloud-restore transform takes over from the pop-in once both are done
  const place = (at: number, i: number, r: Rect): React.CSSProperties => (t < T3.dissolve ? pop(at) : toCloud(t, i, r));
  const qs = qsRectAt(t);
  const google = googleRectAt(t);
  const gmail = gmailRectAt(t);
  const SEARCH: Rect = { x: 20, y: 576, w: 296, h: 34 };
  const DOCK: Rect = { x: 16, y: 624, w: 304, h: 70 };

  const tile = (r: Rect, label: string, glyph: React.ReactNode, style: React.CSSProperties) => (
    <div style={{ position: "absolute", left: r.x, top: r.y, width: r.w, ...style }}>
      <div
        style={{
          width: r.w,
          height: r.h,
          borderRadius: r.w * ICON_RADIUS,
          background: "#fff",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          boxShadow: "0 6px 16px rgba(0,0,0,0.3)",
        }}
      >
        {glyph}
      </div>
      <div style={{ textAlign: "center", color: "#fff", fontSize: 7.5, marginTop: 5, fontWeight: 600, width: r.w + 20, marginLeft: -10 }}>{label}</div>
    </div>
  );

  return (
    <>
      <div style={{ position: "absolute", top: 36, left: 24, color: "rgba(255,255,255,0.85)", fontSize: 9, fontWeight: 600, ...place(appear, 0, { x: 24, y: 36, w: 40, h: 12 }) }}>
        Gabriel
      </div>
      <div style={{ position: "absolute", left: DATE_RECT.x, top: DATE_RECT.y, width: DATE_RECT.w, height: DATE_RECT.h, borderRadius: 18, ...glass(0.9), ...place(appear + 0.06, 1, DATE_RECT) }}>
        <DateWidget pal={pal} />
      </div>
      <div style={{ position: "absolute", left: qs.x, top: qs.y, width: qs.w, height: qs.h, borderRadius: 18, ...glass(0.9), overflow: "hidden", ...place(appear + 0.12, 2, qs) }}>
        <QSWidget h={qs.h} pal={pal} />
      </div>
      {tile(google, "Google", <GoogleG size={28} />, place(appear + 0.18, 3, google))}
      {tile(gmail, "Gmail", <GmailM size={32} />, place(appear + 0.22, 4, gmail))}

      <div style={{ position: "absolute", top: 556, left: 0, right: 0, display: "flex", justifyContent: "center", gap: 5, alignItems: "center", ...place(appear + 0.26, 5, { x: 150, y: 556, w: 36, h: 9 }) }}>
        <div style={{ width: 16, height: 4, borderRadius: 2, background: "rgba(255,255,255,0.85)" }} />
        <div style={{ width: 9, height: 9, borderRadius: 5, background: "rgba(255,255,255,0.2)", color: "#fff", fontSize: 7, display: "flex", alignItems: "center", justifyContent: "center" }}>+</div>
      </div>
      <div
        style={{
          position: "absolute",
          top: SEARCH.y,
          left: SEARCH.x,
          width: SEARCH.w,
          height: SEARCH.h,
          borderRadius: 17,
          ...glass(0.8),
          display: "flex",
          alignItems: "center",
          padding: "0 4px 0 12px",
          gap: 8,
          ...place(appear + 0.3, 6, SEARCH),
        }}
      >
        <SearchGlyph size={11} />
        <div style={{ flex: 1, fontSize: 8.5, color: "rgba(255,255,255,0.65)" }}>Search apps or the web</div>
        <div style={{ width: 26, height: 26, borderRadius: 13, background: pal.accent, display: "flex", alignItems: "center", justifyContent: "center" }}>
          <div style={{ width: 10, height: 10, borderRadius: 5, border: "1.6px solid #2a1420" }} />
        </div>
      </div>
      <div style={{ position: "absolute", top: DOCK.y, left: DOCK.x, width: DOCK.w, height: DOCK.h, borderRadius: 24, ...glass(1), ...place(appear + 0.36, 7, DOCK) }} />
      {[
        { bg: "#fff", g: <PhoneGlyph size={26} /> },
        { bg: "#fff", g: <MessagesGlyph size={28} /> },
        { bg: "#fff", g: <ChromeGlyph size={30} /> },
        { bg: "#4c8df6", g: <CameraGlyph size={28} /> },
      ].map((ic, i) => {
        const r = DOCK_ICON(i);
        return (
          <div
            key={i}
            style={{
              position: "absolute",
              left: r.x,
              top: r.y,
              width: r.w,
              height: r.h,
              borderRadius: r.w * ICON_RADIUS,
              background: ic.bg,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              ...place(appear + 0.4 + i * 0.04, 8 + i, r),
            }}
          >
            {ic.g}
          </div>
        );
      })}
    </>
  );
};
