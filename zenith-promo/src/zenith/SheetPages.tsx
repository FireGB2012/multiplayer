import React from "react";
import { interpolate, interpolateColors } from "remotion";
import { CheckGlyph, FlameGlyph, LockGlyph, SparkleGlyph } from "./AppIcons";
import { Card, Label, Segmented, SheetHeader, Toggle } from "./ui";
import { easeInOut, prog, springy, stepAt, tw } from "./anim";
import { SWATCHES, T } from "./timeline";
import type { Palette } from "./HomeScreen";

const row: React.CSSProperties = { display: "flex", alignItems: "center", justifyContent: "space-between", gap: 10 };
const title: React.CSSProperties = { fontSize: 10, fontWeight: 600, color: "#fff" };
const sub: React.CSSProperties = { fontSize: 7.5, color: "rgba(255,255,255,0.6)", marginTop: 2, lineHeight: 1.4 };

/** Animated slide between keyed positions (used for selection rings / segmented highlights). */
const slideBetween = (t: number, keys: [number, number][], dur = 0.32) => {
  let v = keys[0][1];
  for (let i = 1; i < keys.length; i++) {
    const [at, to] = keys[i];
    if (t >= at) v = tw(t, at, at + dur, keys[i - 1][1], to, easeInOut);
  }
  return v;
};

// Glass amount 20% → 70% → 45% on "Dial in the glass".
export const glassAmountAt = (t: number) => {
  if (t < T.dialGlass + 0.15) return 0.2;
  if (t < T.dialGlass + 0.75) return tw(t, T.dialGlass + 0.15, T.dialGlass + 0.75, 0.2, 0.7, easeInOut);
  return tw(t, T.dialGlass + 0.75, T.dialGlass + 1.15, 0.7, 0.45, easeInOut);
};

export const SWATCH_KEYS: [number, number][] = [
  [0, 2],
  [8.6, 1],
  [9.0, 0],
  [9.45, 2],
];

export const LookPage: React.FC<{ t: number; pal: Palette }> = ({ t, pal }) => {
  const ring = slideBetween(t, SWATCH_KEYS);
  const selected = stepAt(t, SWATCH_KEYS);
  const amount = glassAmountAt(t);
  const glassFocus = prog(t, T.dialGlass - 0.1, T.dialGlass + 0.25) * (1 - prog(t, T.tilt - 0.1, T.tilt + 0.3));
  const tiltFocus = prog(t, T.tilt - 0.1, T.tilt + 0.25);
  const tiltOn = prog(t, T.tilt + 0.05, T.tilt + 0.3);
  const SW = 46;
  return (
    <div>
      <SheetHeader title="Look" />
      <Card style={{ marginBottom: 8 }}>
        <Label style={{ marginBottom: 9 }}>Colour</Label>
        <div style={{ position: "relative", display: "flex" }}>
          <div
            style={{
              position: "absolute",
              left: ring * SW + SW / 2 - 17,
              top: -3,
              width: 34,
              height: 34,
              borderRadius: 17,
              border: "1.6px solid #fff",
            }}
          />
          {SWATCHES.map((s, i) => (
            <div key={s.name} style={{ width: SW, display: "flex", flexDirection: "column", alignItems: "center" }}>
              <div style={{ width: 28, height: 28, borderRadius: 14, background: `linear-gradient(135deg, ${s.c1}, ${s.c2})` }} />
              <div style={{ fontSize: 6.5, marginTop: 6, color: i === selected ? "#fff" : "rgba(255,255,255,0.55)", fontWeight: i === selected ? 700 : 500 }}>
                {s.name}
              </div>
            </div>
          ))}
        </div>
        <div style={{ marginTop: 10 }}>
          <Segmented options={["Auto", "Light", "Dark"]} pos={2} accent={pal.accent} />
        </div>
      </Card>

      <Card
        style={{
          marginBottom: 8,
          scale: interpolate(glassFocus, [0, 1], [1, 1.035]),
          border: `0.8px solid ${interpolateColors(glassFocus, [0, 1], ["rgba(255,255,255,0.07)", pal.accent])}`,
          boxShadow: `0 0 ${18 * glassFocus}px ${pal.accent}55`,
        }}
      >
        <div style={{ position: "relative", height: 28, borderRadius: 10, background: "rgba(255,255,255,0.07)", overflow: "hidden" }}>
          <div style={{ position: "absolute", left: 0, top: 0, bottom: 0, width: `${amount * 100}%`, background: `linear-gradient(90deg, ${pal.accent}33, ${pal.accent}aa)` }} />
          <div style={{ position: "absolute", left: 10, right: 10, top: 0, bottom: 0, ...row }}>
            <span style={title}>Glass</span>
            <span style={{ fontSize: 9, color: "#fff", fontWeight: 600 }}>{Math.round(amount * 100)}%</span>
          </div>
        </div>
      </Card>

      <Card
        style={{
          marginBottom: 8,
          scale: interpolate(tiltFocus * (1 - prog(t, T.homeSheetIn - 0.3, T.homeSheetIn)), [0, 1], [1, 1.035]),
          border: `0.8px solid ${interpolateColors(tiltFocus, [0, 1], ["rgba(255,255,255,0.07)", pal.accent])}`,
        }}
      >
        <div style={row}>
          <div>
            <div style={title}>Tilt shine</div>
            <div style={sub}>
              Light moves across the glass as you tilt
              <br />
              the phone
            </div>
          </div>
          <Toggle on={tiltOn} accent={pal.accent} />
        </div>
      </Card>

      <Card>
        <Label style={{ marginBottom: 8 }}>Opening apps</Label>
        <Segmented options={["Colour", "Liquid glass"]} pos={0} accent={pal.accent} />
        <div style={{ ...sub, marginTop: 8 }}>The icon grows into a card in its own colour.</div>
      </Card>
    </div>
  );
};

export const GRID_KEYS: [number, number][] = [
  [0, 1],
  [13.62, 2],
  [13.95, 0],
  [14.25, 1],
];
export const SHAPE_KEYS: [number, number][] = [
  [0, 2],
  [14.38, 1],
  [14.72, 3],
  [15.05, 0],
];
// Squircle, Circle, Rounded, Square
export const SHAPE_RADIUS = [0.34, 0.5, 0.24, 0.08];

export const shapeRadiusAt = (t: number) => {
  let v = SHAPE_RADIUS[SHAPE_KEYS[0][1]];
  for (let i = 1; i < SHAPE_KEYS.length; i++) {
    const [at, idx] = SHAPE_KEYS[i];
    if (t >= at) v = tw(t, at, at + 0.35, SHAPE_RADIUS[SHAPE_KEYS[i - 1][1]], SHAPE_RADIUS[idx], easeInOut);
  }
  return v;
};

export const gridScaleAt = (t: number) => {
  const scales = [0.86, 1, 1.12];
  let v = scales[GRID_KEYS[0][1]];
  for (let i = 1; i < GRID_KEYS.length; i++) {
    const [at, idx] = GRID_KEYS[i];
    if (t >= at) v = tw(t, at, at + 0.4, scales[GRID_KEYS[i - 1][1]], scales[idx], springy);
  }
  return v;
};

export const HomePage: React.FC<{ t: number; pal: Palette }> = ({ t, pal }) => {
  const grid = slideBetween(t, GRID_KEYS);
  const shape = slideBetween(t, SHAPE_KEYS);
  const shapeIdx = stepAt(t, SHAPE_KEYS);
  const SW = 62;
  return (
    <div>
      <SheetHeader title="Home screen" />
      <Card style={{ marginBottom: 8 }}>
        <Label style={{ marginBottom: 8 }}>Grid</Label>
        <Segmented options={["Compact", "Balanced", "Roomy"]} pos={grid} accent={pal.accent} />
        <div style={{ ...sub, marginTop: 7 }}>
          {["5 across, 6 down", "4 across, 5 down", "3 across, 4 down"][Math.round(grid)]} on this screen. Icons size themselves to fit.
        </div>
      </Card>
      <Card style={{ marginBottom: 8 }}>
        <Label style={{ marginBottom: 8 }}>Icon shape</Label>
        <div style={{ position: "relative", display: "flex", gap: 0 }}>
          <div
            style={{
              position: "absolute",
              left: shape * SW + 9,
              top: 0,
              width: 32,
              height: 32,
              borderRadius: 32 * shapeRadiusAt(t),
              background: pal.accent,
              boxShadow: `0 3px 12px ${pal.accent}77`,
            }}
          />
          {["Squircle", "Circle", "Rounded", "Square"].map((s, i) => (
            <div key={s} style={{ width: SW, display: "flex", flexDirection: "column", alignItems: "flex-start", paddingLeft: 9 }}>
              <div style={{ width: 32, height: 32, borderRadius: 32 * SHAPE_RADIUS[i], background: "rgba(255,255,255,0.12)" }} />
              <div style={{ fontSize: 7, marginTop: 5, width: 32, textAlign: "center", color: i === shapeIdx ? "#fff" : "rgba(255,255,255,0.55)", fontWeight: i === shapeIdx ? 700 : 500 }}>
                {s}
              </div>
            </div>
          ))}
        </div>
      </Card>
      <Card style={{ padding: "4px 12px" }}>
        {[
          ["Labels", "App names under the icons"],
          ["Dock", "Its apps are kept while it is hidden"],
          ["Search bar", "Above the dock, for your apps and the web"],
        ].map(([a, b], i) => (
          <div key={a} style={{ ...row, padding: "7px 0", borderTop: i ? "0.5px solid rgba(255,255,255,0.07)" : undefined }}>
            <div>
              <div style={title}>{a}</div>
              <div style={sub}>{b}</div>
            </div>
            <Toggle on={1} accent={pal.accent} />
          </div>
        ))}
      </Card>
    </div>
  );
};

export const auraValueAt = (t: number) => Math.round(tw(t, T.stackAura, T.stackAura + 0.95, 35, 160, easeInOut));

export const AuraPage: React.FC<{ t: number; pal: Palette }> = ({ t, pal }) => {
  const aura = auraValueAt(t);
  const streak = prog(t, T.stackAura, T.stackAura + 0.4);
  const unlock = prog(t, T.unlockThemes, T.unlockThemes + 0.55, springy);
  const bump = 1 + 0.12 * Math.sin(Math.PI * prog(t, T.stackAura + 0.85, T.stackAura + 1.2));
  const themes = [
    { n: "Ember", d: "Banked coals under dark iron", c: "#ff8a4c", cost: 150 },
    { n: "Tide", d: "Deep water with a green shore", c: "#2fd1a4", cost: 250 },
    { n: "Orchid", d: "Violet bloom on near-black", c: "#c46cf0", cost: 350 },
    { n: "Solstice", d: "Low sun, long gold shadows", c: "#ffb347", cost: 500 },
  ];
  return (
    <div>
      <SheetHeader title="Aura" back={false} />
      <div
        style={{
          borderRadius: 16,
          padding: "14px 14px",
          background: `linear-gradient(135deg, ${pal.accent}38, rgba(255,255,255,0.05))`,
          border: "0.6px solid rgba(255,255,255,0.14)",
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: 10,
        }}
      >
        <div>
          <div style={{ fontSize: 24, fontWeight: 700, color: "#fff", lineHeight: 1, scale: bump, transformOrigin: "left center" }}>{aura}</div>
          <div style={{ fontSize: 7.5, color: "rgba(255,255,255,0.65)", marginTop: 4 }}>aura</div>
        </div>
        <div style={{ textAlign: "right" }}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "flex-end", gap: 3, fontSize: 9, fontWeight: 700, color: "#fff" }}>
            <div style={{ scale: 1 + streak * 0.25 }}>
              <FlameGlyph size={11} color={interpolateColors(streak, [0, 1], ["#ff8a4c", "#ffb347"])} />
            </div>
            {streak < 0.5 ? "No streak yet" : "7-day streak"}
          </div>
          <div style={{ fontSize: 7.5, color: "rgba(255,255,255,0.6)", marginTop: 4 }}>Tomorrow: +{streak < 0.5 ? 10 : 40}</div>
        </div>
      </div>
      <div style={{ ...sub, fontSize: 8, marginBottom: 12, padding: "0 2px" }}>
        Open Zenith once a day to keep the streak. Each day in a row is worth more, up to 50. Miss a day and it starts again — nothing
        else is lost.
      </div>
      <Label style={{ fontSize: 7, letterSpacing: 1, marginBottom: 7, paddingLeft: 2 }}>THEMES</Label>
      {themes.map((th, i) => {
        const u = i === 0 ? unlock : 0;
        return (
          <div
            key={th.n}
            style={{
              ...row,
              padding: "9px 12px",
              borderRadius: 14,
              marginBottom: 6,
              background: i === 0 ? `linear-gradient(90deg, ${th.c}${Math.round(u * 60).toString(16).padStart(2, "0")}, rgba(255,255,255,0.055))` : "rgba(255,255,255,0.055)",
              border: `0.6px solid ${i === 0 ? interpolateColors(u, [0, 1], ["rgba(255,255,255,0.07)", th.c]) : "rgba(255,255,255,0.07)"}`,
              scale: 1 + 0.04 * Math.sin(Math.PI * Math.min(u, 1)),
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <div style={{ width: 24, height: 24, borderRadius: 12, background: th.c, position: "relative", display: "flex", alignItems: "center", justifyContent: "center" }}>
                <div style={{ position: "absolute", opacity: 1 - u, scale: 1 - u * 0.6 }}>
                  <LockGlyph size={11} color="rgba(40,20,10,0.75)" />
                </div>
                <div style={{ position: "absolute", opacity: u, scale: 0.4 + u * 0.6 }}>
                  <CheckGlyph size={13} color="#2a1206" />
                </div>
              </div>
              <div>
                <div style={title}>{th.n}</div>
                <div style={sub}>{th.d}</div>
              </div>
            </div>
            <div style={{ fontSize: 8.5, fontWeight: 700, color: u > 0.5 ? th.c : "rgba(255,255,255,0.75)" }}>{u > 0.5 ? "Unlocked" : th.cost}</div>
          </div>
        );
      })}
    </div>
  );
};

export const PlusPage: React.FC<{ t: number; pal: Palette }> = ({ t, pal }) => {
  const shine = tw(t, T.zenithPlus + 0.1, T.zenithPlus + 0.9, -60, 160);
  const unlocks = [T.themeMaker, T.hiddenApps, T.freePlacement, T.freePlacement + 0.35, T.freePlacement + 0.5];
  const u = unlocks.map((at) => prog(t, at, at + 0.45, springy));
  const lockTile = (p: number) => (
    <div style={{ width: 24, height: 24, borderRadius: 8, background: interpolateColors(Math.min(p, 1), [0, 1], ["rgba(255,255,255,0.1)", pal.accent]), position: "relative", display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0 }}>
      <div style={{ position: "absolute", opacity: 1 - Math.min(p, 1), scale: 1 + p * 0.4 }}>
        <LockGlyph size={11} />
      </div>
      <div style={{ position: "absolute", opacity: Math.min(p, 1), scale: 0.4 + p * 0.6 }}>
        <CheckGlyph size={13} color="#1d0c08" />
      </div>
    </div>
  );
  const item = (p: number, a: string, b: string, i: number, right?: React.ReactNode) => (
    <div key={a} style={{ ...row, padding: "8px 0", borderTop: i ? "0.5px solid rgba(255,255,255,0.07)" : undefined }}>
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        {right ? null : lockTile(p)}
        <div>
          <div style={title}>{a}</div>
          <div style={sub}>{b}</div>
        </div>
      </div>
      {right}
    </div>
  );
  return (
    <div>
      <SheetHeader title="Zenith Plus" />
      <div
        style={{
          position: "relative",
          overflow: "hidden",
          borderRadius: 14,
          padding: "11px 12px",
          background: "rgba(255,255,255,0.07)",
          border: "0.6px solid rgba(255,255,255,0.14)",
          ...row,
          marginBottom: 8,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <div style={{ width: 26, height: 26, borderRadius: 8, background: "linear-gradient(135deg,#ffd36b,#ffaa2b)", display: "flex", alignItems: "center", justifyContent: "center" }}>
            <SparkleGlyph size={15} />
          </div>
          <div>
            <div style={title}>Try Plus free for 7 days</div>
            <div style={sub}>$1.99 a month or $14.99 once</div>
          </div>
        </div>
        <div style={{ color: "rgba(255,255,255,0.5)", fontSize: 12 }}>›</div>
        <div
          style={{
            position: "absolute",
            top: -20,
            bottom: -20,
            left: `${shine}%`,
            width: 50,
            rotate: "20deg",
            background: "linear-gradient(90deg, transparent, rgba(255,255,255,0.35), transparent)",
          }}
        />
      </div>
      <Card style={{ padding: "2px 12px", marginBottom: 8 }}>
        {item(u[0], "Theme maker", "Build a palette of your own", 0)}
        {item(u[1], "Hidden apps", "Keep chosen apps out of the drawer", 1)}
        {item(0, "Free placement", "Plus: put apps anywhere, any widget size", 2, <Toggle on={u[2]} accent={pal.accent} />)}
      </Card>
      <Card style={{ padding: "2px 12px" }}>
        {item(u[3], "Back up home screen", "Save everything to a file", 0)}
        {item(u[4], "Restore from a backup", "Replaces your current layout", 1)}
      </Card>
      <div style={{ ...sub, marginTop: 8, paddingLeft: 4 }}>Locked items open Zenith Plus.</div>
    </div>
  );
};
