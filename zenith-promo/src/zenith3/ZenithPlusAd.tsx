import React from "react";
import { AbsoluteFill } from "remotion";
import { easeInOut, prog, useTime } from "../zenith/anim";
import { Backdrop } from "../zenith/Scenes";
import { Phone3 } from "./Phone3";
import { Captions3, Outro3, PlusBurst, WhisperVignette } from "./Scenes3";
import { Sound3 } from "./Sound3";
import { T3, paletteAt3 } from "./t3";

// Ad #3: Zenith Plus — theme maker, free placement, hidden apps, account backup, pricing.
// Inter is loaded once by ../zenith/ZenithPromo (imported by the root).
export const ZenithPlusAd: React.FC = () => {
  const t = useTime();
  const pal = paletteAt3(t);
  return (
    <AbsoluteFill style={{ background: "#08050a", fontFamily: "Inter" }}>
      <Backdrop t={t} pal={{ accent: pal.accent, a: pal.glow, b: pal.s1, base: pal.base }} />
      <WhisperVignette t={t} />
      <Phone3 t={t} pal={pal} />
      <PlusBurst t={t} />
      <Captions3 t={t} />
      <Outro3 t={t} />
      <AbsoluteFill style={{ background: "#000", opacity: prog(t, T3.end - 0.45, T3.end - 0.03, easeInOut) }} />
      <Sound3 />
    </AbsoluteFill>
  );
};
