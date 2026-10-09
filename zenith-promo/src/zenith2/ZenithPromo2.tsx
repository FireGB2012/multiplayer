import React from "react";
import { AbsoluteFill } from "remotion";
import { easeInOut, prog, useTime } from "../zenith/anim";
import { Backdrop } from "../zenith/Scenes";
import { Phone2 } from "./Phone2";
import { Captions2, Outro2 } from "./Scenes2";
import { Sound2 } from "./Sound2";
import { T2, paletteAt2 } from "./theme2";

// Ad #2: "Zenith moves" — app opening, drawer search, free placement, hidden apps, theme maker.
// Inter is loaded once by ../zenith/ZenithPromo (imported by the root).
export const ZenithPromo2: React.FC = () => {
  const t = useTime();
  const pal = paletteAt2(t);
  return (
    <AbsoluteFill style={{ background: "#07070d", fontFamily: "Inter" }}>
      <Backdrop t={t} pal={{ accent: pal.accent, a: pal.n1, b: pal.n2, base: pal.base }} />
      <Phone2 t={t} pal={pal} />
      <Captions2 t={t} pal={pal} />
      <Outro2 t={t} pal={pal} />
      <AbsoluteFill style={{ background: "#000", opacity: prog(t, T2.end - 0.45, T2.end - 0.03, easeInOut) }} />
      <Sound2 />
    </AbsoluteFill>
  );
};
