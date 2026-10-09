import React from "react";
import { interpolate, random } from "remotion";
import { CameraGlyph, ChromeGlyph, GmailM, GoogleG, MessagesGlyph, PhoneGlyph, SearchGlyph } from "../zenith/AppIcons";
import { glass } from "../zenith/ui";
import { easeInOut, easeOut, prog, springy } from "../zenith/anim";
import { DRAWER_APPS } from "./Glyphs2";
import { T2, type Pal2 } from "./theme2";

export const SW = 336;
export const SH = 716;

export type Rect = { x: number; y: number; w: number; h: number };

export const WIDGET: Rect = { x: 20, y: 60, w: 296, h: 236 };
export const GOOGLE_HOME: Rect = { x: 22, y: 314, w: 58, h: 134 };
export const GOOGLE_MID: Rect = { x: 104, y: 314, w: 58, h: 134 };
export const GMAIL_RECT: Rect = { x: 256, y: 314, w: 58, h: 134 };
export const DOCK_ICON = (i: number): Rect => ({ x: 36 + i * 72, y: 635, w: 48, h: 48 });
export const TILE_RADIUS = 0.24;

/** Where the Google tile is: moved by free placement, then resized. */
export const googleRectAt = (t: number): Rect => {
  const moved = prog(t, T2.drop, T2.drop + 0.5, springy);
  const grow = prog(t, T2.resizeStart + 0.1, T2.resizeEnd, easeInOut);
  return {
    x: interpolate(moved, [0, 1], [GOOGLE_HOME.x, GOOGLE_MID.x]),
    y: GOOGLE_HOME.y,
    w: interpolate(grow, [0, 1], [58, 132]),
    h: 134,
  };
};

/** Nebula wallpaper (purple/teal by default, follows the theme maker). */
export const Nebula: React.FC<{ pal: Pal2; t: number }> = ({ pal, t }) => {
  const cloud = (color: string, x: number, y: number, w: number, h: number, blur: number, opacity: number, phase: number) => (
    <div
      style={{
        position: "absolute",
        left: x - w / 2 + Math.sin(t * 0.3 + phase) * 8,
        top: y - h / 2 + Math.cos(t * 0.25 + phase) * 6,
        width: w,
        height: h,
        borderRadius: "50%",
        background: color,
        filter: `blur(${blur}px)`,
        opacity,
      }}
    />
  );
  return (
    <div style={{ position: "absolute", inset: 0, background: pal.base, overflow: "hidden" }}>
      {cloud(pal.n1, 230, 170, 300, 260, 38, 0.85, 0)}
      {cloud(pal.n2, 120, 300, 280, 230, 34, 0.9, 1.3)}
      {cloud(pal.n3, 260, 470, 360, 280, 46, 0.9, 2.1)}
      {cloud(pal.n2, 300, 330, 140, 120, 28, 0.6, 3)}
      {cloud(pal.n1, 60, 520, 220, 200, 40, 0.55, 4)}
      {cloud("#000", 190, 390, 200, 150, 30, 0.45, 5)}
      {cloud("#000", 90, 120, 160, 120, 30, 0.35, 6)}
      <div style={{ position: "absolute", inset: 0, background: `linear-gradient(180deg, transparent 55%, ${pal.base}cc 100%)` }} />
      {new Array(60).fill(0).map((_, i) => (
        <div
          key={i}
          style={{
            position: "absolute",
            left: random(`nx${i}`) * SW,
            top: random(`ny${i}`) * SH,
            width: random(`nz${i}`) > 0.85 ? 2 : 1.1,
            height: random(`nz${i}`) > 0.85 ? 2 : 1.1,
            borderRadius: 2,
            background: "#fff",
            opacity: 0.2 + 0.5 * Math.abs(Math.sin(t * (0.5 + random(`nt${i}`)) + i)),
          }}
        />
      ))}
    </div>
  );
};

/** A plain, lifeless stock launcher for the "most launchers just sit there" opener. */
export const StockScreen: React.FC = () => {
  const row = ["Play Store", "Photos", "YouTube", "Settings"];
  const apps = row.map((n) => DRAWER_APPS.find((a) => a.name === n)!);
  return (
    <div style={{ position: "absolute", inset: 0, background: "linear-gradient(180deg, #3a3e45, #23262b)", filter: "saturate(0.35) brightness(0.85)" }}>
      <div style={{ position: "absolute", left: 26, top: 74, color: "#e8e8e8", fontSize: 19, fontWeight: 500 }}>Sun, Oct 4</div>
      <div style={{ position: "absolute", left: 26, top: 100, color: "#bdbdbd", fontSize: 10 }}>☁ 18°C</div>
      <div style={{ position: "absolute", left: 22, right: 22, top: 470, display: "flex", justifyContent: "space-between" }}>
        {apps.map((a) => (
          <div key={a.name} style={{ width: 62, display: "flex", flexDirection: "column", alignItems: "center" }}>
            <div style={{ width: 46, height: 46, borderRadius: 23, background: "#f1f1f1", display: "flex", alignItems: "center", justifyContent: "center" }}>
              {a.glyph(30)}
            </div>
            <div style={{ color: "#ddd", fontSize: 7.5, marginTop: 5 }}>{a.name}</div>
          </div>
        ))}
      </div>
      <div style={{ position: "absolute", left: 22, right: 22, top: 570, display: "flex", justifyContent: "space-between" }}>
        {[<PhoneGlyph key="p" size={26} />, <MessagesGlyph key="m" size={28} />, <ChromeGlyph key="c" size={30} />, <CameraGlyph key="k" size={28} />].map((g, i) => (
          <div key={i} style={{ width: 62, display: "flex", justifyContent: "center" }}>
            <div style={{ width: 46, height: 46, borderRadius: 23, background: i === 3 ? "#4c8df6" : "#f1f1f1", display: "flex", alignItems: "center", justifyContent: "center" }}>
              {g}
            </div>
          </div>
        ))}
      </div>
      <div style={{ position: "absolute", left: 22, right: 22, top: 640, height: 40, borderRadius: 20, background: "#e9e9e9", display: "flex", alignItems: "center", padding: "0 14px", gap: 10 }}>
        <GoogleG size={18} />
        <div style={{ flex: 1, height: 6, borderRadius: 3, background: "#cfcfcf" }} />
      </div>
    </div>
  );
};

const Analog2: React.FC<{ size: number; pal: Pal2; t: number }> = ({ size, pal, t }) => {
  const sec = 280 + t * 6;
  return (
    <svg width={size} height={size} viewBox="0 0 100 100">
      <circle cx="50" cy="50" r="48" fill="rgba(255,255,255,0.05)" stroke="rgba(255,255,255,0.2)" strokeWidth="0.7" />
      {new Array(60).fill(0).map((_, i) => {
        const a = (i / 60) * Math.PI * 2;
        const major = i % 5 === 0;
        return (
          <line
            key={i}
            x1={50 + Math.sin(a) * (major ? 38 : 42)}
            y1={50 - Math.cos(a) * (major ? 38 : 42)}
            x2={50 + Math.sin(a) * 45}
            y2={50 - Math.cos(a) * 45}
            stroke={major ? "rgba(255,255,255,0.8)" : "rgba(255,255,255,0.3)"}
            strokeWidth={major ? 1.6 : 0.5}
            strokeLinecap="round"
          />
        );
      })}
      <line x1="50" y1="50" x2={50 + Math.sin((244.5 * Math.PI) / 180) * 24} y2={50 - Math.cos((244.5 * Math.PI) / 180) * 24} stroke="#fff" strokeWidth="2.6" strokeLinecap="round" />
      <line x1="50" y1="50" x2={50 + Math.sin(((54 + t * 0.1) * Math.PI) / 180) * 36} y2={50 - Math.cos(((54 + t * 0.1) * Math.PI) / 180) * 36} stroke={pal.accent} strokeWidth="1.8" strokeLinecap="round" />
      <line x1="50" y1="50" x2={50 + Math.sin((sec * Math.PI) / 180) * 42} y2={50 - Math.cos((sec * Math.PI) / 180) * 42} stroke={pal.accent2} strokeWidth="0.7" strokeLinecap="round" />
      <circle cx="50" cy="50" r="1.8" fill={pal.accent} />
    </svg>
  );
};

/** Tile with an app glyph; radius follows the "Rounded" icon shape. */
export const Tile: React.FC<{ rect: Rect; label: string; children: React.ReactNode; style?: React.CSSProperties }> = ({ rect, label, children, style }) => (
  <div style={{ position: "absolute", left: rect.x, top: rect.y, width: rect.w, ...style }}>
    <div
      style={{
        width: rect.w,
        height: rect.h,
        borderRadius: Math.min(rect.w, rect.h) * TILE_RADIUS,
        background: "#fff",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        boxShadow: "0 6px 16px rgba(0,0,0,0.3)",
      }}
    >
      {children}
    </div>
    <div style={{ textAlign: "center", color: "#fff", fontSize: 8, marginTop: 5, fontWeight: 600 }}>{label}</div>
  </div>
);

export const HomeScreen2: React.FC<{ t: number; pal: Pal2; appear: number }> = ({ t, pal, appear }) => {
  const pop = (at: number): React.CSSProperties => {
    const p = prog(t, at, at + 0.7, springy);
    return { opacity: prog(t, at, at + 0.2), translate: `0px ${interpolate(p, [0, 1], [26, 0])}px`, scale: interpolate(p, [0, 1], [0.86, 1]) };
  };
  const g = googleRectAt(t);
  // Tapped icons dip under the finger; white tiles dim while the liquid-glass card is open over them.
  const dip = (tapAt: number, openAt: number) => 1 - 0.07 * Math.sin(Math.PI * prog(t, tapAt - 0.05, openAt, easeInOut));
  const glassK =
    t < T2.chromeOpen || t > T2.chromeClose + 0.45
      ? 0
      : t < T2.chromeClose
        ? prog(t, T2.chromeOpen, T2.chromeOpen + 0.5, easeOut)
        : 1 - prog(t, T2.chromeClose, T2.chromeClose + 0.38, easeInOut);
  const dimmed = (st: React.CSSProperties, scaleK = 1): React.CSSProperties => ({
    ...st,
    opacity: (st.opacity as number) * (1 - 0.7 * glassK),
    scale: (st.scale as number) * scaleK,
  });
  return (
    <>
      <div style={{ position: "absolute", top: 36, left: 24, color: "rgba(255,255,255,0.88)", fontSize: 9, fontWeight: 600, ...pop(appear) }}>Gabriel</div>
      <div style={{ position: "absolute", left: WIDGET.x, top: WIDGET.y, width: WIDGET.w, height: WIDGET.h, borderRadius: 18, ...glass(0.9), overflow: "hidden", ...pop(appear + 0.06) }}>
        <div style={{ position: "absolute", left: 16, top: 16 }}>
          <div style={{ fontSize: 7, letterSpacing: 1.2, color: pal.accent, fontWeight: 700 }}>SUNDAY</div>
          <div style={{ fontSize: 11, color: "#fff", fontWeight: 700, marginTop: 3 }}>4 October</div>
        </div>
        <div style={{ position: "absolute", right: 16, top: 16, fontSize: 15, fontWeight: 600 }}>
          <span style={{ color: "rgba(255,255,255,0.85)" }}>20:</span>
          <span style={{ color: pal.accent2 }}>09</span>
        </div>
        <div style={{ position: "absolute", left: WIDGET.w / 2 - 62, top: 50 }}>
          <Analog2 size={124} pal={pal} t={t} />
        </div>
        <div style={{ position: "absolute", left: 0, right: 0, bottom: 14, textAlign: "center", fontSize: 8, color: "rgba(255,255,255,0.8)" }}>Good evening, Gabriel</div>
      </div>

      <Tile rect={g} label="Google" style={dimmed(pop(appear + 0.14))}>
        <GoogleG size={interpolate(g.w, [58, 132], [30, 66])} />
      </Tile>
      <Tile rect={GMAIL_RECT} label="Gmail" style={dimmed(pop(appear + 0.2), dip(T2.gmailTap, T2.gmailOpen))}>
        <GmailM size={34} />
      </Tile>

      <div style={{ position: "absolute", top: 558, left: 0, right: 0, display: "flex", justifyContent: "center", gap: 5, alignItems: "center", ...pop(appear + 0.26) }}>
        <div style={{ width: 16, height: 4, borderRadius: 2, background: "rgba(255,255,255,0.85)" }} />
        <div style={{ width: 9, height: 9, borderRadius: 5, background: "rgba(255,255,255,0.2)", color: "#fff", fontSize: 7, display: "flex", alignItems: "center", justifyContent: "center" }}>+</div>
      </div>

      <div style={{ position: "absolute", top: 576, left: 20, width: 296, height: 34, borderRadius: 17, ...glass(0.8), display: "flex", alignItems: "center", padding: "0 4px 0 12px", gap: 8, ...pop(appear + 0.3) }}>
        <SearchGlyph size={11} />
        <div style={{ flex: 1, fontSize: 8.5, color: "rgba(255,255,255,0.65)" }}>Search apps or the web</div>
        <div style={{ width: 26, height: 26, borderRadius: 13, background: pal.accent, display: "flex", alignItems: "center", justifyContent: "center" }}>
          <div style={{ width: 10, height: 10, borderRadius: 5, border: "1.6px solid #1a1430" }} />
        </div>
      </div>

      <div style={{ position: "absolute", top: 624, left: 16, width: 304, height: 70, borderRadius: 24, ...glass(1), ...pop(appear + 0.36) }} />
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
              borderRadius: r.w * TILE_RADIUS,
              background: ic.bg,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              ...dimmed(pop(appear + 0.4 + i * 0.04), i === 2 ? dip(T2.chromeTap, T2.chromeOpen) : 1),
            }}
          >
            {ic.g}
          </div>
        );
      })}
    </>
  );
};

