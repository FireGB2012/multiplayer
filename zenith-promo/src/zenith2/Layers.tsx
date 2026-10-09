import React from "react";
import { interpolate, interpolateColors, random } from "remotion";
import { CheckGlyph, ChromeGlyph, GmailM, GoogleG, LockGlyph, SearchGlyph } from "../zenith/AppIcons";
import { Toggle, glass } from "../zenith/ui";
import { easeIn, easeInOut, easeOut, prog, softSpring, springy, tw } from "../zenith/anim";
import { DRAWER_APPS } from "./Glyphs2";
import { DOCK_ICON, GMAIL_RECT, SH, SW, TILE_RADIUS, googleRectAt, type Rect } from "./Home2";
import { T2, hsv, sliders, type Pal2 } from "./theme2";

const lerpRect = (a: Rect, b: Rect, p: number): Rect => ({
  x: interpolate(p, [0, 1], [a.x, b.x]),
  y: interpolate(p, [0, 1], [a.y, b.y]),
  w: interpolate(p, [0, 1], [a.w, b.w]),
  h: interpolate(p, [0, 1], [a.h, b.h]),
});
const FULL: Rect = { x: 0, y: 0, w: SW, h: SH };
const openSnap = (t: number, openAt: number, closeAt: number) =>
  t < closeAt ? prog(t, openAt, openAt + 0.5, easeOut) : 1 - prog(t, closeAt, closeAt + 0.38, easeInOut);

// ------------------------------------------------------------ app opening

/** Skeleton rows that suggest app content without copying a real app's UI. */
const Skeleton: React.FC<{ tone: string; avatar?: boolean }> = ({ tone, avatar = true }) => (
  <div style={{ position: "absolute", left: 18, right: 18, top: 92 }}>
    {new Array(7).fill(0).map((_, i) => (
      <div key={i} style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 16 }}>
        {avatar ? <div style={{ width: 26, height: 26, borderRadius: 13, background: tone, opacity: 0.9 - i * 0.07 }} /> : null}
        <div style={{ flex: 1 }}>
          <div style={{ height: 6, width: `${70 - (i % 3) * 12}%`, borderRadius: 3, background: tone }} />
          <div style={{ height: 5, width: `${88 - (i % 2) * 20}%`, borderRadius: 3, background: tone, opacity: 0.6, marginTop: 6 }} />
        </div>
      </div>
    ))}
  </div>
);

/** Gmail tile grows into a card in its own colour, then shrinks back. */
export const ColourOpen: React.FC<{ t: number }> = ({ t }) => {
  if (t < T2.gmailOpen || t > T2.gmailClose + 0.45) return null;
  const p = openSnap(t, T2.gmailOpen, T2.gmailClose);
  const r = lerpRect(GMAIL_RECT, FULL, p);
  const content = prog(t, T2.gmailOpen + 0.45, T2.gmailOpen + 0.8) * (1 - prog(t, T2.gmailClose - 0.2, T2.gmailClose + 0.05));
  return (
    <div
      style={{
        position: "absolute",
        left: r.x,
        top: r.y,
        width: r.w,
        height: r.h,
        borderRadius: interpolate(p, [0, 1], [GMAIL_RECT.w * TILE_RADIUS, 39]),
        background: interpolateColors(p, [0, 0.55], ["#ffffff", "#e5483d"]),
        overflow: "hidden",
        boxShadow: `0 20px 50px rgba(0,0,0,${0.45 * p})`,
      }}
    >
      <div style={{ position: "absolute", inset: 0, background: "linear-gradient(160deg, rgba(255,255,255,0.18), transparent 50%, rgba(0,0,0,0.18))", opacity: p }} />
      <div
        style={{
          position: "absolute",
          left: "50%",
          top: "50%",
          translate: "-50% -50%",
          width: interpolate(p, [0, 1], [48, 96]),
          height: interpolate(p, [0, 1], [48, 96]),
          borderRadius: 26,
          background: `rgba(255,255,255,${prog(p, 0.3, 0.8)})`,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          opacity: 1 - content,
          scale: 1 + content * 0.3,
          boxShadow: `0 10px 30px rgba(0,0,0,${0.25 * p})`,
        }}
      >
        <GmailM size={interpolate(p, [0, 1], [34, 60])} />
      </div>
      <div style={{ position: "absolute", inset: 0, opacity: content }}>
        <div style={{ position: "absolute", left: 18, top: 40, color: "#fff", fontSize: 18, fontWeight: 800 }}>Inbox</div>
        <div style={{ position: "absolute", left: 18, right: 18, top: 68, height: 12, borderRadius: 6, background: "rgba(255,255,255,0.22)" }} />
        <Skeleton tone="rgba(255,255,255,0.32)" />
      </div>
    </div>
  );
};

/** Chrome (dock) opens into a liquid glass card instead. */
export const GlassOpen: React.FC<{ t: number }> = ({ t }) => {
  if (t < T2.chromeOpen || t > T2.chromeClose + 0.45) return null;
  const src = DOCK_ICON(2);
  const p = openSnap(t, T2.chromeOpen, T2.chromeClose);
  const r = lerpRect(src, FULL, p);
  const content = prog(t, T2.chromeOpen + 0.45, T2.chromeOpen + 0.8) * (1 - prog(t, T2.chromeClose - 0.2, T2.chromeClose + 0.05));
  const sheen = tw(t, T2.chromeOpen + 0.1, T2.chromeOpen + 1.0, -40, 150, easeInOut);
  const solid = 1 - prog(p, 0.05, 0.5);
  return (
    <div
      style={{
        position: "absolute",
        left: r.x,
        top: r.y,
        width: r.w,
        height: r.h,
        borderRadius: interpolate(p, [0, 1], [src.w * TILE_RADIUS, 39]),
        background: `linear-gradient(160deg, rgba(255,255,255,${0.26 + solid * 0.74}), rgba(255,255,255,${0.07 + solid * 0.93}) 55%, rgba(255,255,255,${0.15 + solid * 0.85}))`,
        backdropFilter: "blur(38px) saturate(180%) brightness(1.08)",
        border: `1px solid rgba(255,255,255,${0.45 * p})`,
        boxShadow: `inset 0 1.5px 0 rgba(255,255,255,${0.7 * p}), inset 0 -30px 60px rgba(255,255,255,0.06), 0 24px 60px rgba(0,0,0,${0.45 * p})`,
        overflow: "hidden",
      }}
    >
      <div
        style={{
          position: "absolute",
          inset: 0,
          background: `linear-gradient(115deg, transparent ${sheen - 14}%, rgba(255,255,255,0.35) ${sheen}%, transparent ${sheen + 14}%)`,
        }}
      />
      <div
        style={{
          position: "absolute",
          left: "50%",
          top: "50%",
          translate: "-50% -50%",
          opacity: 1 - content,
          scale: 1 + content * 0.3,
          filter: `drop-shadow(0 8px 20px rgba(0,0,0,${0.35 * p}))`,
        }}
      >
        <ChromeGlyph size={interpolate(p, [0, 1], [30, 70])} />
      </div>
      <div style={{ position: "absolute", inset: 0, opacity: content }}>
        <div style={{ position: "absolute", left: 16, right: 16, top: 40, height: 30, borderRadius: 15, ...glass(1.1), backdropFilter: "none", display: "flex", alignItems: "center", gap: 8, padding: "0 12px" }}>
          <SearchGlyph size={10} />
          <div style={{ fontSize: 8.5, color: "rgba(255,255,255,0.75)" }}>Search or type a URL</div>
        </div>
        {[0, 1, 2].map((i) => (
          <div key={i} style={{ position: "absolute", left: 16, right: 16, top: 90 + i * 150, height: 132, borderRadius: 20, ...glass(1.1), backdropFilter: "none" }}>
            <div style={{ position: "absolute", left: 14, top: 14, right: 60, height: 7, borderRadius: 4, background: "rgba(255,255,255,0.4)" }} />
            <div style={{ position: "absolute", left: 14, top: 28, right: 110, height: 6, borderRadius: 3, background: "rgba(255,255,255,0.25)" }} />
            <div style={{ position: "absolute", left: 14, right: 14, top: 48, bottom: 14, borderRadius: 12, background: "rgba(255,255,255,0.1)" }} />
          </div>
        ))}
      </div>
    </div>
  );
};

// ------------------------------------------------------------ drawer

const COLS = 4;
const cell = (i: number) => ({ x: 24 + (i % COLS) * 72 + 13, y: 112 + Math.floor(i / COLS) * 76 });
const QUERY = "maps";

const drawerYAt = (t: number) => {
  let y = SH;
  if (t >= T2.drawerUp) y = tw(t, T2.drawerUp, T2.drawerUp + 0.5, SH, 0, softSpring);
  if (t >= T2.drawerDown) y = tw(t, T2.drawerDown, T2.drawerDown + 0.35, 0, SH, easeIn);
  if (t >= T2.hideDrawer + 0.15) y = tw(t, T2.hideDrawer + 0.15, T2.hideDrawer + 0.6, SH, 0, softSpring);
  if (t >= T2.hideDrawerDown) y = tw(t, T2.hideDrawerDown, T2.hideDrawerDown + 0.3, 0, SH, easeIn);
  return y;
};

const queryAt = (t: number) => {
  if (t > T2.drawerDown + 0.4) return "";
  return QUERY.slice(0, T2.typeAt.filter((a) => t >= a).length);
};

/** The visible app order at each step of the scene (filtering, then hiding Calendar). */
const drawerSteps = (t: number): [number, string[]][] => {
  const all = DRAWER_APPS.map((a) => a.name);
  if (t < T2.hideSheetUp) {
    return [
      [0, all],
      ...T2.typeAt.map((at, i) => [at, all.filter((n) => n.toLowerCase().includes(QUERY.slice(0, i + 1)))] as [number, string[]]),
    ];
  }
  return [
    [0, all],
    [T2.poof + 0.1, all.filter((n) => n !== "Calendar")],
  ];
};

const appPlacement = (t: number, name: string) => {
  const steps = drawerSteps(t);
  const at = (list: string[]) => {
    const i = list.indexOf(name);
    return i < 0 ? { ...cell(all0(name)), shown: 0 } : { ...cell(i), shown: 1 };
  };
  let prev = at(steps[0][1]);
  let cur = prev;
  for (let k = 1; k < steps.length; k++) {
    const [time, list] = steps[k];
    if (t < time) break;
    const target = at(list);
    const nextAt = steps[k + 1]?.[0];
    const tEval = nextAt !== undefined && t >= nextAt ? nextAt : t; // state when the next step takes over
    const p = prog(tEval, time, time + 0.28, easeInOut);
    cur = {
      x: interpolate(p, [0, 1], [prev.x, target.shown ? target.x : prev.x]),
      y: interpolate(p, [0, 1], [prev.y, target.shown ? target.y : prev.y]),
      shown: interpolate(p, [0, 1], [prev.shown, target.shown]),
    };
    prev = cur;
  }
  return cur;
};
const all0 = (name: string) => DRAWER_APPS.findIndex((a) => a.name === name);

export const Drawer: React.FC<{ t: number; pal: Pal2 }> = ({ t, pal }) => {
  const y = drawerYAt(t);
  if (y >= SH) return null;
  const q = queryAt(t);
  const focused = t >= T2.searchAway - 0.05 && t < T2.drawerDown + 0.3;
  const caret = Math.floor(t * 2.4) % 2 === 0;
  const poof = t >= T2.hideSheetUp ? prog(t, T2.poof, T2.poof + 0.35, easeIn) : 0;
  const count = t >= T2.poof + 0.2 ? 19 : 20;
  return (
    <div style={{ position: "absolute", inset: 0 }}>
      <div style={{ position: "absolute", inset: 0, background: "rgba(0,0,0,0.25)", opacity: 1 - y / SH }} />
      <div
        style={{
          position: "absolute",
          inset: 0,
          translate: `0px ${y}px`,
          borderRadius: "24px 24px 0 0",
          background: "rgba(8,8,22,0.72)",
          backdropFilter: "blur(30px) saturate(140%)",
          boxShadow: "0 -10px 40px rgba(0,0,0,0.4)",
        }}
      >
        <div style={{ width: 30, height: 3, borderRadius: 2, background: "rgba(255,255,255,0.4)", margin: "30px auto 0" }} />
        <div style={{ position: "absolute", left: 26, top: 54, color: "#fff", fontSize: 17, fontWeight: 800 }}>All apps</div>
        <div style={{ position: "absolute", left: 26, top: 76, color: "rgba(255,255,255,0.6)", fontSize: 8 }}>{count} apps</div>
        <div style={{ position: "absolute", left: 284, top: 56, width: 26, height: 26, borderRadius: 13, ...glass(1), display: "flex", alignItems: "center", justifyContent: "center", color: "#fff", fontSize: 12 }}>
          ×
        </div>
        {DRAWER_APPS.map((a) => {
          const pl = appPlacement(t, a.name);
          const isCal = a.name === "Calendar";
          const gone = isCal ? poof : 0;
          const s = pl.shown * (1 - gone) * (1 + (isCal ? Math.sin(Math.PI * Math.min(1, poof * 1.6)) * 0.25 : 0));
          if (s <= 0.01) return null;
          return (
            <div
              key={a.name}
              style={{
                position: "absolute",
                left: pl.x - 13,
                top: pl.y,
                width: 72,
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                scale: Math.max(s, 0),
                opacity: Math.min(1, pl.shown * 1.2) * (1 - gone),
                filter: gone > 0 ? `blur(${gone * 6}px)` : undefined,
              }}
            >
              <div style={{ width: 46, height: 46, borderRadius: 46 * TILE_RADIUS, background: a.bg, display: "flex", alignItems: "center", justifyContent: "center", overflow: "hidden", boxShadow: a.bg === "transparent" ? undefined : "0 4px 10px rgba(0,0,0,0.25)" }}>
                {a.glyph(36)}
              </div>
              <div style={{ color: "#fff", fontSize: 7.5, marginTop: 5, fontWeight: 500 }}>{a.name}</div>
            </div>
          );
        })}
        {/* sparkles where Calendar was */}
        {t >= T2.poof && t < T2.poof + 0.8
          ? new Array(10).fill(0).map((_, i) => {
              const c = cell(0);
              const a = random(`pa${i}`) * Math.PI * 2;
              const d = prog(t, T2.poof, T2.poof + 0.6, easeOut) * (18 + random(`pd${i}`) * 26);
              return (
                <div
                  key={i}
                  style={{
                    position: "absolute",
                    left: c.x + 23 + Math.cos(a) * d,
                    top: c.y + 23 + Math.sin(a) * d,
                    width: 3,
                    height: 3,
                    borderRadius: 2,
                    background: i % 2 ? pal.accent : pal.accent2,
                    opacity: 1 - prog(t, T2.poof + 0.2, T2.poof + 0.75),
                  }}
                />
              );
            })
          : null}
        <div
          style={{
            position: "absolute",
            left: 20,
            top: 664,
            width: 296,
            height: 34,
            borderRadius: 17,
            ...glass(1),
            border: `0.8px solid ${focused ? pal.accent : "rgba(255,255,255,0.18)"}`,
            display: "flex",
            alignItems: "center",
            gap: 8,
            padding: "0 12px",
          }}
        >
          <SearchGlyph size={11} />
          <div style={{ fontSize: 9, color: q ? "#fff" : "rgba(255,255,255,0.6)", fontWeight: q ? 600 : 400 }}>
            {q || (focused ? "" : "Search apps or the web")}
            {focused && caret ? <span style={{ color: pal.accent, fontWeight: 300 }}>|</span> : null}
          </div>
        </div>
      </div>
    </div>
  );
};

// ------------------------------------------------------------ free placement

const Pill: React.FC<{ t: number; inAt: number; outAt: number; children: React.ReactNode }> = ({ t, inAt, outAt, children }) => {
  const p = prog(t, inAt, inAt + 0.4, springy) - prog(t, outAt, outAt + 0.25, easeIn);
  if (p <= 0.001 && t > inAt) return null;
  return (
    <div
      style={{
        position: "absolute",
        left: "50%",
        top: 30,
        translate: `-50% ${interpolate(p, [0, 1], [-60, 0])}px`,
        opacity: Math.min(1, p * 1.5),
        height: 28,
        padding: "0 14px",
        borderRadius: 14,
        ...glass(1.2),
        background: "rgba(30,24,52,0.55)",
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

const Trash: React.FC = () => (
  <svg width={10} height={10} viewBox="0 0 24 24">
    <path fill="#fff" d="M9 3h6l1 2h4v2H4V5h4zM6 8h12l-1 13H7z" />
  </svg>
);

export const FreePlacement: React.FC<{ t: number; pal: Pal2 }> = ({ t, pal }) => {
  if (t < T2.pressAt - 0.1 || t > T2.resizeDone + 0.5) return null;
  const g = googleRectAt(t);
  const press = prog(t, T2.pressAt, T2.dragStart, easeInOut) * (1 - prog(t, T2.dragStart, T2.dragStart + 0.15));
  const dragging = t >= T2.dragStart && t < T2.drop + 0.25;
  const f = fingerAt(t);
  const ghostFade = 1 - prog(t, T2.drop, T2.drop + 0.25);
  const resizing = prog(t, T2.resizeStart - 0.12, T2.resizeStart + 0.1) * (1 - prog(t, T2.resizeDone, T2.resizeDone + 0.2));
  return (
    <>
      {/* long-press ring */}
      {press > 0 ? (
        <div
          style={{
            position: "absolute",
            left: g.x - 5,
            top: g.y - 5,
            width: g.w + 10,
            height: g.h + 10,
            borderRadius: g.w * TILE_RADIUS + 5,
            border: `2px solid ${pal.accent}`,
            opacity: press,
            boxShadow: `0 0 ${16 * press}px ${pal.accent}`,
          }}
        />
      ) : null}
      <Pill t={t} inAt={T2.dragStart - 0.1} outAt={T2.drop + 0.1}>
        <Trash /> Hold to remove
      </Pill>
      {dragging && f ? (
        <div
          style={{
            position: "absolute",
            left: f.x - 29,
            top: f.y - 67,
            width: 58,
            height: 134,
            borderRadius: 14,
            background: "rgba(205,205,220,0.6)",
            backdropFilter: "blur(6px)",
            border: "1px solid rgba(255,255,255,0.5)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            opacity: ghostFade * prog(t, T2.dragStart, T2.dragStart + 0.12),
            scale: 1.06,
            rotate: `${interpolate(t, [T2.dragStart, T2.dragStart + 0.4, T2.drop - 0.2, T2.drop], [0, 4, -2, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" })}deg`,
            boxShadow: "0 18px 36px rgba(0,0,0,0.4)",
          }}
        >
          <div style={{ filter: "grayscale(1)", opacity: 0.65 }}>
            <GoogleG size={30} />
          </div>
        </div>
      ) : null}
      <Pill t={t} inAt={T2.resizeStart - 0.15} outAt={T2.resizeDone}>
        Pull the pointer to resize · tap to finish
      </Pill>
      {resizing > 0 ? (
        <>
          <div
            style={{
              position: "absolute",
              left: g.x - 4,
              top: g.y - 4,
              width: g.w + 8,
              height: g.h + 8,
              borderRadius: Math.min(g.w, g.h) * TILE_RADIUS + 4,
              border: `2px solid ${pal.accent}`,
              boxShadow: `0 0 18px ${pal.accent}aa, inset 0 0 0 1px rgba(255,255,255,0.4)`,
              opacity: resizing,
            }}
          />
          <svg
            width={16}
            height={16}
            viewBox="0 0 24 24"
            style={{ position: "absolute", left: g.x + g.w - 8, top: g.y + g.h - 8, opacity: resizing, filter: "drop-shadow(0 2px 3px rgba(0,0,0,0.5))" }}
          >
            <path d="M3 3l18 7-8 3-3 8z" fill="#f2f0ff" stroke="#6b63c9" strokeWidth={1.2} strokeLinejoin="round" transform="rotate(180 12 12)" />
          </svg>
        </>
      ) : null}
    </>
  );
};

// ------------------------------------------------------------ sheets

const sheetBase = (top: number): React.CSSProperties => ({
  position: "absolute",
  left: 6,
  right: 6,
  top,
  height: SH + 120,
  borderRadius: 24,
  background: "linear-gradient(180deg, rgba(52,48,88,0.76), rgba(16,16,36,0.9))",
  backdropFilter: "blur(22px) saturate(160%)",
  border: "0.7px solid rgba(255,255,255,0.18)",
  boxShadow: "inset 0 1px 0 rgba(255,255,255,0.25), 0 -10px 40px rgba(0,0,0,0.4)",
  overflow: "hidden",
});

const Handle: React.FC = () => <div style={{ width: 30, height: 3, borderRadius: 2, background: "rgba(255,255,255,0.4)", margin: "8px auto 0" }} />;

export const HIDE_SHEET_TOP = 206;
export const HIDE_ROW_Y = (i: number) => 172 + i * 40;

export const HiddenAppsSheet: React.FC<{ t: number; pal: Pal2 }> = ({ t, pal }) => {
  let top = SH;
  if (t >= T2.hideSheetUp) top = tw(t, T2.hideSheetUp, T2.hideSheetUp + 0.45, SH, HIDE_SHEET_TOP, springy);
  if (t >= T2.hideDrawer) top = tw(t, T2.hideDrawer, T2.hideDrawer + 0.35, HIDE_SHEET_TOP, SH + 20, easeIn);
  if (top >= SH) return null;
  const on = prog(t, T2.hideToggle, T2.hideToggle + 0.25);
  const apps = ["Calendar", "Camera", "Chrome", "Clock", "Contacts"].map((n) => DRAWER_APPS.find((a) => a.name === n)!);
  return (
    <div style={sheetBase(top)}>
      <Handle />
      <div style={{ position: "absolute", left: 16, top: 22, color: "#fff", fontSize: 14, fontWeight: 700 }}>Hidden apps</div>
      <div style={{ position: "absolute", left: 16, right: 16, top: 48, color: "rgba(255,255,255,0.65)", fontSize: 8, lineHeight: 1.6 }}>
        Hidden apps stay out of the drawer and out of search. They are not locked on your phone — anything else that lists your apps still shows them.
      </div>
      <div style={{ position: "absolute", left: 16, top: 100, display: "flex", alignItems: "center", gap: 10 }}>
        <div style={{ width: 28, height: 28, borderRadius: 9, background: "rgba(255,255,255,0.1)", display: "flex", alignItems: "center", justifyContent: "center" }}>
          <LockGlyph size={12} />
        </div>
        <div>
          <div style={{ color: "#fff", fontSize: 10, fontWeight: 600 }}>Set a PIN</div>
          <div style={{ color: "rgba(255,255,255,0.55)", fontSize: 7.5, marginTop: 2 }}>Optional</div>
        </div>
      </div>
      <div style={{ position: "absolute", left: 16, top: 150, color: "rgba(255,255,255,0.55)", fontSize: 7, letterSpacing: 1, fontWeight: 600 }}>APPS</div>
      {apps.map((a, i) => (
        <div
          key={a.name}
          style={{
            position: "absolute",
            left: 16,
            right: 16,
            top: HIDE_ROW_Y(i),
            height: 40,
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            borderRadius: 12,
            background: i === 0 ? `rgba(255,255,255,${0.08 * on})` : undefined,
            padding: "0 4px",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <div style={{ width: 28, height: 28, borderRadius: 8, background: a.bg, display: "flex", alignItems: "center", justifyContent: "center", overflow: "hidden" }}>
              {a.glyph(22)}
            </div>
            <div style={{ color: "#fff", fontSize: 9.5, fontWeight: 500 }}>{a.name}</div>
          </div>
          <Toggle on={i === 0 ? on : 0} accent={pal.accent} />
        </div>
      ))}
    </div>
  );
};

export const THEME_SHEET_TOP = 196;
const SECTION_Y = [118, 214, 310];
const SLIDER_X0 = 14;
const SLIDER_W = 296;
export const sliderThumb = (section: number, row: number, frac: number) => ({
  x: 6 + SLIDER_X0 + frac * SLIDER_W,
  y: THEME_SHEET_TOP + SECTION_Y[section] + 24 + row * 22 + 7,
});

const Slider: React.FC<{ y: number; bg: string; frac: number; active?: boolean; ring: string }> = ({ y, bg, frac, active, ring }) => (
  <div style={{ position: "absolute", left: SLIDER_X0, top: y, width: SLIDER_W, height: 14, borderRadius: 7, background: bg }}>
    <div
      style={{
        position: "absolute",
        left: frac * SLIDER_W - 8,
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

export const ThemeMakerSheet: React.FC<{ t: number; pal: Pal2 }> = ({ t, pal }) => {
  let top = SH;
  if (t >= T2.themeIn) top = tw(t, T2.themeIn, T2.themeIn + 0.45, SH, THEME_SHEET_TOP, easeOut);
  if (t >= T2.themeDown) top = tw(t, T2.themeDown, T2.themeDown + 0.35, THEME_SHEET_TOP, SH + 20, easeIn);
  if (top >= SH) return null;
  const s = sliders(t);
  const active = (at: number, dur: number) => t >= at - 0.05 && t <= at + dur + 0.05;
  const rainbow = "linear-gradient(90deg, #f00, #ff0 17%, #0f0 33%, #0ff 50%, #00f 67%, #f0f 83%, #f00)";
  const sections = [
    { label: "Accent", h: s.accentH, sat: 0.46, val: 1, act: active(T2.slideAccent, 0.4) },
    { label: "Second accent", h: s.accent2H, sat: 0.72, val: 1, act: active(T2.slideAccent2, 0.38) },
    { label: "Background", h: s.bgH, sat: 0.55, val: s.bgV, act: active(T2.slideBg, 0.33) },
  ];
  return (
    <div style={sheetBase(top)}>
      <Handle />
      <div style={{ position: "absolute", left: 16, top: 20, color: "#fff", fontSize: 14, fontWeight: 700 }}>Theme maker</div>
      <div style={{ position: "absolute", left: 14, right: 14, top: 46, height: 60, borderRadius: 14, background: "rgba(0,0,0,0.32)" }}>
        <div style={{ position: "absolute", left: 12, right: 12, top: 10, height: 40, borderRadius: 20, ...glass(1.1), display: "flex", alignItems: "center", gap: 10, padding: "0 10px" }}>
          <div style={{ width: 22, height: 22, borderRadius: 11, background: `linear-gradient(135deg, ${pal.accent}, ${pal.accent2})` }} />
          <div style={{ color: "#fff", fontSize: 10, fontWeight: 700 }}>Yours</div>
        </div>
      </div>
      {sections.map((sec, i) => {
        const color = hsv(sec.h, sec.sat, sec.val);
        const y = SECTION_Y[i];
        return (
          <div key={sec.label}>
            <div style={{ position: "absolute", left: 14, right: 14, top: y, height: 18, display: "flex", alignItems: "center", justifyContent: "space-between" }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                <div style={{ width: 16, height: 16, borderRadius: 5, background: color, border: "0.6px solid rgba(255,255,255,0.3)" }} />
                <div style={{ color: "#fff", fontSize: 9.5, fontWeight: 500 }}>{sec.label}</div>
              </div>
              <div style={{ color: "rgba(255,255,255,0.6)", fontSize: 7.5, fontVariantNumeric: "tabular-nums" }}>{color}</div>
            </div>
            <Slider y={y + 24} bg={rainbow} frac={sec.h / 360} active={sec.act} ring={i === 2 ? pal.accent : color} />
            <Slider y={y + 46} bg={`linear-gradient(90deg, ${hsv(sec.h, 0, sec.val)}, ${hsv(sec.h, 1, sec.val)})`} frac={sec.sat} ring={color} />
            <Slider y={y + 68} bg={`linear-gradient(90deg, #000, ${hsv(sec.h, sec.sat, 1)})`} frac={sec.val} ring={color} />
          </div>
        );
      })}
    </div>
  );
};

// ------------------------------------------------------------ backup toast

export const BackupToast: React.FC<{ t: number; pal: Pal2 }> = ({ t, pal }) => {
  if (t < T2.backup - 0.05 || t > T2.backupGone + 0.4) return null;
  const p = prog(t, T2.backup, T2.backup + 0.45, springy) - prog(t, T2.backupGone, T2.backupGone + 0.3, easeIn);
  const fill = prog(t, T2.backup + 0.15, T2.backupDone, easeInOut);
  const done = prog(t, T2.backupDone, T2.backupDone + 0.3, springy);
  return (
    <div
      style={{
        position: "absolute",
        left: "50%",
        top: 490,
        width: 240,
        height: 48,
        translate: `-50% ${interpolate(p, [0, 1], [60, 0])}px`,
        opacity: Math.min(1, p * 1.5),
        borderRadius: 24,
        ...glass(1.3),
        background: "rgba(30,24,52,0.5)",
        display: "flex",
        alignItems: "center",
        gap: 10,
        padding: "0 12px",
      }}
    >
      <div style={{ width: 28, height: 28, borderRadius: 14, background: interpolateColors(Math.min(done, 1), [0, 1], ["rgba(255,255,255,0.15)", pal.accent2]), position: "relative", display: "flex", alignItems: "center", justifyContent: "center" }}>
        <svg width={13} height={13} viewBox="0 0 24 24" style={{ position: "absolute", opacity: 1 - Math.min(done, 1) }}>
          <path d="M12 4v11M7 9l5-5 5 5M5 19h14" fill="none" stroke="#fff" strokeWidth={2.4} strokeLinecap="round" strokeLinejoin="round" />
        </svg>
        <div style={{ position: "absolute", opacity: Math.min(done, 1), scale: 0.4 + Math.min(done, 1.2) * 0.6 }}>
          <CheckGlyph size={14} color="#1a1206" />
        </div>
      </div>
      <div style={{ flex: 1 }}>
        <div style={{ color: "#fff", fontSize: 9, fontWeight: 600 }}>{done > 0.4 ? "Home screen backed up" : "Backing up home screen…"}</div>
        <div style={{ marginTop: 5, height: 3, borderRadius: 2, background: "rgba(255,255,255,0.15)", overflow: "hidden" }}>
          <div style={{ width: `${fill * 100}%`, height: "100%", borderRadius: 2, background: `linear-gradient(90deg, ${pal.accent}, ${pal.accent2})` }} />
        </div>
      </div>
    </div>
  );
};

// ------------------------------------------------------------ finger

type Key = [number, number, number]; // time, x, y
type Gesture = { keys: Key[]; tap?: boolean; cut?: number };

const thumbA0 = sliderThumb(0, 0, 252 / 360);
const thumbA1 = sliderThumb(0, 0, 338 / 360);
const thumbB0 = sliderThumb(1, 0, 192 / 360);
const thumbB1 = sliderThumb(1, 0, 44 / 360);
const thumbC0 = sliderThumb(2, 0, 236 / 360);
const thumbC1 = sliderThumb(2, 0, 328 / 360);

const GESTURES: Gesture[] = [
  { keys: [[T2.gmailTap, GMAIL_RECT.x + 29, GMAIL_RECT.y + 67]], tap: true, cut: T2.gmailOpen },
  { keys: [[T2.chromeTap, DOCK_ICON(2).x + 24, DOCK_ICON(2).y + 24]], tap: true, cut: T2.chromeOpen },
  { keys: [[T2.swipeUp, 168, 692], [T2.swipeUp + 0.35, 168, 500]] },
  { keys: [[T2.searchAway - 0.12, 120, 681]], tap: true },
  {
    keys: [
      [T2.pressAt, 51, 381],
      [T2.dragStart, 51, 381],
      [T2.dragStart + 0.4, 180, 440],
      [T2.drop, 133, 381],
    ],
  },
  { keys: [[T2.resizeStart, 162, 448], [T2.resizeEnd, 236, 448]] },
  { keys: [[T2.resizeDone, 236, 448]], tap: true },
  { keys: [[T2.hideToggle - 0.05, 299, HIDE_SHEET_TOP + HIDE_ROW_Y(0) + 20]], tap: true },
  {
    keys: [
      [T2.slideAccent - 0.02, thumbA0.x, thumbA0.y],
      [T2.slideAccent + 0.4, thumbA1.x, thumbA1.y],
      [T2.slideAccent2 - 0.02, thumbB0.x, thumbB0.y],
      [T2.slideAccent2 + 0.38, thumbB1.x, thumbB1.y],
      [T2.slideBg - 0.02, thumbC0.x, thumbC0.y],
      [T2.slideBg + 0.33, thumbC1.x, thumbC1.y],
    ],
  },
];

type FingerState = { x: number; y: number; vis: number; down: number; ripple: number; cutK: number };

/** The most visible gesture at time t (overlapping fade windows hand over smoothly). */
export function fingerAt(t: number): FingerState | null {
  let best: FingerState | null = null;
  for (const g of GESTURES) {
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

export const Finger: React.FC<{ t: number }> = ({ t }) => {
  const f = fingerAt(t);
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
