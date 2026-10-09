import React from "react";
import { interpolate, interpolateColors, random } from "remotion";
import { CheckGlyph, LockGlyph, SearchGlyph, SparkleGlyph } from "../zenith/AppIcons";
import { Toggle, glass } from "../zenith/ui";
import { easeIn, easeInOut, easeOut, prog, softSpring, springy, tw } from "../zenith/anim";
import { hsv } from "../zenith2/theme2";
import { DRAWER_APPS } from "../zenith2/Glyphs2";
import { GoldChip, Glyph, Handle, SH, SW, Slider, sheetBase } from "./kit";
import { CLOUD } from "./Home3";
import { GOLD1, GOLD2, GOLD3, T3, sliders3, type Pal3 } from "./t3";

const row: React.CSSProperties = { display: "flex", alignItems: "center", justifyContent: "space-between", gap: 10 };
const title: React.CSSProperties = { fontSize: 10, fontWeight: 600, color: "#fff" };
const sub: React.CSSProperties = { fontSize: 7.5, color: "rgba(255,255,255,0.6)", marginTop: 2, lineHeight: 1.4 };

const BackCircle: React.FC<{ x: number; y: number; icon?: "back" | "close" }> = ({ x, y, icon = "back" }) => (
  <div style={{ position: "absolute", left: x, top: y, width: 26, height: 26, borderRadius: 13, background: "rgba(255,255,255,0.1)", display: "flex", alignItems: "center", justifyContent: "center" }}>
    <svg width={12} height={12} viewBox="0 0 24 24">
      <path
        fill="none"
        stroke="#fff"
        strokeWidth={2.6}
        strokeLinecap="round"
        strokeLinejoin="round"
        d={icon === "back" ? "M19 12H5m6-6l-6 6 6 6" : "M6 6l12 12M18 6L6 18"}
      />
    </svg>
  </div>
);

// ------------------------------------------------------------ Plus page → Theme maker page

export const PLUS_TOP = 150;
export const THEME_TOP = 196;
const PLUS_ROW_Y = (i: number) => 112 + i * 44;
export const PLUS_ROW_CENTER = (i: number) => ({ x: 150, y: PLUS_TOP + PLUS_ROW_Y(i) + 22 });
const SECTION_Y = [118, 214, 310];
const SLIDER_X0 = 14;
const SLIDER_W = 296;
export const sliderThumb = (section: number, rowIdx: number, frac: number) => ({
  x: 6 + SLIDER_X0 + frac * SLIDER_W,
  y: THEME_TOP + SECTION_Y[section] + 24 + rowIdx * 22 + 7,
});

const PLUS_FEATURES = [
  { n: "Theme maker", d: "Build a palette of your own" },
  { n: "Hidden apps", d: "Keep chosen apps out of the drawer" },
  { n: "Free placement", d: "Put apps anywhere, any widget size" },
  { n: "Back up to your account", d: "Your setup, saved to your Zenith account" },
];

const PlusPage: React.FC<{ t: number }> = ({ t }) => {
  const shine = tw(t, T3.plusSheetUp + 0.35, T3.plusSheetUp + 1.1, -60, 160);
  const unlocked = prog(t, T3.unlockAt[3], T3.unlockAt[3] + 0.3);
  return (
    <>
      <BackCircle x={10} y={16} />
      <div style={{ position: "absolute", left: 44, top: 20, fontSize: 14, fontWeight: 700, color: "#fff", letterSpacing: -0.2 }}>
        Zenith <span style={{ color: GOLD2 }}>Plus</span>
      </div>
      <div
        style={{
          position: "absolute",
          left: 10,
          right: 10,
          top: 52,
          height: 50,
          overflow: "hidden",
          borderRadius: 14,
          padding: "0 12px",
          background: "rgba(255,255,255,0.07)",
          border: "0.6px solid rgba(255,255,255,0.14)",
          ...row,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <div style={{ width: 26, height: 26, borderRadius: 8, background: `linear-gradient(135deg, ${GOLD1}, ${GOLD2})`, display: "flex", alignItems: "center", justifyContent: "center" }}>
            <SparkleGlyph size={15} />
          </div>
          <div>
            <div style={title}>Try Plus free for 7 days</div>
            <div style={sub}>$1.99 a month or $14.99 once</div>
          </div>
        </div>
        <div style={{ color: "rgba(255,255,255,0.5)", fontSize: 12 }}>›</div>
        <div style={{ position: "absolute", top: -20, bottom: -20, left: `${shine}%`, width: 50, rotate: "20deg", background: "linear-gradient(90deg, transparent, rgba(255,230,160,0.4), transparent)" }} />
      </div>
      <div style={{ position: "absolute", left: 10, right: 10, top: 108, height: 4 * 44 + 8, borderRadius: 14, background: "rgba(255,255,255,0.055)", border: "0.5px solid rgba(255,255,255,0.07)" }} />
      {PLUS_FEATURES.map((f, i) => {
        const u = prog(t, T3.unlockAt[i], T3.unlockAt[i] + 0.4, springy);
        const uc = Math.min(1, u);
        const tapFlash = i === 0 ? Math.sin(Math.PI * prog(t, T3.themeTap - 0.02, T3.themeTap + 0.3)) : 0;
        return (
          <div
            key={f.n}
            style={{
              position: "absolute",
              left: 14,
              right: 14,
              top: PLUS_ROW_Y(i),
              height: 44,
              padding: "0 8px",
              borderRadius: 12,
              background: `rgba(255,214,120,${0.08 * Math.sin(Math.PI * uc) + 0.12 * tapFlash})`,
              borderTop: i ? "0.5px solid rgba(255,255,255,0.07)" : undefined,
              ...row,
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <div
                style={{
                  width: 24,
                  height: 24,
                  borderRadius: 8,
                  background: interpolateColors(uc, [0, 1], ["rgba(255,255,255,0.1)", GOLD2]),
                  boxShadow: `0 0 ${12 * uc}px ${GOLD2}88`,
                  position: "relative",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  flexShrink: 0,
                }}
              >
                <div style={{ position: "absolute", opacity: 1 - uc, scale: 1 + u * 0.4 }}>
                  <LockGlyph size={11} />
                </div>
                <div style={{ position: "absolute", opacity: uc, scale: 0.4 + u * 0.6 }}>
                  <CheckGlyph size={13} color="#3a2300" />
                </div>
              </div>
              <div>
                <div style={title}>{f.n}</div>
                <div style={sub}>{f.d}</div>
              </div>
            </div>
            <div style={{ color: "rgba(255,255,255,0.4)", fontSize: 12 }}>›</div>
          </div>
        );
      })}
      <div style={{ position: "absolute", left: 16, top: 300, ...sub, opacity: 1 - unlocked }}>Locked items open Zenith Plus.</div>
      <div style={{ position: "absolute", left: 16, top: 300, ...sub, color: GOLD1, opacity: unlocked }}>Everything is unlocked.</div>
    </>
  );
};

const ThemePage: React.FC<{ t: number; pal: Pal3 }> = ({ t, pal }) => {
  const s = sliders3(t);
  const active = (at: number, dur: number) => t >= at - 0.05 && t <= at + dur + 0.05;
  const rainbow = "linear-gradient(90deg, #f00, #ff0 17%, #0f0 33%, #0ff 50%, #00f 67%, #f0f 83%, #f00)";
  const sections = [
    { label: "Accent", h: s.accentH, sat: s.accentS, val: s.accentV, act: active(T3.slideAccent, 0.5) },
    { label: "Second accent", h: s.accent2H, sat: s.accent2S, val: 1, act: active(T3.slideAccent2, 0.45) },
    { label: "Background", h: s.bgH, sat: 0.5, val: 0.12, act: active(T3.slideBg, T3.slideBgEnd - T3.slideBg) },
  ];
  return (
    <>
      <BackCircle x={10} y={16} />
      <div style={{ position: "absolute", left: 44, top: 20, color: "#fff", fontSize: 14, fontWeight: 700 }}>Theme maker</div>
      <div style={{ position: "absolute", left: 14, right: 14, top: 50, height: 58, borderRadius: 14, background: "rgba(0,0,0,0.32)" }}>
        <div style={{ position: "absolute", left: 12, right: 12, top: 9, height: 40, borderRadius: 20, ...glass(1.1), backdropFilter: "none", display: "flex", alignItems: "center", gap: 10, padding: "0 10px" }}>
          <div style={{ width: 22, height: 22, borderRadius: 11, background: `linear-gradient(135deg, ${pal.accent}, ${pal.accent2})` }} />
          <div style={{ color: "#fff", fontSize: 10, fontWeight: 700 }}>Yours</div>
        </div>
      </div>
      {sections.map((sec, i) => {
        const color = hsv(sec.h, sec.sat, sec.val);
        const y = SECTION_Y[i];
        return (
          <div key={sec.label}>
            <div style={{ position: "absolute", left: 14, right: 14, top: y, height: 18, ...row }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                <div style={{ width: 16, height: 16, borderRadius: 5, background: color, border: "0.6px solid rgba(255,255,255,0.3)" }} />
                <div style={{ color: "#fff", fontSize: 9.5, fontWeight: 500 }}>{sec.label}</div>
              </div>
              <div style={{ color: "rgba(255,255,255,0.6)", fontSize: 7.5, fontVariantNumeric: "tabular-nums" }}>{color}</div>
            </div>
            <Slider x={SLIDER_X0} y={y + 24} w={SLIDER_W} bg={rainbow} frac={sec.h / 360} active={sec.act} ring={i === 2 ? pal.accent : color} />
            <Slider x={SLIDER_X0} y={y + 46} w={SLIDER_W} bg={`linear-gradient(90deg, ${hsv(sec.h, 0, Math.max(sec.val, 0.25))}, ${hsv(sec.h, 1, Math.max(sec.val, 0.25))})`} frac={sec.sat} ring={color} />
            <Slider x={SLIDER_X0} y={y + 68} w={SLIDER_W} bg={`linear-gradient(90deg, #000, ${hsv(sec.h, sec.sat, 1)})`} frac={sec.val} ring={color} />
          </div>
        );
      })}
    </>
  );
};

export const PlusThemeSheet: React.FC<{ t: number; pal: Pal3 }> = ({ t, pal }) => {
  let top = SH;
  if (t >= T3.plusSheetUp) top = tw(t, T3.plusSheetUp, T3.plusSheetUp + 0.45, SH, PLUS_TOP, easeOut);
  if (t >= T3.themePush) top = tw(t, T3.themePush, T3.themePush + 0.45, PLUS_TOP, THEME_TOP, softSpring);
  if (t >= T3.themeDown) top = tw(t, T3.themeDown, T3.themeDown + 0.35, THEME_TOP, SH + 20, easeIn);
  if (top >= SH) return null;
  const push = prog(t, T3.themePush, T3.themePush + 0.45, easeInOut);
  return (
    <div style={sheetBase(top)}>
      <Handle />
      {push < 1 ? (
        <div style={{ position: "absolute", inset: 0, translate: `${-push * 110}% 0px`, opacity: 1 - push * 0.8 }}>
          <PlusPage t={t} />
        </div>
      ) : null}
      {push > 0 ? (
        <div style={{ position: "absolute", inset: 0, translate: `${(1 - push) * 110}% 0px`, opacity: 0.2 + push * 0.8 }}>
          <ThemePage t={t} pal={pal} />
        </div>
      ) : null}
    </div>
  );
};

// ------------------------------------------------------------ Hidden apps

export const HIDE_TOP = 220;
export const HIDE_ROW_Y = (i: number) => 172 + i * 40;
const HIDE_APPS = ["Messages", "Photos", "Play Store", "Settings", "YouTube"];
export const HIDE_TARGET = 1; // Photos

export const HiddenSheet3: React.FC<{ t: number; pal: Pal3 }> = ({ t, pal }) => {
  let top = SH;
  if (t >= T3.hideSheetUp) top = tw(t, T3.hideSheetUp, T3.hideSheetUp + 0.38, SH, HIDE_TOP, easeOut);
  if (t >= T3.hideDrawer) top = tw(t, T3.hideDrawer, T3.hideDrawer + 0.32, HIDE_TOP, SH + 20, easeIn);
  if (top >= SH) return null;
  const on = prog(t, T3.hideToggle, T3.hideToggle + 0.25);
  const apps = HIDE_APPS.map((n) => DRAWER_APPS.find((a) => a.name === n)!);
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
            padding: "0 4px",
            borderRadius: 12,
            background: i === HIDE_TARGET ? `rgba(255,255,255,${0.08 * on})` : undefined,
            ...row,
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <div style={{ width: 28, height: 28, borderRadius: 8, background: a.bg, display: "flex", alignItems: "center", justifyContent: "center", overflow: "hidden" }}>{a.glyph(22)}</div>
            <div style={{ color: "#fff", fontSize: 9.5, fontWeight: 500 }}>{a.name}</div>
          </div>
          <Toggle on={i === HIDE_TARGET ? on : 0} accent={pal.accent} />
        </div>
      ))}
    </div>
  );
};

// ------------------------------------------------------------ drawer (Photos poofs out)

const COLS = 4;
const cell = (i: number) => ({ x: 24 + (i % COLS) * 72 + 13, y: 112 + Math.floor(i / COLS) * 76 });
const POOF_APP = "Photos";

export const Drawer3: React.FC<{ t: number; pal: Pal3 }> = ({ t, pal }) => {
  let y = SH;
  if (t >= T3.hideDrawer + 0.1) y = tw(t, T3.hideDrawer + 0.1, T3.hideDrawer + 0.52, SH, 0, softSpring);
  if (t >= T3.hideDrawerDown) y = tw(t, T3.hideDrawerDown, T3.hideDrawerDown + 0.3, 0, SH, easeIn);
  if (y >= SH) return null;
  const poof = prog(t, T3.poof, T3.poof + 0.32, easeIn);
  const reflow = prog(t, T3.poof + 0.1, T3.poof + 0.4, easeInOut);
  const names = DRAWER_APPS.map((a) => a.name);
  const after = names.filter((n) => n !== POOF_APP);
  const poofCell = cell(names.indexOf(POOF_APP));
  return (
    <div style={{ position: "absolute", inset: 0 }}>
      <div style={{ position: "absolute", inset: 0, background: "rgba(0,0,0,0.25)", opacity: 1 - y / SH }} />
      <div
        style={{
          position: "absolute",
          inset: 0,
          translate: `0px ${y}px`,
          borderRadius: "24px 24px 0 0",
          background: "rgba(14,8,20,0.74)",
          backdropFilter: "blur(30px) saturate(140%)",
          boxShadow: "0 -10px 40px rgba(0,0,0,0.4)",
        }}
      >
        <div style={{ width: 30, height: 3, borderRadius: 2, background: "rgba(255,255,255,0.4)", margin: "30px auto 0" }} />
        <div style={{ position: "absolute", left: 26, top: 54, color: "#fff", fontSize: 17, fontWeight: 800 }}>All apps</div>
        <div style={{ position: "absolute", left: 26, top: 76, color: "rgba(255,255,255,0.6)", fontSize: 8 }}>{reflow > 0.5 ? 19 : 20} apps</div>
        <div style={{ position: "absolute", left: 284, top: 56, width: 26, height: 26, borderRadius: 13, ...glass(1), backdropFilter: "none", display: "flex", alignItems: "center", justifyContent: "center", color: "#fff", fontSize: 12 }}>
          ×
        </div>
        {DRAWER_APPS.map((a, i) => {
          const isPoof = a.name === POOF_APP;
          const from = cell(i);
          const to = isPoof ? from : cell(after.indexOf(a.name));
          const x = interpolate(reflow, [0, 1], [from.x, to.x]);
          const yy = interpolate(reflow, [0, 1], [from.y, to.y]);
          const gone = isPoof ? poof : 0;
          const s = (1 - gone) * (1 + (isPoof ? Math.sin(Math.PI * Math.min(1, poof * 1.6)) * 0.25 : 0));
          if (s <= 0.01) return null;
          return (
            <div
              key={a.name}
              style={{
                position: "absolute",
                left: x - 13,
                top: yy,
                width: 72,
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                scale: s,
                opacity: 1 - gone,
                filter: gone > 0 ? `blur(${gone * 6}px)` : undefined,
              }}
            >
              <div style={{ width: 46, height: 46, borderRadius: 12, background: a.bg, display: "flex", alignItems: "center", justifyContent: "center", overflow: "hidden", boxShadow: a.bg === "transparent" ? undefined : "0 4px 10px rgba(0,0,0,0.25)" }}>
                {a.glyph(36)}
              </div>
              <div style={{ color: "#fff", fontSize: 7.5, marginTop: 5, fontWeight: 500 }}>{a.name}</div>
            </div>
          );
        })}
        {t >= T3.poof && t < T3.poof + 0.8
          ? new Array(10).fill(0).map((_, i) => {
              const a = random(`p3a${i}`) * Math.PI * 2;
              const d = prog(t, T3.poof, T3.poof + 0.6, easeOut) * (18 + random(`p3d${i}`) * 26);
              return (
                <div
                  key={i}
                  style={{
                    position: "absolute",
                    left: poofCell.x + 23 + Math.cos(a) * d,
                    top: poofCell.y + 23 + Math.sin(a) * d,
                    width: 3,
                    height: 3,
                    borderRadius: 2,
                    background: i % 2 ? pal.accent : pal.accent2,
                    opacity: 1 - prog(t, T3.poof + 0.2, T3.poof + 0.75),
                  }}
                />
              );
            })
          : null}
        <div style={{ position: "absolute", left: 20, top: 664, width: 296, height: 34, borderRadius: 17, ...glass(1), backdropFilter: "none", display: "flex", alignItems: "center", gap: 8, padding: "0 12px" }}>
          <SearchGlyph size={11} />
          <div style={{ fontSize: 9, color: "rgba(255,255,255,0.6)" }}>Search apps or the web</div>
        </div>
      </div>
    </div>
  );
};

// ------------------------------------------------------------ Backup & reset page

export const BACKUP_ROW_Y = 290; // "Back up to your account" row centre (screen px)
export const RESTORE_ROW_Y = 346; // "Restore from your account" row centre

const PageRow: React.FC<{ icon: React.ReactNode; name: React.ReactNode; desc: React.ReactNode; flash?: number; danger?: boolean }> = ({
  icon,
  name,
  desc,
  flash = 0,
  danger,
}) => (
  <div style={{ height: 56, display: "flex", alignItems: "center", gap: 12, padding: "0 12px", background: `rgba(255,255,255,${0.12 * flash})` }}>
    <div
      style={{
        width: 28,
        height: 28,
        borderRadius: 9,
        background: danger ? "rgba(255,90,90,0.16)" : "rgba(255,255,255,0.1)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        flexShrink: 0,
      }}
    >
      {icon}
    </div>
    <div style={{ flex: 1 }}>
      <div style={{ fontSize: 10, fontWeight: 600, color: danger ? "#ff7a7a" : "#fff", display: "flex", alignItems: "center", gap: 6 }}>{name}</div>
      <div style={{ ...sub, fontSize: 7.5 }}>{desc}</div>
    </div>
  </div>
);

export const BackupPage3: React.FC<{ t: number; pal: Pal3 }> = ({ t, pal }) => {
  let x = SW;
  if (t >= T3.backupPageIn) x = tw(t, T3.backupPageIn, T3.backupPageIn + 0.4, SW, 0, easeOut);
  if (t >= T3.backupPageOut) x = tw(t, T3.backupPageOut, T3.backupPageOut + 0.3, 0, SW + 20, easeIn);
  if (x >= SW) return null;
  const fill = prog(t, T3.backupTap + 0.1, T3.backupDone - 0.05, easeInOut);
  const uploading = t >= T3.backupTap + 0.05 && t < T3.backupDone;
  const done = prog(t, T3.backupDone, T3.backupDone + 0.3, springy);
  const flashB = Math.sin(Math.PI * prog(t, T3.backupTap - 0.02, T3.backupTap + 0.35));
  const flashR = Math.sin(Math.PI * prog(t, T3.restoreTap - 0.02, T3.restoreTap + 0.35));
  const bob = uploading ? Math.sin(t * 14) * 1.5 : 0;
  const card: React.CSSProperties = { position: "absolute", left: 14, right: 14, borderRadius: 16, overflow: "hidden", background: "rgba(255,255,255,0.07)", border: "0.5px solid rgba(255,255,255,0.08)" };
  return (
    <div style={{ position: "absolute", inset: 0, translate: `${x}px 0px`, background: "rgba(16,10,22,0.62)", backdropFilter: "blur(26px) saturate(150%)" }}>
      <BackCircle x={16} y={40} />
      <BackCircle x={294} y={40} icon="close" />
      <div style={{ position: "absolute", left: 18, top: 80, color: "#fff", fontSize: 20, fontWeight: 800, letterSpacing: -0.3 }}>Backup & reset</div>
      <div style={{ ...card, top: 118 }}>
        <PageRow icon={Glyph.save(14)} name="Back up home screen" desc={<>Your layout, widgets and settings, saved to a file you keep</>} />
        <div style={{ height: 0.5, background: "rgba(255,255,255,0.07)", marginLeft: 52 }} />
        <PageRow icon={Glyph.history(14)} name="Restore from a backup" desc={<>Puts a saved file back. Replaces your current layout and settings</>} />
      </div>
      <div style={{ position: "absolute", left: 18, top: 246, color: "rgba(255,255,255,0.6)", fontSize: 8 }}>In your Zenith account</div>
      <div style={{ ...card, top: 262, border: `0.8px solid ${interpolateColors(Math.min(1, done), [0, 1], ["rgba(255,214,120,0.35)", GOLD2])}` }}>
        <PageRow
          flash={flashB}
          icon={<div style={{ translate: `0px ${bob}px` }}>{Glyph.cloudUp(16, uploading || done > 0 ? GOLD1 : "#fff")}</div>}
          name={
            <>
              Back up to your account <GoldChip />
            </>
          }
          desc={
            done > 0.2 ? (
              <span style={{ color: GOLD1, display: "inline-flex", alignItems: "center", gap: 4 }}>
                <CheckGlyph size={9} color={GOLD1} /> Backed up just now
              </span>
            ) : uploading ? (
              <span style={{ display: "block", marginTop: 4, height: 4, width: 150, borderRadius: 2, background: "rgba(255,255,255,0.15)", overflow: "hidden" }}>
                <span style={{ display: "block", height: "100%", width: `${fill * 100}%`, borderRadius: 2, background: `linear-gradient(90deg, ${GOLD1}, ${GOLD3})` }} />
              </span>
            ) : (
              "Signed in as Gabriel"
            )
          }
        />
        <div style={{ height: 0.5, background: "rgba(255,255,255,0.07)", marginLeft: 52 }} />
        <PageRow flash={flashR} icon={Glyph.cloudDown(16)} name="Restore from your account" desc="Replaces your current layout" />
      </div>
      <div style={{ ...card, top: 390 }}>
        <PageRow danger icon={Glyph.power(14)} name="Run setup again" desc="Start from the welcome screen" />
      </div>
      {/* accent glow on the account card while it uploads */}
      <div style={{ position: "absolute", left: 14, right: 14, top: 262, height: 112, borderRadius: 16, boxShadow: `0 0 ${24 * Math.max(fill * (1 - done), done * 0.6)}px ${pal.accent}55`, pointerEvents: "none" }} />
    </div>
  );
};

/** Cloud badge that the home screen is pulled into and restored from. */
export const CloudBadge: React.FC<{ t: number }> = ({ t }) => {
  const vis = prog(t, T3.dissolve - 0.1, T3.dissolve + 0.1) * (1 - prog(t, T3.reassemble + 0.45, T3.reassemble + 0.7));
  if (vis <= 0) return null;
  const pulse = 1 + 0.12 * Math.sin(Math.PI * prog(t, T3.dissolve + 0.25, T3.dissolve + 0.5)) + 0.12 * Math.sin(Math.PI * prog(t, T3.reassemble - 0.05, T3.reassemble + 0.25));
  return (
    <div
      style={{
        position: "absolute",
        left: CLOUD.x - 19,
        top: CLOUD.y - 19 + 20,
        width: 38,
        height: 38,
        borderRadius: 19,
        ...glass(1.3),
        background: "rgba(40,28,10,0.5)",
        border: `1px solid ${GOLD2}`,
        boxShadow: `0 0 20px ${GOLD2}88`,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        opacity: vis,
        scale: pulse,
      }}
    >
      {t < T3.reassemble ? Glyph.cloudUp(20, GOLD1) : Glyph.cloudDown(20, GOLD1)}
    </div>
  );
};

// ------------------------------------------------------------ paywall

export const PAYWALL_TOP = 48;
export const PRICE_CARD_CENTER = (i: number) => ({ x: 6 + (i === 0 ? 14 : 168) + 73, y: PAYWALL_TOP + 300 + 52 });

export const Paywall3: React.FC<{ t: number }> = ({ t }) => {
  if (t < T3.paywallUp) return null;
  const top = tw(t, T3.paywallUp, T3.paywallUp + 0.45, SH, PAYWALL_TOP, easeOut);
  const feats = ["Theme maker", "Free placement", "Hidden apps", "Account backup"];
  const monthlySel = prog(t, T3.monthly - 0.06, T3.monthly + 0.25) * (1 - prog(t, T3.once - 0.06, T3.once + 0.2));
  const onceSel = prog(t, T3.once - 0.06, T3.once + 0.25);
  const ctaShine = tw(t, T3.ctaShine, T3.ctaShine + 0.8, -40, 140, easeInOut);
  const glow = 0.6 + 0.4 * Math.sin(t * 3);
  const card = (i: number, label: string, price: string, per: string, sel: number, selAt: number) => {
    const pop = 1 + 0.12 * Math.sin(Math.PI * prog(t, selAt, selAt + 0.35));
    return (
      <div
        style={{
          position: "absolute",
          left: i === 0 ? 14 : 168,
          top: 300,
          width: 146,
          height: 104,
          borderRadius: 18,
          background: `linear-gradient(160deg, rgba(255,214,120,${0.06 + 0.14 * sel}), rgba(255,255,255,0.04))`,
          border: `${1 + sel}px solid ${interpolateColors(sel, [0, 1], ["rgba(255,255,255,0.14)", GOLD2])}`,
          boxShadow: `0 0 ${26 * sel}px ${GOLD2}66`,
          scale: 1 + 0.04 * sel,
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          gap: 3,
          opacity: 0.6 + 0.4 * Math.max(sel, t < T3.monthly ? 1 : 0),
        }}
      >
        <div style={{ fontSize: 8, color: "rgba(255,255,255,0.7)", fontWeight: 600, letterSpacing: 0.4 }}>{label}</div>
        <div style={{ fontSize: 22, fontWeight: 800, color: sel > 0.5 ? GOLD1 : "#fff", scale: pop }}>{price}</div>
        <div style={{ fontSize: 8, color: "rgba(255,255,255,0.65)" }}>{per}</div>
      </div>
    );
  };
  return (
    <div style={{ ...sheetBase(top), background: "linear-gradient(180deg, rgba(52,36,28,0.86), rgba(16,10,18,0.95))" }}>
      <Handle />
      <div style={{ position: "absolute", left: "50%", top: 22, translate: "-50% 0", width: 52, height: 52, borderRadius: 16, background: `linear-gradient(135deg, ${GOLD1}, ${GOLD3})`, display: "flex", alignItems: "center", justifyContent: "center", boxShadow: `0 0 ${30 * glow}px ${GOLD2}99` }}>
        <SparkleGlyph size={30} />
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: 86, textAlign: "center", color: "#fff", fontSize: 19, fontWeight: 800, letterSpacing: -0.3 }}>
        Zenith <span style={{ color: GOLD2 }}>Plus</span>
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: 112, textAlign: "center", color: "rgba(255,255,255,0.65)", fontSize: 9 }}>Everything, unlocked.</div>
      {feats.map((f, i) => {
        const p = prog(t, T3.paywallUp + 0.35 + i * 0.1, T3.paywallUp + 0.75 + i * 0.1, springy);
        return (
          <div
            key={f}
            style={{
              position: "absolute",
              left: i % 2 === 0 ? 14 : 168,
              top: 140 + Math.floor(i / 2) * 38,
              width: 146,
              height: 30,
              borderRadius: 15,
              background: "rgba(255,255,255,0.07)",
              border: "0.5px solid rgba(255,255,255,0.1)",
              display: "flex",
              alignItems: "center",
              gap: 7,
              padding: "0 10px",
              opacity: Math.min(1, p * 1.4),
              scale: interpolate(p, [0, 1], [0.8, 1]),
            }}
          >
            <div style={{ width: 16, height: 16, borderRadius: 8, background: GOLD2, display: "flex", alignItems: "center", justifyContent: "center" }}>
              <CheckGlyph size={10} color="#3a2300" />
            </div>
            <div style={{ color: "#fff", fontSize: 8.5, fontWeight: 600 }}>{f}</div>
          </div>
        );
      })}
      <div style={{ position: "absolute", left: 16, top: 226, color: "#fff", fontSize: 11, fontWeight: 800 }}>
        7 days <span style={{ color: GOLD2 }}>free</span>
      </div>
      <div style={{ position: "absolute", left: 30, right: 30, top: 266, height: 3, borderRadius: 2, background: "rgba(255,255,255,0.12)" }}>
        <div style={{ height: "100%", borderRadius: 2, width: `${prog(t, T3.daysStart, T3.daysStart + 6 * 0.16) * 100}%`, background: `linear-gradient(90deg, ${GOLD1}, ${GOLD3})` }} />
      </div>
      {new Array(7).fill(0).map((_, i) => {
        const on = prog(t, T3.daysStart + i * 0.16, T3.daysStart + i * 0.16 + 0.2, springy);
        return (
          <div
            key={i}
            style={{
              position: "absolute",
              left: 17 + i * 45,
              top: 254,
              width: 26,
              height: 26,
              borderRadius: 13,
              background: interpolateColors(Math.min(1, on), [0, 1], ["rgba(40,30,40,1)", GOLD2]),
              border: `1px solid ${interpolateColors(Math.min(1, on), [0, 1], ["rgba(255,255,255,0.2)", GOLD1])}`,
              color: on > 0.5 ? "#3a2300" : "rgba(255,255,255,0.7)",
              fontSize: 9,
              fontWeight: 800,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              scale: 1 + 0.2 * Math.sin(Math.PI * Math.min(1, on)),
            }}
          >
            {i + 1}
          </div>
        );
      })}
      {card(0, "MONTHLY", "$1.99", "a month", monthlySel, T3.monthly)}
      {card(1, "ONE-TIME", "$14.99", "pay once", onceSel, T3.once)}
      <div
        style={{
          position: "absolute",
          left: 14,
          right: 14,
          top: 428,
          height: 44,
          borderRadius: 22,
          overflow: "hidden",
          background: `linear-gradient(135deg, ${GOLD1}, ${GOLD2} 55%, ${GOLD3})`,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          color: "#3a2300",
          fontSize: 11.5,
          fontWeight: 800,
          boxShadow: `0 8px 24px ${GOLD2}55`,
        }}
      >
        Start free trial
        <div style={{ position: "absolute", top: -20, bottom: -20, left: `${ctaShine}%`, width: 40, rotate: "20deg", background: "linear-gradient(90deg, transparent, rgba(255,255,255,0.7), transparent)" }} />
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: 482, textAlign: "center", ...sub, fontSize: 7.5 }}>Then $1.99 a month, or $14.99 once.</div>
    </div>
  );
};
