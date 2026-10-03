// Video-time (seconds) of every beat, measured from the voiceover's pauses
// (voiceover starts at 0.5s). Tweak these to re-sync visuals.
export const T = {
  // Intro
  yourPhone: 0.5,
  yourHomeScreen: 1.62,
  yourRules: 2.76,
  introOut: 3.75,
  // Logo
  logoIn: 4.0,
  launcherLine: 5.62,
  phoneIn: 5.45,
  // Look sheet
  lookIn: 8.25,
  pickColour: 8.46,
  dialGlass: 9.48,
  tilt: 10.96,
  lightFollows: 11.9,
  // Home screen sheet
  homeSheetIn: 13.2,
  grid: 13.32,
  shapes: 14.29,
  sheetDown: 15.3,
  everySize: 15.44,
  // Aura
  auraIn: 17.3,
  stackAura: 18.3,
  unlockThemes: 19.45,
  // Plus
  plusIn: 20.75,
  wantMore: 20.94,
  zenithPlus: 21.75,
  themeMaker: 22.95,
  hiddenApps: 23.8,
  freePlacement: 24.55,
  // Outro
  outro: 25.7,
  zenithWord: 25.98,
  makeItYours: 26.88,
  end: 30,
};

export const THEMES = {
  sunset: { accent: "#ef8a5e", a: "#b9643d", b: "#c84a68", base: "#130a0f" },
  aurora: { accent: "#3fcfb0", a: "#1f8f84", b: "#2f62b8", base: "#06100f" },
  midnight: { accent: "#7d7bff", a: "#4b3fc4", b: "#2a4aa6", base: "#08091a" },
  ember: { accent: "#ff7a45", a: "#c4451f", b: "#9c2c3a", base: "#140806" },
};

// Theme changes driven by the "Pick your colour" beat, and by the Ember unlock.
export const THEME_KEYS: [number, keyof typeof THEMES][] = [
  [0, "sunset"],
  [8.6, "aurora"],
  [9.0, "midnight"],
  [9.45, "sunset"],
];

export const SWATCHES = [
  { name: "Midnight", c1: "#5b8cff", c2: "#8f6bff" },
  { name: "Aurora", c1: "#2fd1a4", c2: "#3aa7d6" },
  { name: "Sunset", c1: "#ff8a6b", c2: "#ff5f6d" },
  { name: "Frost", c1: "#4fb5ff", c2: "#1f7cf2" },
  { name: "Sakura", c1: "#f6a0e0", c2: "#c47cf0" },
  { name: "Graphite", c1: "#e2e4e9", c2: "#9aa0aa" },
];
