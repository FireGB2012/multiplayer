import { easeInOut, tw } from "../zenith/anim";
import { hsv } from "../zenith2/theme2";

// Video-time (seconds) for every beat of the Zenith Plus ad, measured from the
// voiceover's pauses (voiceover starts at 0.5s).
export const T3 = {
  outOfBox: 0.5,
  homeAppear: 0.35,
  // "Zenith Plus lets you go further"
  plusBurst: 2.75,
  plusLine: 3.04,
  plusSheetUp: 3.15,
  unlockAt: [3.7, 3.88, 4.06, 4.24],
  themeTap: 4.72,
  themePush: 4.92,
  // Theme maker
  buildTheme: 5.32,
  slideAccent: 5.45,
  slideAccent2: 6.05,
  everyColour: 6.69,
  slideBg: 6.75,
  slideBgMid: 7.5,
  slideBgEnd: 8.1,
  themeDown: 8.3,
  // Free placement
  pressGmail: 8.72,
  dragGmail: 9.05,
  dropGmail: 9.48,
  dragGoogle: 9.58,
  dropGoogle: 9.95,
  anywhere: 9.01,
  anySize: 10.38,
  resizeStart: 10.3,
  shrinkTo: 10.92,
  growTo: 11.55,
  resizeDone: 11.8,
  // Hidden apps (whispered)
  hideSheetUp: 12.08,
  hideLine: 12.33,
  hideToggle: 12.72,
  hideDrawer: 13.0,
  poof: 13.55,
  hideDrawerDown: 14.15,
  // Account backup + restore
  backupPageIn: 14.3,
  backupLine: 14.63,
  backupTap: 14.8,
  backupDone: 15.8,
  restoreTap: 16.0,
  bringBack: 16.12,
  backupPageOut: 16.12,
  dissolve: 16.45,
  reassemble: 16.92,
  restoredToast: 17.32,
  // Paywall
  paywallUp: 17.6,
  trialLine: 17.88,
  daysStart: 18.45,
  monthly: 20.06,
  once: 22.64,
  ctaShine: 23.7,
  // Outro
  outro: 25.35,
  plusWord: 25.52,
  unlockLine: 26.78,
  end: 30,
};

export const GOLD1 = "#FFE08A";
export const GOLD2 = "#FFB547";
export const GOLD3 = "#FF9A3C";

export type Pal3 = {
  accent: string;
  accent2: string;
  base: string;
  s1: string; // silk streaks
  s2: string;
  glow: string;
};

/** Theme maker slider state; the whole look derives from these. */
export const sliders3 = (t: number) => {
  let bgH = 298;
  if (t >= T3.slideBg) bgH = tw(t, T3.slideBg, T3.slideBgMid, 298, 18, easeInOut);
  if (t >= T3.slideBgMid + 0.05) bgH = tw(t, T3.slideBgMid + 0.05, T3.slideBgEnd, 18, 185, easeInOut);
  return {
    accentH: tw(t, T3.slideAccent, T3.slideAccent + 0.5, 327, 42, easeInOut),
    accentS: tw(t, T3.slideAccent, T3.slideAccent + 0.5, 0.38, 0.62, easeInOut),
    accentV: tw(t, T3.slideAccent, T3.slideAccent + 0.5, 0.91, 1, easeInOut),
    accent2H: tw(t, T3.slideAccent2, T3.slideAccent2 + 0.45, 262, 190, easeInOut),
    accent2S: tw(t, T3.slideAccent2, T3.slideAccent2 + 0.45, 0.35, 0.65, easeInOut),
    bgH,
  };
};

export const paletteAt3 = (t: number): Pal3 => {
  const s = sliders3(t);
  return {
    accent: hsv(s.accentH, s.accentS, s.accentV),
    accent2: hsv(s.accent2H, s.accent2S, 1),
    base: hsv(s.bgH, 0.5, 0.12),
    s1: hsv(s.bgH, 0.32, 0.85),
    s2: hsv(s.bgH + 22, 0.4, 0.92),
    glow: hsv(Math.max(0, s.bgH - 12), 0.55, 0.5),
  };
};
