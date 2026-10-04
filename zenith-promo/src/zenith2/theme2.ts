import { easeInOut, tw } from "../zenith/anim";

// Video-time (seconds) for every beat of ad #2, measured from the voiceover's pauses
// (voiceover starts at 0.5s).
export const T2 = {
  launchersSit: 0.5,
  zenithMoves: 2.56,
  // App opening: colour card
  tapApp: 4.2,
  gmailTap: 4.45,
  gmailOpen: 4.55,
  gmailClose: 6.55,
  // App opening: liquid glass
  liquidGlass: 7.36,
  chromeTap: 7.45,
  chromeOpen: 7.55,
  chromeClose: 8.85,
  // Drawer + search
  swipeUp: 9.45,
  drawerUp: 9.6,
  searchAway: 10.49,
  typeAt: [10.62, 10.8, 10.98, 11.16],
  drawerDown: 12.1,
  // Free placement
  control: 12.5,
  pressAt: 13.3,
  dragStart: 13.85,
  drop: 14.65,
  anywhere: 13.8,
  anySize: 15.25,
  resizeStart: 15.1,
  resizeEnd: 15.8,
  resizeDone: 15.95,
  // Hidden apps
  hideSheetUp: 15.95,
  hideToggle: 16.5,
  hideDrawer: 16.95,
  poof: 17.35,
  hideDrawerDown: 17.75,
  hideLine: 16.24,
  // Theme maker
  themeIn: 17.85,
  themeLine: 17.96,
  slideAccent: 18.2,
  slideAccent2: 18.72,
  slideBg: 19.22,
  themeDown: 19.7,
  // Backup
  backup: 20.05,
  backupDone: 20.9,
  backupGone: 21.35,
  // Outro
  outro: 21.5,
  zenithWord: 21.72,
  finallyYours: 22.51,
  end: 27,
};

export type Pal2 = {
  accent: string;
  accent2: string;
  base: string;
  n1: string; // nebula colours
  n2: string;
  n3: string;
};

export const hsv = (h: number, s: number, v: number) => {
  const f = (n: number) => {
    const k = (n + h / 60) % 6;
    return v - v * s * Math.max(0, Math.min(k, 4 - k, 1));
  };
  const hex = (x: number) =>
    Math.round(Math.max(0, Math.min(1, x)) * 255)
      .toString(16)
      .padStart(2, "0");
  return `#${hex(f(5))}${hex(f(3))}${hex(f(1))}`.toUpperCase();
};

/** Theme maker slider state; everything visual derives from these. */
export const sliders = (t: number) => ({
  accentH: tw(t, T2.slideAccent, T2.slideAccent + 0.4, 252, 338, easeInOut),
  accent2H: tw(t, T2.slideAccent2, T2.slideAccent2 + 0.38, 192, 44, easeInOut),
  bgH: tw(t, T2.slideBg, T2.slideBg + 0.33, 236, 328, easeInOut),
  bgV: tw(t, T2.slideBg, T2.slideBg + 0.33, 0.095, 0.085, easeInOut),
});

export const paletteAt2 = (t: number): Pal2 => {
  const s = sliders(t);
  return {
    accent: hsv(s.accentH, 0.46, 1),
    accent2: hsv(s.accent2H, 0.72, 1),
    base: hsv(s.bgH, 0.55, s.bgV),
    n1: hsv(s.accentH, 0.62, 0.58),
    n2: hsv(s.accent2H, 0.72, 0.5),
    n3: hsv(s.bgH, 0.6, 0.32),
  };
};
