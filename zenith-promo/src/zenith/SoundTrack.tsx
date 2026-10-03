import React from "react";
import { Audio } from "@remotion/media";
import { interpolate, staticFile, useVideoConfig } from "remotion";
import { VOICE_START } from "./anim";
import { T } from "./timeline";

const Sfx: React.FC<{ at: number; src: string; volume?: number }> = ({ at, src, volume = 0.6 }) => {
  const { fps } = useVideoConfig();
  return (
    <Audio name={src} src={staticFile(`sfx/${src}.wav`)} from={Math.round(at * fps)} premountFor={fps} volume={volume} />
  );
};

export const SoundTrack: React.FC = () => {
  const { fps } = useVideoConfig();
  return (
    <>
      <Audio name="Voiceover" src={staticFile("voiceover.mp3")} from={Math.round(VOICE_START * fps)} premountFor={fps} volume={1} />
      <Audio
        name="Music"
        src={staticFile("music.wav")}
        volume={(f) =>
          interpolate(f / fps, [0, 0.4, 27.9, 28.4], [0.42, 0.24, 0.24, 0.5], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          })
        }
      />

      {/* Intro */}
      <Sfx at={T.yourPhone - 0.12} src="whoosh-short" volume={0.3} />
      <Sfx at={T.yourHomeScreen - 0.12} src="whoosh-short" volume={0.3} />
      <Sfx at={T.yourRules - 0.12} src="whoosh-short" volume={0.35} />
      <Sfx at={0.6} src="riser" volume={0.45} />
      <Sfx at={T.logoIn - 0.03} src="impact" volume={0.85} />
      <Sfx at={T.logoIn + 0.35} src="shimmer" volume={0.55} />

      {/* Phone arrives */}
      <Sfx at={T.phoneIn - 0.1} src="whoosh" volume={0.6} />
      <Sfx at={T.phoneIn + 0.5} src="glass_003" volume={0.4} />
      <Sfx at={6.9} src="shimmer" volume={0.3} />

      {/* Look */}
      <Sfx at={T.lookIn - 0.05} src="whoosh-short" volume={0.5} />
      <Sfx at={8.6} src="glass_002" volume={0.7} />
      <Sfx at={9.0} src="glass_002" volume={0.7} />
      <Sfx at={9.45} src="glass_002" volume={0.7} />
      {[0.2, 0.32, 0.44, 0.56, 0.68, 0.85, 1.0].map((d) => (
        <Sfx key={d} at={T.dialGlass + d} src="select_001" volume={0.35} />
      ))}
      <Sfx at={T.tilt + 0.05} src="toggle_001" volume={0.75} />
      <Sfx at={T.tilt + 0.35} src="whoosh-short" volume={0.3} />
      <Sfx at={T.lightFollows - 0.15} src="shimmer" volume={0.55} />

      {/* Home screen */}
      <Sfx at={T.homeSheetIn - 0.05} src="whoosh-short" volume={0.45} />
      <Sfx at={13.62} src="glass_005" volume={0.7} />
      <Sfx at={13.95} src="glass_005" volume={0.7} />
      <Sfx at={14.25} src="glass_005" volume={0.7} />
      <Sfx at={14.38} src="glass_006" volume={0.7} />
      <Sfx at={14.72} src="glass_006" volume={0.7} />
      <Sfx at={15.05} src="glass_006" volume={0.7} />
      <Sfx at={T.sheetDown - 0.05} src="whoosh-down" volume={0.5} />
      <Sfx at={T.everySize + 0.1} src="glass_004" volume={0.6} />
      <Sfx at={T.everySize + 0.25} src="whoosh-short" volume={0.3} />

      {/* Aura */}
      <Sfx at={T.auraIn - 0.05} src="whoosh-short" volume={0.5} />
      {[0, 0.12, 0.24, 0.36, 0.48, 0.6, 0.72, 0.84].map((d) => (
        <Sfx key={d} at={T.stackAura + d} src="select_001" volume={0.25 + d * 0.3} />
      ))}
      <Sfx at={T.stackAura + 0.95} src="confirmation_001" volume={0.55} />
      <Sfx at={T.unlockThemes} src="confirmation_002" volume={0.6} />
      <Sfx at={T.unlockThemes + 0.1} src="shimmer" volume={0.45} />

      {/* Plus */}
      <Sfx at={T.plusIn - 0.05} src="whoosh-short" volume={0.45} />
      <Sfx at={T.zenithPlus + 0.1} src="shimmer" volume={0.4} />
      <Sfx at={T.themeMaker} src="glass_001" volume={0.65} />
      <Sfx at={T.hiddenApps} src="glass_001" volume={0.65} />
      <Sfx at={T.freePlacement} src="toggle_002" volume={0.75} />
      <Sfx at={T.freePlacement + 0.35} src="glass_001" volume={0.5} />
      <Sfx at={T.freePlacement + 0.5} src="glass_001" volume={0.5} />

      {/* Outro */}
      <Sfx at={T.outro - 0.15} src="whoosh" volume={0.55} />
      <Sfx at={T.outro + 0.05} src="impact" volume={0.75} />
      <Sfx at={T.outro + 0.3} src="shimmer" volume={0.55} />
      <Sfx at={T.makeItYours} src="glass_004" volume={0.35} />
    </>
  );
};
