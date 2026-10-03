import React from "react";
import { Img, interpolate, staticFile } from "remotion";
import { easeIn, easeInOut, prog, springy, tw, window01 } from "./anim";
import { T } from "./timeline";
import type { Palette } from "./HomeScreen";


/** Full-frame backdrop: deep plum with slow glass-like colour blobs following the theme. */
export const Backdrop: React.FC<{ t: number; pal: Palette }> = ({ t, pal }) => {
  const blob = (color: string, x: number, y: number, size: number, phase: number, opacity: number) => (
    <div
      style={{
        position: "absolute",
        left: x + Math.sin(t * 0.35 + phase) * 60 - size / 2,
        top: y + Math.cos(t * 0.3 + phase) * 50 - size / 2,
        width: size,
        height: size,
        borderRadius: "50%",
        background: color,
        filter: "blur(140px)",
        opacity,
      }}
    />
  );
  return (
    <div style={{ position: "absolute", inset: 0, background: "#09060a", overflow: "hidden" }}>
      {blob(pal.a, 180, 420, 760, 0, 0.55)}
      {blob(pal.b, 900, 1150, 820, 2, 0.5)}
      {blob("#5a6cff", 860, 260, 520, 4, 0.22)}
      {blob(pal.accent, 240, 1650, 600, 1, 0.3)}
      <div style={{ position: "absolute", inset: 0, background: "radial-gradient(ellipse at 50% 45%, transparent 40%, rgba(0,0,0,0.55) 100%)" }} />
    </div>
  );
};

const headline: React.CSSProperties = {
  fontFamily: "Inter",
  fontWeight: 800,
  fontSize: 104,
  letterSpacing: -3,
  color: "#fff",
  lineHeight: 1.05,
  textAlign: "center",
};

const gradientText = (from: string, to: string): React.CSSProperties => ({
  background: `linear-gradient(100deg, ${from}, ${to})`,
  WebkitBackgroundClip: "text",
  backgroundClip: "text",
  color: "transparent",
});

/** A line that fades up out of a blur. */
const BlurIn: React.FC<{ t: number; at: number; out?: number; style?: React.CSSProperties; children: React.ReactNode }> = ({
  t,
  at,
  out,
  style,
  children,
}) => {
  const p = prog(t, at, at + 0.55);
  const o = out === undefined ? 0 : prog(t, out, out + 0.35, easeIn);
  return (
    <div
      style={{
        ...style,
        opacity: p * (1 - o),
        filter: `blur(${(1 - p) * 18 + o * 16}px)`,
        translate: `0px ${interpolate(p, [0, 1], [50, 0]) - o * 40}px`,
        scale: interpolate(o, [0, 1], [1, 1.08]),
      }}
    >
      {children}
    </div>
  );
};

export const Intro: React.FC<{ t: number; pal: Palette }> = ({ t, pal }) => {
  if (t > T.logoIn + 0.5) return null;
  return (
    <div style={{ position: "absolute", inset: 0, display: "flex", flexDirection: "column", justifyContent: "center", alignItems: "center", gap: 18 }}>
      <BlurIn t={t} at={T.yourPhone - 0.1} out={T.introOut} style={headline}>
        Your phone.
      </BlurIn>
      <BlurIn t={t} at={T.yourHomeScreen - 0.1} out={T.introOut + 0.05} style={headline}>
        Your home screen.
      </BlurIn>
      <BlurIn t={t} at={T.yourRules - 0.1} out={T.introOut + 0.1} style={{ ...headline, fontSize: 132, ...gradientText(pal.accent, "#c86bff") }}>
        Your rules.
      </BlurIn>
    </div>
  );
};

/** Glass icon with a light streak sweeping through it (masked by the icon's own alpha). */
export const GlassIcon: React.FC<{ size: number; shineAt: number; t: number }> = ({ size, shineAt, t }) => {
  const x = tw(t, shineAt, shineAt + 0.9, -60, 160, easeInOut);
  return (
    <div style={{ position: "relative", width: size, height: size * (671 / 653) }}>
      <Img src={staticFile("zenith-icon.png")} style={{ width: "100%", height: "100%" }} />
      <div
        style={{
          position: "absolute",
          inset: 0,
          WebkitMaskImage: `url(${staticFile("zenith-icon.png")})`,
          WebkitMaskSize: "100% 100%",
          maskImage: `url(${staticFile("zenith-icon.png")})`,
          maskSize: "100% 100%",
          background: `linear-gradient(115deg, transparent ${x - 18}%, rgba(255,255,255,0.75) ${x}%, transparent ${x + 18}%)`,
          mixBlendMode: "screen",
        }}
      />
    </div>
  );
};

/** "This is Zenith" logo moment; flies up and away as the phone rises. */
export const LogoReveal: React.FC<{ t: number; pal: Palette }> = ({ t, pal }) => {
  if (t < T.logoIn - 0.1 || t > T.phoneIn + 0.9) return null;
  const p = prog(t, T.logoIn, T.logoIn + 0.9, springy);
  const away = prog(t, T.phoneIn - 0.05, T.phoneIn + 0.6, easeInOut);
  const word = prog(t, T.logoIn + 0.3, T.logoIn + 0.95);
  return (
    <div
      style={{
        position: "absolute",
        inset: 0,
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        translate: `0px ${-away * 140}px`,
        scale: interpolate(away, [0, 1], [1, 0.72]),
        opacity: 1 - prog(t, T.phoneIn - 0.05, T.phoneIn + 0.4),
        filter: away > 0 ? `blur(${away * 22}px)` : undefined,
      }}
    >
      <div style={{ position: "absolute", width: 900, height: 900, borderRadius: "50%", background: `radial-gradient(circle, ${pal.accent}55, #7b6bff22 40%, transparent 70%)`, opacity: p, scale: 0.6 + p * 0.4 }} />
      <div style={{ scale: interpolate(p, [0, 1], [0.35, 1]), rotate: `${interpolate(p, [0, 1], [-14, 0])}deg`, filter: `blur(${(1 - Math.min(p, 1)) * 24}px)`, opacity: Math.min(1, p * 1.6) }}>
        <GlassIcon size={440} shineAt={T.logoIn + 0.35} t={t} />
      </div>
      <div style={{ ...headline, fontSize: 150, letterSpacing: interpolate(word, [0, 1], [30, -4]), marginTop: 40, opacity: word, filter: `blur(${(1 - word) * 12}px)` }}>
        Zenith
      </div>
    </div>
  );
};

type Cap = { at: number; out: number; l1: string; l2?: string; small?: string };
const CAPTIONS: Cap[] = [
  { at: T.launcherLine, out: T.lookIn, small: "The Android launcher", l1: "built on", l2: "liquid glass" },
  { at: T.pickColour, out: T.dialGlass - 0.15, l1: "Pick your", l2: "colour" },
  { at: T.dialGlass, out: T.tilt - 0.15, l1: "Dial in", l2: "the glass" },
  { at: T.tilt, out: T.homeSheetIn - 0.05, l1: "Tilt it.", l2: "Light follows." },
  { at: T.grid, out: T.everySize - 0.2, l1: "Your grid.", l2: "Your shapes." },
  { at: T.everySize, out: T.auraIn + 0.05, l1: "Every app.", l2: "Any size." },
  { at: T.auraIn + 0.2, out: T.plusIn + 0.1, l1: "Open daily.", l2: "Earn aura." },
  { at: T.wantMore - 0.05, out: T.zenithPlus - 0.1, l1: "Want more?" },
  { at: T.zenithPlus, out: T.outro - 0.1, l1: "Zenith", l2: "Plus" },
];

/** Headline captions above the phone. */
export const Captions: React.FC<{ t: number; pal: Palette }> = ({ t, pal }) => (
  <>
    {CAPTIONS.map((c) => {
      if (t < c.at - 0.1 || t > c.out + 0.5) return null;
      const v = window01(t, c.at, c.out, 0.45, 0.3);
      const p = prog(t, c.at, c.at + 0.6, springy);
      const isPlus = c.l1 === "Zenith";
      return (
        <div
          key={c.at}
          style={{
            position: "absolute",
            left: 60,
            right: 60,
            top: 0,
            height: 400,
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            opacity: v,
            filter: `blur(${(1 - v) * 14}px)`,
            translate: `0px ${interpolate(p, [0, 1], [40, 0])}px`,
          }}
        >
          {c.small ? (
            <div style={{ fontFamily: "Inter", fontSize: 46, fontWeight: 600, color: "rgba(255,255,255,0.75)", marginBottom: 6 }}>{c.small}</div>
          ) : null}
          <div style={{ ...headline, fontSize: 92 }}>
            {c.l1}
            {c.l2 ? (
              <span style={isPlus ? gradientText("#ffd36b", "#ff9a3c") : gradientText(pal.accent, "#c86bff")}>
                {c.l1.length + c.l2.length > 12 ? <br /> : " "}
                {c.l2}
              </span>
            ) : null}
          </div>
        </div>
      );
    })}
  </>
);

/** End card: icon, wordmark, tagline. */
export const Outro: React.FC<{ t: number; pal: Palette }> = ({ t, pal }) => {
  if (t < T.outro - 0.05) return null;
  const p = prog(t, T.outro, T.outro + 0.9, springy);
  const word = prog(t, T.zenithWord - 0.05, T.zenithWord + 0.5);
  const tag = prog(t, T.makeItYours - 0.05, T.makeItYours + 0.5);
  const small = prog(t, T.makeItYours + 0.9, T.makeItYours + 1.5);
  const fadeOut = prog(t, T.end - 0.35, T.end);
  const float = Math.sin((t - T.outro) * 1.4) * 10;
  return (
    <div style={{ position: "absolute", inset: 0, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", opacity: 1 - fadeOut }}>
      <div style={{ position: "absolute", width: 1000, height: 1000, borderRadius: "50%", background: `radial-gradient(circle, ${pal.accent}50, #7b6bff26 40%, transparent 70%)`, opacity: p, top: 250 }} />
      <div style={{ scale: interpolate(p, [0, 1], [0.3, 1]), opacity: Math.min(1, p * 1.5), filter: `blur(${(1 - Math.min(p, 1)) * 20}px)`, translate: `0px ${float}px` }}>
        <GlassIcon size={400} shineAt={T.outro + 0.25} t={t} />
      </div>
      <div style={{ ...headline, fontSize: 150, marginTop: 50, opacity: word, letterSpacing: interpolate(word, [0, 1], [24, -4]), filter: `blur(${(1 - word) * 12}px)` }}>
        Zenith
      </div>
      <div style={{ ...headline, fontSize: 84, marginTop: 14, opacity: tag, translate: `0px ${(1 - tag) * 30}px`, ...gradientText(pal.accent, "#c86bff") }}>Make it yours.</div>
      <div style={{ fontFamily: "Inter", fontSize: 44, fontWeight: 500, color: "rgba(255,255,255,0.7)", marginTop: 40, opacity: small }}>
        Liquid glass launcher for Android
      </div>
    </div>
  );
};

