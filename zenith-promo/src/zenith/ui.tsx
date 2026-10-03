import React from "react";
import { interpolate, interpolateColors } from "remotion";

// Screen UI is authored in "phone px" (a 336x716 screen) and zoomed 2x.

export const glass = (strength = 1): React.CSSProperties => ({
  background: `linear-gradient(160deg, rgba(255,255,255,${0.13 * strength}), rgba(255,255,255,${0.035 * strength}))`,
  backdropFilter: `blur(${14 * strength}px) saturate(150%)`,
  border: `0.6px solid rgba(255,255,255,${0.16 * strength})`,
  boxShadow: `inset 0 0.8px 0 rgba(255,255,255,${0.22 * strength}), 0 8px 24px rgba(0,0,0,0.28)`,
});

export const Card: React.FC<{ style?: React.CSSProperties; children: React.ReactNode }> = ({ style, children }) => (
  <div
    style={{
      background: "rgba(255,255,255,0.055)",
      border: "0.5px solid rgba(255,255,255,0.07)",
      borderRadius: 14,
      padding: "10px 12px",
      ...style,
    }}
  >
    {children}
  </div>
);

export const Label: React.FC<{ children: React.ReactNode; style?: React.CSSProperties }> = ({ children, style }) => (
  <div style={{ fontSize: 8, color: "rgba(255,255,255,0.65)", fontWeight: 500, ...style }}>{children}</div>
);

/** A pill switch. `on` is 0..1 so it can animate. */
export const Toggle: React.FC<{ on: number; accent: string }> = ({ on, accent }) => (
  <div
    style={{
      width: 34,
      height: 20,
      borderRadius: 10,
      background: interpolateColors(Math.min(Math.max(on, 0), 1), [0, 1], ["rgba(255,255,255,0.18)", accent]),
      position: "relative",
      flexShrink: 0,
    }}
  >
    <div
      style={{
        position: "absolute",
        top: 2.5,
        left: interpolate(on, [0, 1], [3, 16.5]),
        width: 15,
        height: 15,
        borderRadius: 8,
        background: "#fff",
        boxShadow: "0 1px 3px rgba(0,0,0,0.35)",
      }}
    />
  </div>
);

/**
 * Segmented control whose highlight slides between options.
 * `pos` is the (fractional) index of the selected option.
 */
export const Segmented: React.FC<{ options: string[]; pos: number; accent: string; height?: number }> = ({
  options,
  pos,
  accent,
  height = 26,
}) => {
  const w = 100 / options.length;
  return (
    <div
      style={{
        position: "relative",
        height,
        borderRadius: height / 2,
        background: "rgba(0,0,0,0.28)",
        border: "0.5px solid rgba(255,255,255,0.12)",
        display: "flex",
      }}
    >
      <div
        style={{
          position: "absolute",
          top: 2,
          bottom: 2,
          left: `calc(${pos * w}% + 2px)`,
          width: `calc(${w}% - 4px)`,
          borderRadius: height / 2,
          background: accent,
          boxShadow: `0 2px 10px ${accent}66`,
        }}
      />
      {options.map((o, i) => (
        <div
          key={o}
          style={{
            flex: 1,
            position: "relative",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 8,
            fontWeight: 600,
            color: Math.abs(pos - i) < 0.5 ? "#1a0d10" : "rgba(255,255,255,0.75)",
          }}
        >
          {o}
        </div>
      ))}
    </div>
  );
};

export const IconTile: React.FC<{ color: string; children: React.ReactNode; size?: number }> = ({
  color,
  children,
  size = 26,
}) => (
  <div
    style={{
      width: size,
      height: size,
      borderRadius: size * 0.3,
      background: color,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      flexShrink: 0,
    }}
  >
    {children}
  </div>
);

export const SheetHeader: React.FC<{ title: string; back?: boolean }> = ({ title, back = true }) => (
  <div style={{ display: "flex", alignItems: "center", gap: 8, margin: "4px 2px 12px" }}>
    {back ? (
      <div
        style={{
          width: 24,
          height: 24,
          borderRadius: 12,
          background: "rgba(255,255,255,0.1)",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        <svg width={12} height={12} viewBox="0 0 24 24">
          <path fill="none" stroke="#fff" strokeWidth={2.6} strokeLinecap="round" strokeLinejoin="round" d="M19 12H5m6-6l-6 6 6 6" />
        </svg>
      </div>
    ) : null}
    <div style={{ fontSize: 14, fontWeight: 700, color: "#fff", letterSpacing: -0.2 }}>{title}</div>
  </div>
);
