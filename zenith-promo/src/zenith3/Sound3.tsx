import React from "react";
import { Audio } from "@remotion/media";
import { interpolate, staticFile, useVideoConfig } from "remotion";
import { VOICE_START } from "../zenith/anim";
import { T3 } from "./t3";

const Sfx: React.FC<{ at: number; src: string; volume?: number }> = ({ at, src, volume = 0.3 }) => {
  const { fps } = useVideoConfig();
  return <Audio name={src} src={staticFile(`sfx/${src}.wav`)} from={Math.max(0, Math.round(at * fps))} premountFor={fps} volume={volume} />;
};

// Voice-first mix: the voice is pre-boosted (voiceover3_loud.wav), the music sits low
// under it and the SFX are kept out of the way of the stressed words.
export const Sound3: React.FC = () => {
  const { fps } = useVideoConfig();
  return (
    <>
      <Audio name="Voiceover" src={staticFile("voiceover3_loud.wav")} from={Math.round(VOICE_START * fps)} premountFor={fps} volume={1} />
      <Audio
        name="Music"
        src={staticFile("music3.wav")}
        volume={(f) =>
          interpolate(f / fps, [0, 0.45, 25.3, 25.45, 26.4, 26.6, 28.3, 28.8], [0.2, 0.11, 0.11, 0.06, 0.06, 0.11, 0.11, 0.45], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          })
        }
      />

      {/* Out of the box */}
      <Sfx at={0.02} src="whoosh" volume={0.22} />
      <Sfx at={0.45} src="glass_003" volume={0.22} />

      {/* Plus reveal (the hit lands in the gap before "Zenith Plus") */}
      <Sfx at={T3.plusBurst - 0.03} src="impact" volume={0.35} />
      <Sfx at={T3.plusBurst + 0.05} src="shimmer" volume={0.12} />
      <Sfx at={T3.plusSheetUp} src="whoosh-short" volume={0.22} />
      {T3.unlockAt.map((a) => (
        <Sfx key={a} at={a} src="glass_002" volume={0.26} />
      ))}

      {/* Theme maker */}
      <Sfx at={T3.themeTap} src="click_002" volume={0.4} />
      <Sfx at={T3.themePush} src="whoosh-short" volume={0.18} />
      {[0, 0.1, 0.2, 0.3, 0.4].map((d) => (
        <Sfx key={`a${d}`} at={T3.slideAccent + d} src="select_001" volume={0.22} />
      ))}
      {[0, 0.11, 0.22, 0.33].map((d) => (
        <Sfx key={`b${d}`} at={T3.slideAccent2 + d} src="select_001" volume={0.22} />
      ))}
      {new Array(11).fill(0).map((_, i) => (
        <Sfx key={`c${i}`} at={T3.slideBg + i * 0.125} src="select_001" volume={0.2} />
      ))}
      <Sfx at={T3.themeDown} src="whoosh-down" volume={0.22} />

      {/* Free placement */}
      <Sfx at={T3.dragGmail - 0.02} src="toggle_001" volume={0.28} />
      <Sfx at={T3.dropGmail} src="glass_003" volume={0.32} />
      <Sfx at={T3.dropGoogle} src="glass_003" volume={0.28} />
      <Sfx at={T3.resizeStart} src="glass_005" volume={0.3} />
      {[10.6, 10.8, 11.15, 11.35, 11.5].map((a) => (
        <Sfx key={a} at={a} src="select_001" volume={0.18} />
      ))}
      <Sfx at={T3.resizeDone} src="click_002" volume={0.4} />

      {/* Hidden apps (whispered, keep it quiet) */}
      <Sfx at={T3.hideSheetUp - 0.03} src="whoosh-short" volume={0.15} />
      <Sfx at={T3.hideToggle} src="toggle_002" volume={0.26} />
      <Sfx at={T3.hideDrawer + 0.1} src="whoosh-short" volume={0.14} />
      <Sfx at={T3.poof} src="glass_001" volume={0.22} />

      {/* Account backup + restore */}
      <Sfx at={T3.backupPageIn - 0.02} src="whoosh-short" volume={0.2} />
      <Sfx at={T3.backupTap} src="click_002" volume={0.4} />
      <Sfx at={T3.backupDone + 0.08} src="confirmation_001" volume={0.28} />
      <Sfx at={T3.restoreTap} src="click_002" volume={0.35} />
      <Sfx at={T3.dissolve} src="whoosh-short" volume={0.18} />
      <Sfx at={T3.reassemble - 0.05} src="whoosh" volume={0.2} />
      <Sfx at={T3.restoredToast} src="glass_004" volume={0.22} />

      {/* Paywall */}
      <Sfx at={T3.paywallUp - 0.05} src="whoosh" volume={0.22} />
      {new Array(7).fill(0).map((_, i) => (
        <Sfx key={`d${i}`} at={T3.daysStart + i * 0.16} src="select_001" volume={0.2} />
      ))}
      <Sfx at={T3.monthly - 0.04} src="glass_002" volume={0.28} />
      <Sfx at={T3.once - 0.04} src="glass_002" volume={0.28} />

      {/* Outro */}
      <Sfx at={T3.outro - 0.42} src="whoosh" volume={0.2} />
      <Sfx at={T3.outro} src="impact" volume={0.35} />
      <Sfx at={T3.outro + 0.15} src="shimmer" volume={0.12} />
      {[0, 0.15, 0.3].map((d) => (
        <Sfx key={`l${d}`} at={T3.unlockLine + 0.1 + d} src="glass_001" volume={0.2} />
      ))}
    </>
  );
};
