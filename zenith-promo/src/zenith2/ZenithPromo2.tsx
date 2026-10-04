import React from "react";
import { AbsoluteFill } from "remotion";
import { useTime } from "../zenith/anim";
import { Backdrop } from "../zenith/Scenes";
import { Phone2 } from "./Phone2";
import { Captions2, Outro2 } from "./Scenes2";
import { Sound2 } from "./Sound2";
import { paletteAt2 } from "./theme2";

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
      <Sound2 />
    </AbsoluteFill>
  );
};
