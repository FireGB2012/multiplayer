import React from "react";
import { interpolate, random } from "remotion";
import { LockGlyph, SparkleGlyph } from "../zenith/AppIcons";
import { easeIn, easeInOut, easeOut, prog, springy } from "../zenith/anim";
import { GlassIcon, GradientLine, headline } from "../zenith/Scenes";
import { GOLD1, GOLD2, GOLD3, T3 } from "./t3";

type Cap = { at: number; l1: string; l2?: string; price?: boolean; out?: number };

const CAPTIONS: Cap[] = [
  { at: T3.outOfBox, l1: "Looks good", l2: "out of the box." },
  { at: T3.plusLine, l1: "Plus lets you", l2: "go further." },
  { at: T3.buildTheme, l1: "Build your", l2: "own theme." },
  { at: T3.everyColour, l1: "Every colour.", l2: "Exactly yours." },
  { at: T3.anywhere, l1: "Apps", l2: "anywhere." },
  { at: T3.anySize, l1: "Widgets", l2: "any size." },
  { at: T3.hideLine, l1: "Hide apps.", l2: "Shhh." },
  { at: T3.backupLine, l1: "Back up", l2: "everything." },
  { at: T3.bringBack, l1: "Bring it back", l2: "anytime." },
  { at: T3.trialLine, l1: "Try Plus", l2: "free for 7 days." },
  { at: T3.monthly, l1: "$1.99", l2: "a month", price: true },
  { at: T3.once, l1: "or $14.99", l2: "once.", price: true, out: T3.outro - 0.45 },
];

// Each caption finishes fading out just before the next one starts, so two never overlap.
const FADE_OUT = 0.22;
const CAPS = CAPTIONS.map((c, i) => ({
  ...c,
  out: c.out ?? (i < CAPTIONS.length - 1 ? Math.max(c.at + 0.5, CAPTIONS[i + 1].at - FADE_OUT - 0.02) : c.at + 2),
}));

export const Captions3: React.FC<{ t: number }> = ({ t }) => (
  <>
    {CAPS.map((c) => {
      if (t < c.at - 0.1 || t > c.out + FADE_OUT + 0.05) return null;
      const v = Math.min(prog(t, c.at, c.at + 0.4), 1 - prog(t, c.out, c.out + FADE_OUT, easeInOut));
      const p = prog(t, c.at, c.at + 0.6, springy);
      return (
        <div
          key={c.at}
          style={{
            position: "absolute",
            left: 50,
            right: 50,
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
          <div style={{ ...headline, fontSize: c.price ? 118 : 92 }}>{c.l1}</div>
          {c.l2 ? <GradientLine id={`g3-${String(c.at).replace(".", "-")}`} text={c.l2} from={GOLD1} to={GOLD3} fontSize={c.price ? 70 : 92} /> : null}
        </div>
      );
    })}
  </>
);

/** Gold sparkle burst over the phone when Plus is introduced. */
export const PlusBurst: React.FC<{ t: number }> = ({ t }) => {
  if (t < T3.plusBurst - 0.05 || t > T3.plusBurst + 1.4) return null;
  const p = prog(t, T3.plusBurst, T3.plusBurst + 0.7, springy);
  const fade = 1 - prog(t, T3.plusBurst + 0.6, T3.plusBurst + 1.3);
  const ring = prog(t, T3.plusBurst, T3.plusBurst + 0.9, easeOut);
  const cx = 540;
  const cy = 1080;
  return (
    <div style={{ position: "absolute", inset: 0, pointerEvents: "none" }}>
      <div
        style={{
          position: "absolute",
          left: cx - 700 * ring,
          top: cy - 700 * ring,
          width: 1400 * ring,
          height: 1400 * ring,
          borderRadius: "50%",
          border: `6px solid ${GOLD2}`,
          boxShadow: `0 0 60px ${GOLD2}, inset 0 0 60px ${GOLD1}88`,
          opacity: (1 - ring) * 0.8,
        }}
      />
      {new Array(14).fill(0).map((_, i) => {
        const a = (i / 14) * Math.PI * 2 + random(`pb${i}`) * 0.4;
        const d = 260 + random(`pd${i}`) * 420;
        const s = 26 + random(`ps${i}`) * 40;
        return (
          <div
            key={i}
            style={{
              position: "absolute",
              left: cx + Math.cos(a) * d * p - s / 2,
              top: cy + Math.sin(a) * d * p - s / 2,
              opacity: fade * Math.min(1, p * 2),
              rotate: `${p * 90 * (i % 2 ? 1 : -1)}deg`,
              filter: `drop-shadow(0 0 12px ${GOLD2})`,
            }}
          >
            <SparkleGlyph size={s} color={i % 3 ? GOLD1 : GOLD2} />
          </div>
        );
      })}
    </div>
  );
};

/** Darkens the frame for the whispered "hide" line. */
export const WhisperVignette: React.FC<{ t: number }> = ({ t }) => {
  const k = prog(t, T3.hideSheetUp - 0.1, T3.hideSheetUp + 0.3) * (1 - prog(t, T3.hideDrawerDown, T3.backupPageIn + 0.3));
  if (k <= 0) return null;
  return <div style={{ position: "absolute", inset: 0, background: "radial-gradient(ellipse at 50% 58%, transparent 35%, rgba(0,0,0,0.75) 100%)", opacity: k, pointerEvents: "none" }} />;
};

/** End card: icon with a Plus badge, "Zenith Plus", "Unlock everything." */
export const Outro3: React.FC<{ t: number }> = ({ t }) => {
  if (t < T3.outro - 0.05) return null;
  const p = prog(t, T3.outro + 0.1, T3.outro + 0.95, springy);
  const word = prog(t, T3.plusWord - 0.05, T3.plusWord + 0.5);
  const badge = prog(t, T3.plusWord + 0.25, T3.plusWord + 0.7, springy);
  const tag = prog(t, T3.unlockLine - 0.05, T3.unlockLine + 0.45);
  const small = prog(t, T3.unlockLine + 1.2, T3.unlockLine + 1.8);
  const fadeOut = prog(t, T3.end - 0.5, T3.end - 0.03, easeIn);
  const float = Math.sin((t - T3.outro) * 1.4) * 10;
  // flanking the "Unlock everything." line
  const locks = [
    { x: 100, y: 1148 },
    { x: 980, y: 1148 },
    { x: 540, y: 1345 },
  ];
  return (
    <div style={{ position: "absolute", inset: 0, opacity: 1 - fadeOut }}>
      <div style={{ position: "absolute", left: 40, top: 200, width: 1000, height: 1000, borderRadius: "50%", background: `radial-gradient(circle, ${GOLD2}44, ${GOLD3}1c 40%, transparent 70%)`, opacity: p }} />
      <div style={{ position: "absolute", inset: 0, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", paddingBottom: 120 }}>
        <div style={{ position: "relative", scale: interpolate(p, [0, 1], [0.3, 1]), opacity: Math.min(1, p * 1.5), filter: `blur(${(1 - Math.min(p, 1)) * 20}px)`, translate: `0px ${float}px` }}>
          <GlassIcon size={360} shineAt={T3.outro + 0.35} t={t} />
          <div
            style={{
              position: "absolute",
              right: -26,
              top: -26,
              width: 110,
              height: 110,
              borderRadius: 34,
              background: `linear-gradient(135deg, ${GOLD1}, ${GOLD3})`,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              boxShadow: `0 0 50px ${GOLD2}aa`,
              scale: badge,
              rotate: `${(1 - Math.min(badge, 1)) * -40}deg`,
            }}
          >
            <SparkleGlyph size={64} />
          </div>
        </div>
        <div style={{ ...headline, fontSize: 140, marginTop: 50, opacity: word, letterSpacing: interpolate(word, [0, 1], [24, -4]), filter: `blur(${(1 - word) * 12}px)` }}>
          Zenith <span style={{ color: GOLD2 }}>Plus</span>
        </div>
        <div style={{ width: "100%", marginTop: 8, opacity: tag, translate: `0px ${(1 - tag) * 30}px` }}>
          <GradientLine id="g3-unlock" text="Unlock everything." from={GOLD1} to={GOLD3} fontSize={84} />
        </div>
        <div style={{ fontFamily: "Inter", fontSize: 44, fontWeight: 500, color: "rgba(255,255,255,0.75)", marginTop: 30, opacity: small }}>Try it free for 7 days</div>
      </div>
      {/* locks pop open into sparkles on "Unlock everything" */}
      {locks.map((l, i) => {
        const at = T3.unlockLine + 0.1 + i * 0.15;
        const inP = prog(t, at - 0.35, at, springy);
        const pop = prog(t, at, at + 0.35, easeOut);
        return (
          <div key={i} style={{ position: "absolute", left: l.x - 40, top: l.y - 40 }}>
            <div style={{ opacity: Math.min(1, inP) * (1 - pop), scale: Math.min(1.2, inP) * (1 + pop * 0.6), filter: `drop-shadow(0 0 14px ${GOLD2})` }}>
              <LockGlyph size={80} color={GOLD1} />
            </div>
            {new Array(6).fill(0).map((_, j) => {
              const a = (j / 6) * Math.PI * 2 + i;
              const d = pop * 90;
              return (
                <div
                  key={j}
                  style={{ position: "absolute", left: 40 + Math.cos(a) * d - 12, top: 40 + Math.sin(a) * d - 12, opacity: pop > 0 ? 1 - prog(t, at + 0.25, at + 0.7) : 0, rotate: `${pop * 120}deg` }}
                >
                  <SparkleGlyph size={24} color={GOLD1} />
                </div>
              );
            })}
          </div>
        );
      })}
    </div>
  );
};

