import React from "react";
import { interpolate } from "remotion";
import { HomeScreen, SCREEN_H, SCREEN_W, StatusBar, Wallpaper, type Palette } from "./HomeScreen";
import { AuraPage, HomePage, LookPage, PlusPage, glassAmountAt, gridScaleAt, shapeRadiusAt } from "./SheetPages";
import { easeIn, easeInOut, prog, softSpring, springy, tw } from "./anim";
import { T } from "./timeline";

export const PHONE_W = 696;
export const PHONE_H = 1456;
const BEZEL = 12;

/** Where the bottom sheet's top edge sits (phone px). SCREEN_H = hidden. */
const sheetTopAt = (t: number) => {
  let top = SCREEN_H;
  if (t >= T.lookIn) top = tw(t, T.lookIn, T.lookIn + 0.7, SCREEN_H, 168, springy);
  if (t >= T.homeSheetIn) top = tw(t, T.homeSheetIn, T.homeSheetIn + 0.55, 168, 326, softSpring);
  if (t >= T.sheetDown) top = tw(t, T.sheetDown, T.sheetDown + 0.4, 326, SCREEN_H + 20, easeIn);
  if (t >= T.auraIn) top = tw(t, T.auraIn, T.auraIn + 0.7, SCREEN_H + 20, 196, springy);
  if (t >= T.plusIn) top = tw(t, T.plusIn, T.plusIn + 0.5, 196, 210, softSpring);
  return top;
};

/** Page push inside the sheet: -1 = gone left, 0 = showing, 1 = waiting on the right. */
const pagePos = (t: number, inAt: number | null, outAt: number | null) => {
  let x = inAt === null ? 0 : tw(t, inAt, inAt + 0.45, 1, 0, easeInOut);
  if (outAt !== null && t >= outAt) x = tw(t, outAt, outAt + 0.45, 0, -1, easeInOut);
  return x;
};

const rotYAt = (t: number) => {
  if (t < T.tilt + 0.3) return 0;
  if (t < T.lightFollows - 0.1) return tw(t, T.tilt + 0.3, T.lightFollows - 0.1, 0, -15, easeInOut);
  if (t < T.lightFollows + 0.7) return tw(t, T.lightFollows - 0.1, T.lightFollows + 0.7, -15, 15, easeInOut);
  return tw(t, T.lightFollows + 0.7, T.lightFollows + 1.25, 15, 0, easeInOut);
};

/** Horizontal position (%) of the light streak sweeping over the glass. */
const shineAt = (t: number, rotY: number) => {
  if (t > T.tilt + 0.2 && t < T.homeSheetIn) return interpolate(rotY, [-15, 15], [-20, 120]);
  if (t > 6.9 && t < 8) return tw(t, 6.9, 7.8, -40, 140, easeInOut);
  if (t > T.everySize + 0.3 && t < T.auraIn) return tw(t, T.everySize + 0.3, T.everySize + 1.2, -40, 140, easeInOut);
  return -100;
};

export const Phone: React.FC<{ t: number; pal: Palette }> = ({ t, pal }) => {
  const enter = prog(t, T.phoneIn, T.phoneIn + 1.0, springy);
  const exit = prog(t, T.outro - 0.1, T.outro + 0.45, easeIn);
  const rotY = rotYAt(t);
  const top = sheetTopAt(t);
  const sheetUp = interpolate(top, [300, SCREEN_H], [1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const amount = glassAmountAt(t);
  const shine = shineAt(t, rotY);
  const big = prog(t, T.everySize + 0.1, T.everySize + 0.9, springy);
  const homeAppear = T.phoneIn + 0.45;

  const pages = [
    { key: "look", x: pagePos(t, null, T.homeSheetIn), show: t < T.homeSheetIn + 0.5, el: <LookPage t={t} pal={pal} /> },
    { key: "home", x: pagePos(t, T.homeSheetIn, null), show: t >= T.homeSheetIn && t < T.sheetDown + 0.5, el: <HomePage t={t} pal={pal} /> },
    { key: "aura", x: pagePos(t, null, T.plusIn), show: t >= T.sheetDown + 0.5 && t < T.plusIn + 0.5, el: <AuraPage t={t} pal={pal} /> },
    { key: "plus", x: pagePos(t, T.plusIn, null), show: t >= T.plusIn, el: <PlusPage t={t} pal={pal} /> },
  ];

  if (t < T.phoneIn || exit >= 1) return null;

  return (
    <div style={{ position: "absolute", inset: 0, perspective: 2600 }}>
      <div
        style={{
          position: "absolute",
          left: (1080 - PHONE_W) / 2,
          top: 400,
          width: PHONE_W,
          height: PHONE_H,
          borderRadius: 96,
          padding: BEZEL,
          background: `linear-gradient(${150 + rotY * 3}deg, #ff8a86 0%, #c22f45 28%, #6e1022 52%, #e0505f 78%, #ff9c8e 100%)`,
          boxShadow: `0 60px 120px rgba(0,0,0,0.55), 0 0 140px ${pal.accent}30`,
          translate: `0px ${interpolate(enter, [0, 1], [1500, 0]) + interpolate(exit, [0, 1], [0, 160])}px`,
          transform: `rotateX(${interpolate(enter, [0, 1], [22, 0])}deg) rotateY(${rotY}deg)`,
          scale: interpolate(exit, [0, 1], [1, 0.45]),
          opacity: 1 - exit,
          filter: exit > 0 ? `blur(${exit * 14}px)` : undefined,
          transformStyle: "preserve-3d",
        }}
      >
        <div
          style={{
            width: "100%",
            height: "100%",
            borderRadius: 84,
            background: "#000",
            padding: 6,
          }}
        >
          <div
            style={{
              position: "relative",
              width: SCREEN_W,
              height: SCREEN_H,
              zoom: (PHONE_W - BEZEL * 2 - 12) / SCREEN_W,
              borderRadius: 39,
              overflow: "hidden",
              fontFamily: "Inter",
            }}
          >
            <Wallpaper pal={pal} t={t} />
            <StatusBar time={t < 15 ? "8:42" : "8:44"} />
            <HomeScreen t={t} pal={pal} appear={homeAppear} big={big} shapeRadius={shapeRadiusAt(t)} gridScale={gridScaleAt(t)} />

            {/* dim behind the sheet */}
            <div style={{ position: "absolute", inset: 0, background: "rgba(0,0,0,0.3)", opacity: sheetUp }} />

            {/* bottom sheet */}
            <div
              style={{
                position: "absolute",
                left: 6,
                right: 6,
                top,
                height: SCREEN_H + 120,
                borderRadius: 24,
                background: `linear-gradient(180deg, rgba(70,40,52,${0.86 - amount * 0.7}), rgba(30,18,26,${0.92 - amount * 0.6}))`,
                backdropFilter: `blur(${10 + amount * 26}px) saturate(160%)`,
                border: "0.7px solid rgba(255,255,255,0.16)",
                boxShadow: "inset 0 1px 0 rgba(255,255,255,0.22), 0 -10px 40px rgba(0,0,0,0.35)",
                overflow: "hidden",
                display: top >= SCREEN_H ? "none" : "block",
              }}
            >
              <div style={{ width: 30, height: 3, borderRadius: 2, background: "rgba(255,255,255,0.35)", margin: "7px auto 6px" }} />
              <div style={{ position: "relative" }}>
                {pages.map((p) =>
                  p.show ? (
                    <div
                      key={p.key}
                      style={{
                        position: "absolute",
                        left: 10,
                        right: 10,
                        top: 0,
                        translate: `${p.x * 110}% 0px`,
                        opacity: 1 - Math.abs(p.x) * 0.8,
                      }}
                    >
                      {p.el}
                    </div>
                  ) : null,
                )}
              </div>
            </div>

            {/* tilt shine: a soft light streak over all the glass */}
            <div
              style={{
                position: "absolute",
                inset: 0,
                mixBlendMode: "screen",
                background: `linear-gradient(105deg, transparent ${shine - 16}%, rgba(255,255,255,0.05) ${shine - 8}%, rgba(255,255,255,0.28) ${shine}%, rgba(255,255,255,0.05) ${shine + 8}%, transparent ${shine + 16}%)`,
              }}
            />
            {/* home indicator */}
            <div style={{ position: "absolute", bottom: 6, left: SCREEN_W / 2 - 40, width: 80, height: 3, borderRadius: 2, background: "rgba(255,255,255,0.85)" }} />
          </div>
        </div>
      </div>
    </div>
  );
};

