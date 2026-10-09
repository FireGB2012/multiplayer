import React from "react";
import { interpolate } from "remotion";
import { GmailM, GoogleG } from "../zenith/AppIcons";
import { easeInOut, prog } from "../zenith/anim";
import { Pill, fingerAt } from "./kit";
import { GMAIL_DRAG, GOOGLE_DRAG } from "./gestures3";
import { GMAIL_A, GOOGLE_A, ICON_RADIUS, gmailRectAt, qsRectAt } from "./Home3";
import { T3, type Pal3 } from "./t3";

const Trash: React.FC = () => (
  <svg width={10} height={10} viewBox="0 0 24 24">
    <path fill="#fff" d="M9 3h6l1 2h4v2H4V5h4zM6 8h12l-1 13H7z" />
  </svg>
);

/** Grey translucent copy of a tile that follows the finger while dragging. */
const Ghost: React.FC<{ x: number; y: number; size: number; opacity: number; tilt: number; children: React.ReactNode }> = ({ x, y, size, opacity, tilt, children }) => (
  <div
    style={{
      position: "absolute",
      left: x - size / 2,
      top: y - size / 2,
      width: size,
      height: size,
      borderRadius: size * ICON_RADIUS,
      background: "rgba(215,205,225,0.62)",
      backdropFilter: "blur(6px)",
      border: "1px solid rgba(255,255,255,0.55)",
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      opacity,
      scale: 1.08,
      rotate: `${tilt}deg`,
      boxShadow: "0 18px 36px rgba(0,0,0,0.4)",
    }}
  >
    <div style={{ filter: "grayscale(1)", opacity: 0.65 }}>{children}</div>
  </div>
);

export const Placement3: React.FC<{ t: number; pal: Pal3 }> = ({ t, pal }) => {
  if (t < T3.pressGmail - 0.1 || t > T3.resizeDone + 0.5) return null;
  // ghosts follow their own drag gesture, never the next touch
  const fg = fingerAt([GMAIL_DRAG], t);
  const fo = fingerAt([GOOGLE_DRAG], t);
  const gm = gmailRectAt(t);
  const press = prog(t, T3.pressGmail, T3.dragGmail, easeInOut) * (1 - prog(t, T3.dragGmail, T3.dragGmail + 0.12));
  const tilt = (a: number, b: number) =>
    interpolate(t, [a, a + 0.2, b - 0.12, b], [0, 5, -2, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const ghostGmail = t >= T3.dragGmail && t < T3.dropGmail + 0.2;
  const ghostGoogle = t >= T3.dragGoogle && t < T3.dropGoogle + 0.2;
  const qs = qsRectAt(t);
  const resizing = prog(t, T3.resizeStart - 0.1, T3.resizeStart + 0.1) * (1 - prog(t, T3.resizeDone, T3.resizeDone + 0.2));
  return (
    <>
      {press > 0 ? (
        <div
          style={{
            position: "absolute",
            left: gm.x - 5,
            top: gm.y - 5,
            width: gm.w + 10,
            height: gm.h + 10,
            borderRadius: gm.w * ICON_RADIUS + 5,
            border: `2px solid ${pal.accent}`,
            boxShadow: `0 0 ${16 * press}px ${pal.accent}`,
            opacity: press,
          }}
        />
      ) : null}
      <Pill t={t} inAt={T3.dragGmail - 0.1} outAt={T3.dropGoogle + 0.1}>
        <Trash /> Hold to remove
      </Pill>
      {ghostGmail && fg ? (
        <Ghost x={fg.x} y={fg.y} size={GMAIL_A.w} opacity={(1 - prog(t, T3.dropGmail, T3.dropGmail + 0.2)) * prog(t, T3.dragGmail, T3.dragGmail + 0.1)} tilt={tilt(T3.dragGmail, T3.dropGmail)}>
          <GmailM size={30} />
        </Ghost>
      ) : null}
      {ghostGoogle && fo ? (
        <Ghost x={fo.x} y={fo.y} size={GOOGLE_A.w} opacity={(1 - prog(t, T3.dropGoogle, T3.dropGoogle + 0.2)) * prog(t, T3.dragGoogle, T3.dragGoogle + 0.1)} tilt={tilt(T3.dragGoogle, T3.dropGoogle)}>
          <GoogleG size={26} />
        </Ghost>
      ) : null}
      <Pill t={t} inAt={T3.resizeStart - 0.12} outAt={T3.resizeDone}>
        Pull the pointer to resize · tap to finish
      </Pill>
      {resizing > 0 ? (
        <>
          <div
            style={{
              position: "absolute",
              left: qs.x - 4,
              top: qs.y - 4,
              width: qs.w + 8,
              height: qs.h + 8,
              borderRadius: 22,
              border: `2px solid ${pal.accent}`,
              boxShadow: `0 0 18px ${pal.accent}aa, inset 0 0 0 1px rgba(255,255,255,0.4)`,
              opacity: resizing,
            }}
          />
          <svg
            width={16}
            height={16}
            viewBox="0 0 24 24"
            style={{ position: "absolute", left: qs.x + qs.w - 10, top: qs.y + qs.h - 10, opacity: resizing, filter: "drop-shadow(0 2px 3px rgba(0,0,0,0.5))" }}
          >
            <path d="M3 3l18 7-8 3-3 8z" fill="#f6f0ff" stroke="#7a5a9a" strokeWidth={1.2} strokeLinejoin="round" transform="rotate(180 12 12)" />
          </svg>
        </>
      ) : null}
    </>
  );
};
