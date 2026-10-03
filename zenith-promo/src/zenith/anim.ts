import { Easing, interpolate, interpolateColors, useCurrentFrame, useVideoConfig } from "remotion";

// Everything in this video is timed in seconds of the final video, so it can be
// lined up with the voiceover by ear. The voiceover itself starts at VOICE_START.
export const VOICE_START = 0.5;

export const useTime = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  return frame / fps;
};

export const easeOut = Easing.bezier(0.16, 1, 0.3, 1);
export const easeInOut = Easing.bezier(0.65, 0, 0.35, 1);
export const easeIn = Easing.bezier(0.7, 0, 0.84, 0);
export const springy = Easing.spring({ damping: 14, stiffness: 120, mass: 0.9 });
export const softSpring = Easing.spring({ damping: 200 });

const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;

/** Tween a number between t0 and t1 (seconds). */
export const tw = (
  t: number,
  t0: number,
  t1: number,
  from: number,
  to: number,
  easing: (x: number) => number = easeOut,
) => interpolate(t, [t0, t1], [from, to], { ...clamp, easing });

/** 0 → 1 progress between t0 and t1. */
export const prog = (t: number, t0: number, t1: number, easing: (x: number) => number = easeOut) =>
  tw(t, t0, t1, 0, 1, easing);

/** Fade/scale-in window: 0 before `inAt`, 1 while visible, 0 after `outAt`. */
export const window01 = (t: number, inAt: number, outAt: number, inDur = 0.45, outDur = 0.35) =>
  Math.min(prog(t, inAt, inAt + inDur), 1 - prog(t, outAt, outAt + outDur, easeIn));

/** Step through keyed values with smooth color interpolation between keys. */
export const colorAt = (t: number, keys: [number, string][], blend = 0.35) => {
  const times: number[] = [];
  const colors: string[] = [];
  keys.forEach(([at, c], i) => {
    if (i === 0) {
      times.push(at);
      colors.push(c);
      return;
    }
    times.push(at, at + blend);
    colors.push(colors[colors.length - 1], c);
  });
  if (times.length === 1) return colors[0];
  return interpolateColors(t, times, colors);
};

/** Index of the last key whose time has passed. */
export const stepAt = <T,>(t: number, keys: [number, T][]): T => {
  let v = keys[0][1];
  for (const [at, val] of keys) if (t >= at) v = val;
  return v;
};
