import React from "react";
import { Audio } from "@remotion/media";
import { interpolate, staticFile, useVideoConfig } from "remotion";
import { VOICE_START } from "../zenith/anim";
import { T2 } from "./theme2";

const Sfx: React.FC<{ at: number; src: string; volume?: number; trim?: number }> = ({ at, src, volume = 0.6, trim = 0 }) => {
  const { fps } = useVideoConfig();
  return (
    <Audio
      name={src}
      src={staticFile(`sfx/${src}.wav`)}
      from={Math.max(0, Math.round(at * fps))}
      trimBefore={Math.round(trim * fps)}
      premountFor={fps}
      volume={volume}
    />
  );
};

export const Sound2: React.FC = () => {
  const { fps } = useVideoConfig();
  return (
    <>
      <Audio name="Voiceover" src={staticFile("voiceover2_loud.wav")} from={Math.round(VOICE_START * fps)} premountFor={fps} volume={1} />
      <Audio
        name="Music"
        src={staticFile("music2.wav")}
        volume={(f) =>
          interpolate(f / fps, [0, 0.4, 21.4, 21.55, 22.3, 22.5, 24.7, 25.2], [0.2, 0.11, 0.11, 0.06, 0.06, 0.11, 0.11, 0.5], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          })
        }
      />

      {/* "Most launchers just sit there" → "Zenith moves" */}
      <Sfx at={0} src="riser" trim={0.84} volume={0.28} />
      <Sfx at={T2.zenithMoves - 0.15} src="impact" volume={0.4} />
      <Sfx at={T2.zenithMoves} src="whoosh" volume={0.3} />
      <Sfx at={T2.zenithMoves + 0.3} src="shimmer" volume={0.18} />

      {/* App opening */}
      <Sfx at={T2.gmailTap} src="click_002" volume={0.5} />
      <Sfx at={T2.gmailOpen} src="whoosh-short" volume={0.55} />
      <Sfx at={T2.gmailClose} src="whoosh-down" volume={0.25} />
      <Sfx at={T2.chromeTap} src="click_002" volume={0.5} />
      <Sfx at={T2.chromeOpen} src="glass_004" volume={0.35} />
      <Sfx at={T2.chromeOpen + 0.1} src="shimmer" volume={0.15} />
      <Sfx at={T2.chromeClose} src="whoosh-down" volume={0.25} />

      {/* Drawer + search */}
      <Sfx at={T2.swipeUp} src="whoosh" volume={0.3} />
      <Sfx at={T2.searchAway - 0.12} src="click_002" volume={0.5} />
      {T2.typeAt.map((a) => (
        <Sfx key={a} at={a} src="select_001" volume={0.55} />
      ))}
      <Sfx at={T2.drawerDown} src="whoosh-down" volume={0.4} />

      {/* Free placement */}
      <Sfx at={T2.dragStart - 0.02} src="toggle_001" volume={0.3} />
      <Sfx at={T2.dragStart + 0.05} src="whoosh-short" volume={0.25} />
      <Sfx at={T2.drop} src="glass_003" volume={0.4} />
      <Sfx at={T2.resizeStart} src="glass_005" volume={0.55} />
      <Sfx at={T2.resizeStart + 0.15} src="whoosh-short" volume={0.3} />
      <Sfx at={T2.resizeDone} src="click_002" volume={0.5} />

      {/* Hidden apps */}
      <Sfx at={T2.hideSheetUp - 0.05} src="whoosh-short" volume={0.45} />
      <Sfx at={T2.hideToggle} src="toggle_002" volume={0.35} />
      <Sfx at={T2.hideDrawer + 0.15} src="whoosh-short" volume={0.2} />
      <Sfx at={T2.poof} src="glass_001" volume={0.3} />
      <Sfx at={T2.poof + 0.05} src="shimmer" volume={0.15} />

      {/* Theme maker */}
      <Sfx at={T2.themeIn - 0.05} src="whoosh-short" volume={0.3} />
      {[0, 0.1, 0.2, 0.3, 0.4].map((d) => (
        <Sfx key={`a${d}`} at={T2.slideAccent + d} src="select_001" volume={0.3} />
      ))}
      {[0, 0.1, 0.2, 0.3].map((d) => (
        <Sfx key={`b${d}`} at={T2.slideAccent2 + d} src="select_001" volume={0.3} />
      ))}
      {[0, 0.1, 0.2, 0.3].map((d) => (
        <Sfx key={`c${d}`} at={T2.slideBg + d} src="select_001" volume={0.3} />
      ))}
      <Sfx at={T2.themeDown} src="whoosh-down" volume={0.45} />

      {/* Backup */}
      <Sfx at={T2.backup} src="open_001" volume={0.35} />
      <Sfx at={T2.backupDone} src="confirmation_002" volume={0.3} />

      {/* Outro */}
      <Sfx at={T2.outro - 0.45} src="whoosh" volume={0.2} />
      <Sfx at={T2.outro + 0.05} src="impact" volume={0.4} />
      <Sfx at={T2.outro + 0.4} src="shimmer" volume={0.15} />
      <Sfx at={T2.finallyYours + 0.75} src="glass_004" volume={0.35} />
    </>
  );
};
