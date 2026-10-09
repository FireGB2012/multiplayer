import React from "react";
import { interpolate } from "remotion";
import { StatusBar } from "../zenith/HomeScreen";
import { easeInOut, easeOut, prog } from "../zenith/anim";
import { HomeScreen2, Nebula, SH, SW, StockScreen } from "./Home2";
import { BackupToast, ColourOpen, Drawer, Finger, FreePlacement, GlassOpen, HiddenAppsSheet, ThemeMakerSheet } from "./Layers";
import { T2, type Pal2 } from "./theme2";

const PHONE_W = 696;
const PHONE_H = 1456;
const BEZEL = 12;
const REVEAL_X = 168;
const REVEAL_Y = 380;

export const Phone2: React.FC<{ t: number; pal: Pal2 }> = ({ t, pal }) => {
  const enter = prog(t, 0, 0.6, easeOut);
  const exit = prog(t, T2.outro - 0.4, T2.outro + 0.1, easeInOut);
  if (exit >= 1) return null;
  const bounce = Math.sin(Math.PI * prog(t, T2.zenithMoves, T2.zenithMoves + 0.55, easeInOut)) * 0.035;
  const revealR = prog(t, T2.zenithMoves, T2.zenithMoves + 0.7, easeInOut) * 820;
  const revealing = t < T2.zenithMoves + 0.75;
  const sheetDim =
    prog(t, T2.hideSheetUp, T2.hideSheetUp + 0.3) * (1 - prog(t, T2.hideDrawer, T2.hideDrawer + 0.3)) +
    prog(t, T2.themeIn, T2.themeIn + 0.3) * (1 - prog(t, T2.themeDown, T2.themeDown + 0.3));

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
          boxShadow: `0 60px 120px rgba(0,0,0,0.55), 0 0 140px ${pal.accent}30`,
          translate: `0px ${interpolate(enter, [0, 1], [260, 0]) + interpolate(exit, [0, 1], [0, 160])}px`,
          scale: (1 + bounce) * interpolate(exit, [0, 1], [1, 0.45]),
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
            {revealing ? <StockScreen /> : null}
            <div
              style={{
                position: "absolute",
                inset: 0,
                clipPath: revealing ? `circle(${revealR}px at ${REVEAL_X}px ${REVEAL_Y}px)` : undefined,
              }}
            >
              <Nebula pal={pal} t={t} />
              <HomeScreen2 t={t} pal={pal} appear={T2.zenithMoves + 0.12} />
              <FreePlacement t={t} pal={pal} />
              <ColourOpen t={t} />
              <GlassOpen t={t} />
              <Drawer t={t} pal={pal} />
              <div style={{ position: "absolute", inset: 0, background: "rgba(0,0,0,0.45)", opacity: sheetDim }} />
              <HiddenAppsSheet t={t} pal={pal} />
              <ThemeMakerSheet t={t} pal={pal} />
            </div>
            {revealing && revealR > 0 ? (
              <div
                style={{
                  position: "absolute",
                  left: REVEAL_X - revealR,
                  top: REVEAL_Y - revealR,
                  width: revealR * 2,
                  height: revealR * 2,
                  borderRadius: "50%",
                  border: "3px solid rgba(255,255,255,0.85)",
                  boxShadow: `0 0 30px ${pal.accent}, inset 0 0 30px ${pal.accent2}88`,
                  opacity: 1 - prog(t, T2.zenithMoves + 0.35, T2.zenithMoves + 0.7),
                }}
              />
            ) : null}
            <StatusBar time="8:09" />
            <BackupToast t={t} pal={pal} />
            <Finger t={t} />
            <div style={{ position: "absolute", bottom: 6, left: SW / 2 - 40, width: 80, height: 3, borderRadius: 2, background: "rgba(255,255,255,0.85)" }} />
          </div>
        </div>
      </div>
    </div>
  );
};

