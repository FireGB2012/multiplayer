import React from "react";
import { AbsoluteFill, interpolate, random, staticFile, useCurrentFrame, useVideoConfig } from "remotion";
import { Audio } from "@remotion/media";
import { easeIn, easeInOut, easeOut, prog, springy, useTime } from "../zenith/anim";
import { GlassIcon, GradientLine } from "../zenith/Scenes";
import { Phone } from "../zenith/Phone";
import { paletteAt } from "../zenith/ZenithPromo";
import { Phone2 } from "../zenith2/Phone2";
import { paletteAt2 } from "../zenith2/theme2";
import { Phone3 } from "../zenith3/Phone3";
import { paletteAt3 } from "../zenith3/t3";
import { IMPACT_FRAME, OrbitCanvas, ShatterCanvas } from "./Glass3D";
import { ICON_CENTER, IMPACTS, IRIS, TT } from "./tt";

export type TrailerProps = { readonly launch: "date" | "now" };

const remap = (t: number, a: number, b: number, c: number, d: number) => c + ((t - a) / (b - a)) * (d - c);

// ---------------------------------------------------------------- montage shots

type Shot = {
  word: string;
  focus: [number, number]; // frame px the camera centres on
  z: [number, number]; // push-in from / to
  ry: [number, number]; // camera yaw drift
  render: (local: number) => React.ReactNode; // local 0..1 within the shot
};

const SHOTS: Shot[] = [
  {
    word: "LIQUID GLASS",
    focus: [540, 1120],
    z: [1.02, 1.12],
    ry: [-8, 4],
    render: (l) => {
      const t2 = remap(l, 0, 1, 7.5, 8.55);
      return <Phone2 t={t2} pal={paletteAt2(t2)} />;
    },
  },
  {
    word: "EVERY COLOUR",
    focus: [540, 900],
    z: [1.12, 1.26],
    ry: [6, -4],
    render: (l) => {
      const t3 = remap(l, 0, 1, 6.75, 8.1);
      return <Phone3 t={t3} pal={paletteAt3(t3)} />;
    },
  },
  {
    word: "TILT TO SHINE",
    focus: [540, 1160],
    z: [1.0, 1.08],
    ry: [0, 0],
    render: (l) => {
      const t1 = remap(l, 0, 1, 11.25, 12.95);
      return <Phone t={t1} pal={paletteAt(t1)} />;
    },
  },
  {
    word: "ANYWHERE",
    focus: [540, 1120],
    z: [1.12, 1.22],
    ry: [-6, 3],
    render: (l) => {
      const t3 = remap(l, 0, 1, 8.95, 9.98);
      return <Phone3 t={t3} pal={paletteAt3(t3)} />;
    },
  },
  {
    word: "ANY SIZE",
    focus: [650, 860],
    z: [1.28, 1.42],
    ry: [5, -3],
    render: (l) => {
      const t3 = remap(l, 0, 1, 10.35, 11.6);
      return <Phone3 t={t3} pal={paletteAt3(t3)} />;
    },
  },
  {
    word: "HIDDEN",
    focus: [420, 800],
    z: [1.4, 1.55],
    ry: [-5, 4],
    render: (l) => {
      const t2 = remap(l, 0, 1, 17.2, 17.84);
      return <Phone2 t={t2} pal={paletteAt2(t2)} />;
    },
  },
  {
    word: "YOURS",
    focus: [540, 830],
    z: [1.45, 1.62],
    ry: [4, -2],
    render: (l) => {
      const t2 = remap(l, 0, 1, 3.5, 4.15);
      return <Phone2 t={t2} pal={paletteAt2(t2)} />;
    },
  },
];

const Montage: React.FC<{ t: number }> = ({ t }) => {
  if (t < TT.montage || t >= TT.silence) return null;
  const i = Math.min(SHOTS.length - 1, Math.floor((t - TT.montage) / TT.shotLen));
  const at = TT.montage + i * TT.shotLen;
  // the last shot holds through the drum fill and cuts out on the silence
  const len = i === SHOTS.length - 1 ? TT.silence - at : TT.shotLen;
  const local = (t - at) / len;
  const s = SHOTS[i];
  const inK = prog(t, at, at + 0.14, easeOut);
  const outK = prog(t, at + len - 0.1, at + len, easeIn);
  const z = interpolate(local, [0, 1], s.z);
  const ry = interpolate(local, [0, 1], s.ry);
  const [fx, fy] = s.focus;
  const dir = i % 2 ? -1 : 1;
  const wordIn = prog(t, at + 0.04, at + 0.18, easeOut);
  const wordSize = Math.min(132, 900 / (s.word.length * 0.66));
  return (
    <div style={{ position: "absolute", inset: 0, perspective: 2200 }}>
      <div
        style={{
          position: "absolute",
          inset: 0,
          transformOrigin: `${fx}px ${fy}px`,
          transform: `translate(${540 - fx + dir * ((1 - inK) * 300 - outK * 300)}px, ${1180 - fy}px) scale(${z}) rotateY(${ry}deg)`,
          filter: inK < 1 || outK > 0 ? `blur(${(1 - inK) * 18 + outK * 18}px)` : undefined,
          opacity: Math.min(1, inK * 1.6),
        }}
      >
        {s.render(local)}
      </div>
      {/* darken the top so the word reads */}
      <div style={{ position: "absolute", left: 0, right: 0, top: 0, height: 520, background: "linear-gradient(180deg, rgba(0,0,0,0.85), transparent)" }} />
      <div
        style={{
          position: "absolute",
          left: 40,
          right: 40,
          top: 150,
          height: 220,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          fontFamily: "Inter",
          fontWeight: 800,
          fontSize: wordSize,
          letterSpacing: 4,
          color: "#fff",
          textShadow: "0 0 40px rgba(180,200,255,0.55)",
          opacity: wordIn * (1 - outK),
          scale: interpolate(wordIn, [0, 1], [1.35, 1]),
          filter: wordIn < 1 ? `blur(${(1 - wordIn) * 12}px)` : undefined,
        }}
      >
        {s.word}
      </div>
      <div style={{ position: "absolute", inset: 0, background: "#fff", opacity: 0.22 * (1 - prog(t, at, at + 0.12)) }} />
    </div>
  );
};

// ---------------------------------------------------------------- intro

const IntroLines: React.FC<{ t: number }> = ({ t }) => {
  if (t > TT.crack + 0.1) return null;
  const line = (at: number, text: string, color: string, delayOut: number) => {
    const p = prog(t, at, at + 0.7, easeOut);
    const o = prog(t, TT.linesOut + delayOut, TT.linesOut + delayOut + 0.3, easeIn);
    return (
      <div
        style={{
          fontFamily: "Inter",
          fontWeight: 500,
          fontSize: 68,
          letterSpacing: 1,
          color,
          opacity: p * (1 - o),
          translate: `0px ${(1 - p) * 30 - o * 20}px`,
          filter: p < 1 || o > 0 ? `blur(${(1 - p) * 10 + o * 12}px)` : undefined,
        }}
      >
        {text}
      </div>
    );
  };
  return (
    <>
      <div style={{ position: "absolute", left: 0, right: 0, top: 120, height: 200, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: 10 }}>
        {line(TT.line1, "Your home screen", "rgba(255,255,255,0.92)", 0)}
        {line(TT.line2, "hasn't changed in years.", "#8b909b", 0.06)}
      </div>
    </>
  );
};

const UntilNow: React.FC<{ t: number }> = ({ t }) => {
  if (t < TT.untilNow - 0.05 || t > TT.untilNowOut + 0.4) return null;
  const p = prog(t, TT.untilNow, TT.untilNow + 0.16, easeOut);
  const o = prog(t, TT.untilNowOut, TT.untilNowOut + 0.35, easeIn);
  return (
    <div style={{ position: "absolute", inset: 0, display: "flex", alignItems: "center", justifyContent: "center" }}>
      <div
        style={{
          fontFamily: "Inter",
          fontWeight: 800,
          fontSize: 140,
          letterSpacing: -3,
          color: "#fff",
          textShadow: "0 0 60px rgba(190,210,255,0.6)",
          opacity: p * (1 - o),
          scale: interpolate(p, [0, 1], [1.4, 1]) * (1 + o * 0.15),
          filter: p < 1 || o > 0 ? `blur(${(1 - p) * 16 + o * 18}px)` : undefined,
        }}
      >
        Until now.
      </div>
    </div>
  );
};

/** The icon forms where the shards converge, then lifts away into the montage. */
const IconForm: React.FC<{ t: number }> = ({ t }) => {
  if (t < TT.icon - 0.05 || t > TT.montage + 0.05) return null;
  const p = prog(t, TT.icon, TT.icon + 0.6, springy);
  const away = prog(t, TT.iconAway, TT.montage, easeIn);
  const ring = prog(t, TT.icon, TT.icon + 0.8, easeOut);
  return (
    <>
      <div
        style={{
          position: "absolute",
          left: ICON_CENTER.x - 700 * ring,
          top: ICON_CENTER.y - 700 * ring,
          width: 1400 * ring,
          height: 1400 * ring,
          borderRadius: "50%",
          border: "4px solid rgba(220,230,255,0.9)",
          boxShadow: `0 0 60px ${IRIS[1]}, inset 0 0 60px ${IRIS[0]}88`,
          opacity: (1 - ring) * 0.8,
        }}
      />
      <div style={{ position: "absolute", left: 0, top: ICON_CENTER.y - 540, width: 1080, height: 1080, background: `radial-gradient(circle, ${IRIS[1]}66, ${IRIS[0]}22 35%, transparent 65%)`, opacity: p * (1 - away) }} />
      <div
        style={{
          position: "absolute",
          left: ICON_CENTER.x - 190,
          top: ICON_CENTER.y - 195,
          scale: interpolate(Math.min(p, 1.2), [0, 1], [0.5, 1]) * (1 - away * 0.4),
          opacity: Math.min(1, p * 2) * (1 - away),
          translate: `0px ${-away * 260}px`,
          filter: away > 0 ? `blur(${away * 16}px)` : undefined,
        }}
      >
        <GlassIcon size={380} shineAt={TT.icon + 0.1} t={t} />
      </div>
      <div style={{ position: "absolute", inset: 0, background: "#fff", opacity: 0.55 * (1 - prog(t, TT.icon, TT.icon + 0.25)) }} />
    </>
  );
};

/** A camera-flash pop on impact plus a small hot spark where it hit. */
const ImpactFlash: React.FC<{ t: number }> = ({ t }) => {
  if (t < TT.crack || t > TT.crack + 0.3) return null;
  const k = 1 - prog(t, TT.crack, TT.crack + 0.16, easeOut);
  const spark = 1 - prog(t, TT.crack, TT.crack + 0.22, easeOut);
  return (
    <>
      <div style={{ position: "absolute", inset: 0, background: "#fff", opacity: 0.28 * k }} />
      <div
        style={{
          position: "absolute",
          left: IMPACT_FRAME.x - 60,
          top: IMPACT_FRAME.y - 60,
          width: 120,
          height: 120,
          borderRadius: "50%",
          background: "radial-gradient(circle, #fff 0%, rgba(210,230,255,0.6) 25%, transparent 70%)",
          opacity: spark,
          scale: 0.6 + (1 - spark) * 0.8,
        }}
      />
    </>
  );
};

// ---------------------------------------------------------------- hero + end card

const WORD = "ZENITH";

const Hero: React.FC<{ t: number; launch: TrailerProps["launch"] }> = ({ t, launch }) => {
  if (t < TT.hero - 0.02) return null;
  const p = prog(t, TT.hero, TT.hero + 0.8, springy);
  const tag = prog(t, TT.tagline, TT.tagline + 0.6);
  const date = prog(t, TT.date, TT.date + 0.22, easeOut);
  const sub = prog(t, TT.date + 0.3, TT.date + 0.8);
  const fadeOut = prog(t, TT.end - 0.6, TT.end - 0.05, easeIn);
  const sheen = interpolate(t, [TT.word + 0.8, TT.word + 1.6], [-30, 130], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const iconY = 700;
  const float = Math.sin((t - TT.hero) * 1.3) * 10;
  return (
    <div style={{ position: "absolute", inset: 0, opacity: 1 - fadeOut }}>
      <div style={{ position: "absolute", left: 0, top: iconY - 540, width: 1080, height: 1080, background: `radial-gradient(circle, ${IRIS[1]}55, ${IRIS[2]}1c 38%, transparent 65%)`, opacity: Math.min(1, p) }} />
      <OrbitCanvas iconFrameY={iconY} />
      <div
        style={{
          position: "absolute",
          left: 540 - 200,
          top: iconY - 205 + float,
          scale: interpolate(Math.min(p, 1.15), [0, 1], [1.5, 1]),
          opacity: Math.min(1, p * 2),
          filter: p < 1 ? `blur(${(1 - Math.min(p, 1)) * 24}px)` : undefined,
        }}
      >
        <GlassIcon size={400} shineAt={TT.hero + 0.5} t={t} />
      </div>
      {/* wordmark, letter by letter */}
      <div style={{ position: "absolute", left: 0, right: 0, top: 1000, display: "flex", justifyContent: "center", gap: 18 }}>
        {WORD.split("").map((ch, i) => {
          const lp = prog(t, TT.word + i * 0.07, TT.word + i * 0.07 + 0.45, easeOut);
          return (
            <span
              key={i}
              style={{
                fontFamily: "Inter",
                fontWeight: 800,
                fontSize: 170,
                color: "#fff",
                opacity: lp,
                translate: `0px ${(1 - lp) * 40}px`,
                filter: lp < 1 ? `blur(${(1 - lp) * 14}px)` : undefined,
                textShadow: "0 0 50px rgba(190,210,255,0.45)",
              }}
            >
              {ch}
            </span>
          );
        })}
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: 1000, height: 210, background: `linear-gradient(105deg, transparent ${sheen - 12}%, rgba(255,255,255,0.55) ${sheen}%, transparent ${sheen + 12}%)`, mixBlendMode: "overlay" }} />
      <div style={{ position: "absolute", left: 0, right: 0, top: 1215, textAlign: "center", fontFamily: "Inter", fontWeight: 500, fontSize: 38, letterSpacing: 12, color: "rgba(255,255,255,0.72)", opacity: tag, translate: `0px ${(1 - tag) * 16}px` }}>
        THE LIQUID GLASS LAUNCHER
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: 1370, opacity: date, scale: interpolate(date, [0, 1], [1.3, 1]), filter: date < 1 ? `blur(${(1 - date) * 14}px)` : undefined }}>
        <GradientLine id="trailer-date" text={launch === "now" ? "OUT NOW" : "OCTOBER 12"} from={IRIS[0]} to={IRIS[2]} fontSize={124} />
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: 1535, textAlign: "center", fontFamily: "Inter", fontWeight: 600, fontSize: 40, letterSpacing: 10, color: "#fff", opacity: sub }}>
        ON GOOGLE PLAY
      </div>
    </div>
  );
};

// ---------------------------------------------------------------- atmosphere

const Atmosphere: React.FC<{ t: number }> = ({ t }) => {
  const streak = (top: number, color: string, op: number, phase: number) => (
    <div
      style={{
        position: "absolute",
        left: -400 + ((t * 60 + phase * 300) % 1900) - 300,
        top,
        width: 1400,
        height: 40,
        rotate: "-24deg",
        background: `linear-gradient(90deg, transparent, ${color}, transparent)`,
        filter: "blur(24px)",
        opacity: op,
      }}
    />
  );
  return (
    <>
      <div style={{ position: "absolute", inset: 0, background: "radial-gradient(ellipse at 50% 45%, #10101c 0%, #050508 70%)" }} />
      {streak(500, IRIS[0], 0.12, 0)}
      {streak(1100, IRIS[2], 0.1, 1.7)}
      {streak(1500, IRIS[1], 0.1, 3.1)}
      {new Array(40).fill(0).map((_, i) => {
        const x = random(`dx${i}`) * 1080;
        const y = (random(`dy${i}`) * 1920 - t * (8 + random(`dv${i}`) * 18)) % 1920;
        return <div key={i} style={{ position: "absolute", left: x, top: y < 0 ? y + 1920 : y, width: 3, height: 3, borderRadius: 2, background: "#fff", opacity: 0.08 + random(`do${i}`) * 0.18 }} />;
      })}
    </>
  );
};

const Grain: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <div
      style={{
        position: "absolute",
        inset: 0,
        backgroundImage: `url(${staticFile("grain.png")})`,
        backgroundPosition: `${Math.floor(random(`gx${frame}`) * 512)}px ${Math.floor(random(`gy${frame}`) * 512)}px`,
        mixBlendMode: "overlay",
        opacity: 0.09,
        pointerEvents: "none",
      }}
    />
  );
};

const shakeAt = (t: number) => {
  let x = 0;
  let y = 0;
  for (const at of IMPACTS) {
    if (t < at || t > at + 0.45) continue;
    const k = Math.exp(-(t - at) * 9) * 16;
    x += Math.sin((t - at) * 90) * k;
    y += Math.cos((t - at) * 73) * k;
  }
  return `${x.toFixed(1)}px ${y.toFixed(1)}px`;
};

// ---------------------------------------------------------------- sound

const Sfx: React.FC<{ at: number; src: string; volume?: number }> = ({ at, src, volume = 0.3 }) => {
  const { fps } = useVideoConfig();
  return <Audio name={src} src={staticFile(`sfx/${src}.wav`)} from={Math.round(at * fps)} premountFor={fps} volume={volume} />;
};

const TrailerSound: React.FC = () => (
  <>
    <Audio name="Score" src={staticFile("trailer_music.wav")} volume={0.62} />
    <Sfx at={TT.crack} src="impactGlass_heavy_000" volume={0.36} />
    <Sfx at={TT.crack + 0.07} src="impactGlass_heavy_002" volume={0.26} />
    <Sfx at={TT.explode - 0.12} src="impactGlass_light_001" volume={0.35} />
    <Sfx at={TT.explode + 0.05} src="impactGlass_light_003" volume={0.3} />
    <Sfx at={TT.icon - 0.5} src="impactGlass_medium_001" volume={0.3} />
    <Sfx at={TT.icon} src="glass_004" volume={0.5} />
    {SHOTS.map((_, i) => (
      <Sfx key={i} at={TT.montage + i * TT.shotLen - 0.05} src="whoosh-short" volume={0.22} />
    ))}
    <Sfx at={TT.hero} src="impact" volume={0.3} />
    <Sfx at={TT.word + 0.8} src="shimmer" volume={0.25} />
    <Sfx at={TT.date} src="impactGlass_light_001" volume={0.4} />
    <Sfx at={TT.date + 0.02} src="glass_004" volume={0.3} />
  </>
);

// ---------------------------------------------------------------- composition

export const ZenithTrailer: React.FC<TrailerProps> = ({ launch }) => {
  const t = useTime();
  return (
    <AbsoluteFill style={{ background: "#050508", fontFamily: "Inter" }}>
      <div style={{ position: "absolute", inset: 0, translate: shakeAt(t) }}>
        <Atmosphere t={t} />
        <ShatterCanvas />
        <ImpactFlash t={t} />
        <IntroLines t={t} />
        <UntilNow t={t} />
        <IconForm t={t} />
        <Montage t={t} />
        <Hero t={t} launch={launch} />
      </div>
      <AbsoluteFill style={{ background: "radial-gradient(ellipse at 50% 50%, transparent 55%, rgba(0,0,0,0.6) 100%)" }} />
      <Grain />
      <AbsoluteFill style={{ background: "#000", opacity: Math.max(1 - prog(t, 0, 0.5), prog(t, TT.end - 0.4, TT.end - 0.02, easeInOut)) }} />
      <TrailerSound />
    </AbsoluteFill>
  );
};
