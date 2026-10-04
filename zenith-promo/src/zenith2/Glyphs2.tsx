import React from "react";
import { Img, staticFile } from "remotion";
import { CameraGlyph, ChromeGlyph, GmailM, GoogleG, MessagesGlyph, PhoneGlyph } from "../zenith/AppIcons";

// Simplified vector app glyphs on a 48x48 box, for the app drawer.

const Calendar: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <rect x="6" y="6" width="36" height="36" rx="6" fill="#4285F4" />
    <rect x="6" y="6" width="36" height="9" rx="4" fill="#1967D2" />
    <text x="24" y="37" textAnchor="middle" fontFamily="Inter" fontWeight="800" fontSize="19" fill="#fff">
      31
    </text>
  </svg>
);

const Clock: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <circle cx="24" cy="24" r="19" fill="#5b6cf0" />
    <circle cx="24" cy="24" r="14" fill="#e8ecff" />
    <path d="M24 24L24 14M24 24l7 5" stroke="#3b4bd8" strokeWidth="3" strokeLinecap="round" />
  </svg>
);

const Contacts: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <circle cx="24" cy="17" r="8" fill="#1a73e8" />
    <path d="M10 40c1-8 7-12 14-12s13 4 14 12z" fill="#1a73e8" />
  </svg>
);

const Drive: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <path d="M17 8h14l13 22h-14z" fill="#FBBC04" />
    <path d="M17 8L4 30l7 12 13-22z" fill="#34A853" />
    <path d="M11 42h26l7-12H18z" fill="#4285F4" />
  </svg>
);

const Files: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <path d="M6 14a4 4 0 0 1 4-4h10l4 4h14a4 4 0 0 1 4 4v18a4 4 0 0 1-4 4H10a4 4 0 0 1-4-4z" fill="#4c8df6" />
    <rect x="6" y="18" width="36" height="22" rx="4" fill="#7fb0ff" />
  </svg>
);

const Maps: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <path d="M24 4c-8 0-14 6-14 14 0 10 14 26 14 26s14-16 14-26c0-8-6-14-14-14z" fill="#EA4335" />
    <path d="M10 18c0-4 1.6-7.5 4.2-10L24 18z" fill="#4285F4" />
    <path d="M24 18l9.8-10C36.4 10.5 38 14 38 18z" fill="#FBBC04" />
    <circle cx="24" cy="18" r="5" fill="#fff" />
  </svg>
);

const Photos: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <path d="M24 24V6a9 9 0 0 1 0 18z" fill="#EA4335" />
    <path d="M24 24h18a9 9 0 0 1-18 0z" fill="#4285F4" />
    <path d="M24 24v18a9 9 0 0 1 0-18z" fill="#34A853" />
    <path d="M24 24H6a9 9 0 0 1 18 0z" fill="#FBBC04" />
  </svg>
);

const PlayStore: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <path d="M10 6l20 18-20 18z" fill="#34A853" />
    <path d="M10 6l26 15-6 3z" fill="#4285F4" />
    <path d="M10 42l26-15-6-3z" fill="#EA4335" />
    <path d="M30 24l6-3 6 3-6 3z" fill="#FBBC04" />
  </svg>
);

const Settings: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <circle cx="24" cy="24" r="14" fill="none" stroke="#5f6368" strokeWidth="7" strokeDasharray="5.5 5.5" />
    <circle cx="24" cy="24" r="11" fill="#5f6368" />
    <circle cx="24" cy="24" r="5" fill="#fff" />
  </svg>
);

const YouTube: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <rect x="4" y="11" width="40" height="27" rx="8" fill="#FF0000" />
    <path d="M20 18v13l11-6.5z" fill="#fff" />
  </svg>
);

const YTMusic: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <circle cx="24" cy="24" r="19" fill="#FF0000" />
    <circle cx="24" cy="24" r="11" fill="none" stroke="#fff" strokeWidth="2" />
    <path d="M21 19v10l8-5z" fill="#fff" />
  </svg>
);

const Weather: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <circle cx="20" cy="20" r="9" fill="#FBBC04" />
    <path d="M16 38a8 8 0 0 1 1-16 10 10 0 0 1 19 3 6.5 6.5 0 0 1 0 13z" fill="#8ab4f8" />
  </svg>
);

const Calculator: React.FC<{ s: number }> = ({ s }) => (
  <svg width={s} height={s} viewBox="0 0 48 48">
    <rect x="8" y="5" width="32" height="38" rx="7" fill="#3c4043" />
    <rect x="13" y="10" width="22" height="8" rx="2" fill="#a8c7fa" />
    {[0, 1, 2].map((r) =>
      [0, 1, 2].map((c) => <circle key={`${r}${c}`} cx={16 + c * 8} cy={25 + r * 7} r="2.4" fill={c === 2 && r === 2 ? "#f9ab00" : "#e8eaed"} />),
    )}
  </svg>
);

const ZenithIcon: React.FC<{ s: number }> = ({ s }) => (
  <Img src={staticFile("zenith-icon.png")} style={{ width: s * 1.25, height: s * 1.25 * (671 / 653) }} />
);

export type DrawerApp = { name: string; bg: string; glyph: (s: number) => React.ReactNode };

export const DRAWER_APPS: DrawerApp[] = [
  { name: "Calendar", bg: "#fff", glyph: (s) => <Calendar s={s} /> },
  { name: "Camera", bg: "#4c8df6", glyph: (s) => <CameraGlyph size={s * 0.8} /> },
  { name: "Chrome", bg: "#fff", glyph: (s) => <ChromeGlyph size={s * 0.8} /> },
  { name: "Clock", bg: "#fff", glyph: (s) => <Clock s={s} /> },
  { name: "Contacts", bg: "#fff", glyph: (s) => <Contacts s={s} /> },
  { name: "Drive", bg: "#fff", glyph: (s) => <Drive s={s * 0.85} /> },
  { name: "Files", bg: "#fff", glyph: (s) => <Files s={s * 0.85} /> },
  { name: "Gmail", bg: "#fff", glyph: (s) => <GmailM size={s * 0.72} /> },
  { name: "Google", bg: "#fff", glyph: (s) => <GoogleG size={s * 0.62} /> },
  { name: "Maps", bg: "#fff", glyph: (s) => <Maps s={s * 0.85} /> },
  { name: "Messages", bg: "#fff", glyph: (s) => <MessagesGlyph size={s * 0.8} /> },
  { name: "Phone", bg: "#fff", glyph: (s) => <PhoneGlyph size={s * 0.75} /> },
  { name: "Photos", bg: "#fff", glyph: (s) => <Photos s={s * 0.8} /> },
  { name: "Play Store", bg: "#fff", glyph: (s) => <PlayStore s={s * 0.75} /> },
  { name: "Settings", bg: "#fff", glyph: (s) => <Settings s={s * 0.8} /> },
  { name: "Weather", bg: "#fff", glyph: (s) => <Weather s={s * 0.85} /> },
  { name: "YouTube", bg: "#fff", glyph: (s) => <YouTube s={s * 0.8} /> },
  { name: "YT Music", bg: "#fff", glyph: (s) => <YTMusic s={s * 0.8} /> },
  { name: "Calculator", bg: "#fff", glyph: (s) => <Calculator s={s * 0.8} /> },
  { name: "Zenith", bg: "transparent", glyph: (s) => <ZenithIcon s={s} /> },
];
