import React from "react";

// Simplified vector app glyphs (drawn on a 48x48 box) so they stay crisp at any size.

export const GoogleG: React.FC<{ size: number }> = ({ size }) => (
  <svg width={size} height={size} viewBox="0 0 48 48">
    <path fill="#EA4335" d="M24 9.5c3.5 0 6.6 1.2 9.1 3.6l6.8-6.8C35.8 2.4 30.3 0 24 0 14.6 0 6.6 5.4 2.7 13.3l7.9 6.1C12.5 13.6 17.8 9.5 24 9.5z" />
    <path fill="#4285F4" d="M46.1 24.5c0-1.6-.1-3.1-.4-4.5H24v9h12.4c-.5 2.9-2.2 5.3-4.6 7l7.6 5.9c4.4-4.1 6.7-10.1 6.7-17.4z" />
    <path fill="#FBBC05" d="M10.5 28.6c-.5-1.4-.8-3-.8-4.6s.3-3.2.8-4.6l-7.9-6.1C1 16.6 0 20.2 0 24s1 7.4 2.7 10.7l7.8-6.1z" />
    <path fill="#34A853" d="M24 48c6.5 0 11.9-2.1 15.9-5.8l-7.6-5.9c-2.1 1.4-4.9 2.3-8.3 2.3-6.2 0-11.5-4.2-13.4-9.9l-7.9 6.1C6.6 42.6 14.6 48 24 48z" />
  </svg>
);

export const GmailM: React.FC<{ size: number }> = ({ size }) => (
  <svg width={size} height={size * 0.75} viewBox="0 0 48 36">
    <path fill="#4285F4" d="M3.3 36h7.6V17.5L0 9.4v23.3C0 34.5 1.5 36 3.3 36z" />
    <path fill="#34A853" d="M37.1 36h7.6c1.8 0 3.3-1.5 3.3-3.3V9.4l-10.9 8.1z" />
    <path fill="#FBBC04" d="M37.1 3.4v14.1L48 9.4V5c0-4.1-4.7-6.4-7.9-4z" />
    <path fill="#EA4335" d="M10.9 17.5V3.4L24 13.2l13.1-9.8v14.1L24 27.3z" />
    <path fill="#C5221F" d="M0 5v4.4l10.9 8.1V3.4L7.9 1C4.7-1.4 0 .9 0 5z" />
  </svg>
);

export const PhoneGlyph: React.FC<{ size: number }> = ({ size }) => (
  <svg width={size} height={size} viewBox="0 0 48 48">
    <path
      fill="#1a73e8"
      d="M13.6 6.5c1.2-.9 2.9-.7 3.8.5l4.2 5.6c.8 1.1.7 2.6-.2 3.6l-2.6 2.7c1.9 4 5 7.2 9 9.1l2.7-2.6c1-.9 2.5-1 3.6-.2l5.6 4.2c1.2.9 1.4 2.6.5 3.8l-2.6 3.4c-1.4 1.8-3.8 2.6-6 1.8C20.3 35.3 12.7 27.7 9.6 16.4c-.7-2.2 0-4.6 1.8-6z"
    />
  </svg>
);

export const MessagesGlyph: React.FC<{ size: number }> = ({ size }) => (
  <svg width={size} height={size} viewBox="0 0 48 48">
    <path fill="#1a73e8" d="M8 10c0-2.2 1.8-4 4-4h24c2.2 0 4 1.8 4 4v18c0 2.2-1.8 4-4 4H20l-9 7v-7h1c-2.2 0-4-1.8-4-4z" />
    <rect x="15" y="14" width="18" height="3" rx="1.5" fill="#fff" />
    <rect x="15" y="21" width="12" height="3" rx="1.5" fill="#fff" />
  </svg>
);

export const ChromeGlyph: React.FC<{ size: number }> = ({ size }) => (
  <svg width={size} height={size} viewBox="0 0 48 48">
    <circle cx="24" cy="24" r="22" fill="#34A853" />
    <path fill="#EA4335" d="M24 2a22 22 0 0 1 19 11H24a11 11 0 0 0-9.5 5.5L5 12.6A22 22 0 0 1 24 2z" />
    <path fill="#FBBC04" d="M43 13a22 22 0 0 1-17.6 33L35 29.5A11 11 0 0 0 35 18.5V13z" />
    <circle cx="24" cy="24" r="10" fill="#fff" />
    <circle cx="24" cy="24" r="7.8" fill="#1a73e8" />
  </svg>
);

export const CameraGlyph: React.FC<{ size: number }> = ({ size }) => (
  <svg width={size} height={size} viewBox="0 0 48 48">
    <rect x="4" y="12" width="40" height="28" rx="7" fill="#fff" />
    <path fill="#fff" d="M16 12l3-5h10l3 5z" />
    <circle cx="24" cy="26" r="9" fill="#1a73e8" />
    <circle cx="24" cy="26" r="5" fill="#8ab4f8" />
  </svg>
);

export const LockGlyph: React.FC<{ size: number; color?: string }> = ({ size, color = "#fff" }) => (
  <svg width={size} height={size} viewBox="0 0 24 24">
    <path fill={color} d="M7 10V8a5 5 0 0 1 10 0v2h.5A1.5 1.5 0 0 1 19 11.5v8a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 5 19.5v-8A1.5 1.5 0 0 1 6.5 10zm2 0h6V8a3 3 0 0 0-6 0z" />
  </svg>
);

export const CheckGlyph: React.FC<{ size: number; color?: string }> = ({ size, color = "#fff" }) => (
  <svg width={size} height={size} viewBox="0 0 24 24">
    <path fill="none" stroke={color} strokeWidth={3} strokeLinecap="round" strokeLinejoin="round" d="M5 12.5l4.5 4.5L19 7.5" />
  </svg>
);

export const BackArrow: React.FC<{ size: number }> = ({ size }) => (
  <svg width={size} height={size} viewBox="0 0 24 24">
    <path fill="none" stroke="#fff" strokeWidth={2.2} strokeLinecap="round" strokeLinejoin="round" d="M19 12H5m6-6l-6 6 6 6" />
  </svg>
);

export const SearchGlyph: React.FC<{ size: number }> = ({ size }) => (
  <svg width={size} height={size} viewBox="0 0 24 24">
    <circle cx="10.5" cy="10.5" r="6" fill="none" stroke="rgba(255,255,255,0.75)" strokeWidth={2.2} />
    <path stroke="rgba(255,255,255,0.75)" strokeWidth={2.2} strokeLinecap="round" d="M15 15l4.5 4.5" />
  </svg>
);

export const FlameGlyph: React.FC<{ size: number; color?: string }> = ({ size, color = "#ff8a4c" }) => (
  <svg width={size} height={size} viewBox="0 0 24 24">
    <path fill={color} d="M12 2c1 3.5 5.5 6 5.5 11.2A5.6 5.6 0 0 1 12 19a5.6 5.6 0 0 1-5.5-5.8c0-2.4 1.2-4 2.5-5.2.2 1.6 1 2.7 2.2 3.2C10.6 8.3 11 5 12 2z" />
  </svg>
);

export const SparkleGlyph: React.FC<{ size: number; color?: string }> = ({ size, color = "#fff" }) => (
  <svg width={size} height={size} viewBox="0 0 24 24">
    <path fill={color} d="M10 3l1.8 5.2L17 10l-5.2 1.8L10 17l-1.8-5.2L3 10l5.2-1.8zM18 13l.9 2.1L21 16l-2.1.9L18 19l-.9-2.1L15 16l2.1-.9z" />
  </svg>
);

export const StatusIcons: React.FC = () => (
  <div style={{ display: "flex", gap: 4, alignItems: "center" }}>
    <svg width={13} height={10} viewBox="0 0 13 10">
      {[0, 1, 2, 3].map((i) => (
        <rect key={i} x={i * 3.3} y={8 - i * 2.4} width={2.4} height={2 + i * 2.4} rx={0.6} fill="#fff" />
      ))}
    </svg>
    <svg width={13} height={10} viewBox="0 0 24 18">
      <path fill="#fff" d="M12 18L0 4.5C3.3 1.7 7.5 0 12 0s8.7 1.7 12 4.5z" />
    </svg>
    <div style={{ width: 19, height: 9, borderRadius: 3, background: "#fff", position: "relative" }}>
      <div style={{ position: "absolute", right: -2.5, top: 3, width: 1.6, height: 3, borderRadius: 1, background: "#fff" }} />
    </div>
  </div>
);
