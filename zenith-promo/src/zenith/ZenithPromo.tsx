import React from "react";
import { AbsoluteFill, staticFile } from "remotion";
import { loadFont } from "@remotion/fonts";
import { colorAt, useTime } from "./anim";
import { THEMES, THEME_KEYS } from "./timeline";
import type { Palette } from "./HomeScreen";
import { Phone } from "./Phone";
import { Backdrop, Captions, Intro, LogoReveal, Outro } from "./Scenes";
import { SoundTrack } from "./SoundTrack";
import { T } from "./timeline";

// Inter (variable, 400–800), bundled locally so rendering works offline.
loadFont({ family: "Inter", url: staticFile("Inter.woff2"), weight: "400 800" });

// The "Pick your colour" swatches, then the Ember theme once it's unlocked.
const PALETTE_KEYS: [number, keyof typeof THEMES][] = [...THEME_KEYS, [T.unlockThemes + 0.15, "ember"], [T.outro, "sunset"]];

const paletteAt = (t: number): Palette => {
  const pick = (k: keyof Palette) => colorAt(t, PALETTE_KEYS.map(([at, name]) => [at, THEMES[name][k]] as [number, string]));
  return { accent: pick("accent"), a: pick("a"), b: pick("b"), base: pick("base") };
};

export const ZenithPromo: React.FC = () => {
  const t = useTime();
  const pal = paletteAt(t);
  return (
    <AbsoluteFill style={{ background: "#09060a", fontFamily: "Inter" }}>
      <Backdrop t={t} pal={pal} />
      <Intro t={t} pal={pal} />
      <LogoReveal t={t} pal={pal} />
      <Phone t={t} pal={pal} />
      <Captions t={t} pal={pal} />
      <Outro t={t} pal={pal} />
      <SoundTrack />
    </AbsoluteFill>
  );
};
