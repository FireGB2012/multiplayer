import React from "react";
import { interpolate } from "remotion";
import { StatusBar } from "../zenith/HomeScreen";
import { easeInOut, easeOut, prog } from "../zenith/anim";
import { Pill, SH, SW, Finger } from "./kit";
import { HomeScreen3, Silk } from "./Home3";
import { Placement3 } from "./Placement3";
import { BackupPage3, CloudBadge, Drawer3, HiddenSheet3, Paywall3, PlusThemeSheet } from "./Sheets3";
import { GESTURES3 } from "./gestures3";
import { GOLD1, T3, type Pal3 } from "./t3";
import { CheckGlyph } from "../zenith/AppIcons";

const PHONE_W = 696;
const PHONE_H = 1456;
const BEZEL = 12;

export const Phone3: React.FC<{ t: number; pal: Pal3 }> = ({ t, pal }) => {
  const enter = prog(t, 0, 0.6, easeOut);
  const exit = prog(t, T3.outro - 0.4, T3.outro + 0.1, easeInOut);
  if (exit >= 1) return null;
  const plus = prog(t, T3.plusBurst, T3.plusBurst + 0.6);
  const sheetDim =
    prog(t, T3.plusSheetUp, T3.plusSheetUp + 0.3) * (1 - prog(t, T3.themeDown, T3.themeDown + 0.3)) +
    prog(t, T3.hideSheetUp, T3.hideSheetUp + 0.3) * (1 - prog(t, T3.hideDrawer, T3.hideDrawer + 0.3));

  return (
    <div style={{ position: "absolute", inset: 0 }}>
      <div
        style={{
          position: "absolute",
          left: (1080 - PHONE_W) / 2,
          top: 400,
          width: PHONE_W,
          height: PHONE_H,
          borderRadius: 96,
          padding: BEZEL,
          background: "linear-gradient(150deg, #ff8a86 0%, #c22f45 28%, #6e1022 52%, #e0505f 78%, #ff9c8e 100%)",
          boxShadow: `0 60px 120px rgba(0,0,0,0.55), 0 0 160px rgba(255,181,71,${0.35 * plus})`,
          translate: `0px ${interpolate(enter, [0, 1], [260, 0]) + interpolate(exit, [0, 1], [0, 160])}px`,
          scale: interpolate(exit, [0, 1], [1, 0.45]),
          opacity: enter * (1 - exit),
          filter: exit > 0 ? `blur(${exit * 14}px)` : undefined,
        }}
      >
        <div style={{ width: "100%", height: "100%", borderRadius: 84, background: "#000", padding: 6 }}>
          <div
            style={{
              position: "relative",
              width: SW,
              height: SH,
              zoom: (PHONE_W - BEZEL * 2 - 12) / SW,
              borderRadius: 39,
              overflow: "hidden",
              fontFamily: "Inter",
            }}
          >
            <Silk pal={pal} t={t} />
            <HomeScreen3 t={t} pal={pal} />
            <Placement3 t={t} pal={pal} />
            <div style={{ position: "absolute", inset: 0, background: "rgba(0,0,0,0.45)", opacity: sheetDim }} />
            <PlusThemeSheet t={t} pal={pal} />
            <HiddenSheet3 t={t} pal={pal} />
            <Drawer3 t={t} pal={pal} />
            <BackupPage3 t={t} pal={pal} />
            <Paywall3 t={t} />
            <StatusBar time="6:53" />
            <CloudBadge t={t} />
            <Pill t={t} inAt={T3.restoredToast} outAt={T3.paywallUp + 0.05}>
              <CheckGlyph size={10} color={GOLD1} /> Restored from your account
            </Pill>
            <Finger t={t} gestures={GESTURES3} />
            <div style={{ position: "absolute", bottom: 6, left: SW / 2 - 40, width: 80, height: 3, borderRadius: 2, background: "rgba(255,255,255,0.85)" }} />
          </div>
        </div>
      </div>
    </div>
  );
};
