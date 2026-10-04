import React from "react";
import { interpolate } from "remotion";
import { prog, springy, window01 } from "../zenith/anim";
import { GlassIcon, GradientLine, headline } from "../zenith/Scenes";
import { T2, type Pal2 } from "./theme2";

type Cap = { at: number; out: number; l1: string; l2?: string; dull?: boolean };

const CAPTIONS: Cap[] = [
  { at: T2.launchersSit, out: T2.zenithMoves - 0.12, l1: "Most launchers", l2: "just sit there.", dull: true },
  { at: T2.zenithMoves, out: T2.tapApp - 0.15, l1: "Zenith", l2: "moves." },
  { at: T2.tapApp, out: T2.liquidGlass - 0.15, l1: "Tap an app.", l2: "Its own colour." },
  { at: T2.liquidGlass, out: T2.swipeUp - 0.05, l1: "Or go full", l2: "liquid glass." },
  { at: T2.swipeUp + 0.05, out: T2.control - 0.12, l1: "Every app,", l2: "one search away." },
  { at: T2.control, out: T2.anywhere - 0.12, l1: "Want control?" },
  { at: T2.anywhere, out: T2.anySize - 0.12, l1: "Put apps", l2: "anywhere." },
  { at: T2.anySize, out: T2.hideLine - 0.12, l1: "Any size." },
  { at: T2.hideLine, out: T2.themeLine - 0.12, l1: "Hide", l2: "any app." },
  { at: T2.themeLine, out: T2.backup - 0.05, l1: "Build your", l2: "own theme." },
  { at: T2.backup + 0.04, out: T2.outro - 0.1, l1: "Back it", l2: "all up." },
];

/** Headline captions above the phone. */
export const Captions2: React.FC<{ t: number; pal: Pal2 }> = ({ t, pal }) => (
  <>
    {CAPTIONS.map((c) => {
      if (t < c.at - 0.1 || t > c.out + 0.5) return null;
      const v = window01(t, c.at, c.out, 0.4, 0.28);
      const p = prog(t, c.at, c.at + 0.6, springy);
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
            filter: v < 0.999 ? `blur(${(1 - v) * 14}px)` : undefined,
            translate: `0px ${interpolate(p, [0, 1], [40, 0])}px`,
          }}
        >
          <div style={{ ...headline, fontSize: 92 }}>{c.l1}</div>
          {c.l2 ? (
            c.dull ? (
              <div style={{ ...headline, fontSize: 92, color: "#8d929c" }}>{c.l2}</div>
            ) : (
              <GradientLine id={`g2-${String(c.at).replace(".", "-")}`} text={c.l2} from={pal.accent} to={pal.accent2} fontSize={92} />
            )
          ) : null}
        </div>
      );
    })}
  </>
);

/** End card: icon, wordmark, tagline. */
export const Outro2: React.FC<{ t: number; pal: Pal2 }> = ({ t, pal }) => {
  if (t < T2.outro - 0.05) return null;
  const p = prog(t, T2.outro, T2.outro + 0.9, springy);
  const word = prog(t, T2.zenithWord - 0.05, T2.zenithWord + 0.5);
  const tag1 = prog(t, T2.finallyYours - 0.05, T2.finallyYours + 0.5);
  const tag2 = prog(t, T2.finallyYours + 0.75, T2.finallyYours + 1.25);
  const small = prog(t, T2.finallyYours + 2.2, T2.finallyYours + 2.8);
  const fadeOut = prog(t, T2.end - 0.35, T2.end);
  const float = Math.sin((t - T2.outro) * 1.4) * 10;
  return (
    <div style={{ position: "absolute", inset: 0, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", opacity: 1 - fadeOut }}>
      <div style={{ position: "absolute", width: 1000, height: 1000, borderRadius: "50%", background: `radial-gradient(circle, ${pal.accent}50, ${pal.accent2}26 40%, transparent 70%)`, opacity: p, top: 230 }} />
      <div style={{ scale: interpolate(p, [0, 1], [0.3, 1]), opacity: Math.min(1, p * 1.5), filter: `blur(${(1 - Math.min(p, 1)) * 20}px)`, translate: `0px ${float}px` }}>
        <GlassIcon size={380} shineAt={T2.outro + 0.25} t={t} />
      </div>
      <div style={{ ...headline, fontSize: 150, marginTop: 44, opacity: word, letterSpacing: interpolate(word, [0, 1], [24, -4]), filter: `blur(${(1 - word) * 12}px)` }}>
        Zenith
      </div>
      <div style={{ ...headline, fontSize: 84, marginTop: 10, opacity: tag1, translate: `0px ${(1 - tag1) * 30}px` }}>Your home screen.</div>
      <div style={{ width: "100%", opacity: tag2, translate: `0px ${(1 - tag2) * 30}px` }}>
        <GradientLine id="g2-yours" text="Finally yours." from={pal.accent} to={pal.accent2} fontSize={84} />
      </div>
      <div style={{ fontFamily: "Inter", fontSize: 44, fontWeight: 500, color: "rgba(255,255,255,0.7)", marginTop: 36, opacity: small }}>
        Liquid glass launcher for Android
      </div>
    </div>
  );
};
