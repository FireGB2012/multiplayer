import React from "react";
import { AbsoluteFill } from "remotion";
import { StatusBar } from "../zenith/HomeScreen";
import { StockScreen } from "../zenith2/Home2";

// Renders the dull stock launcher (from ad #2) as a high-res texture for the
// trailer's glass pane: npx remotion still TrailerStockScreen public/trailer/stock.png
export const StockStill: React.FC = () => (
  <AbsoluteFill style={{ background: "#000" }}>
    <div style={{ position: "relative", width: 336, height: 716, zoom: 3, overflow: "hidden", fontFamily: "Inter" }}>
      <StockScreen />
      <StatusBar time="8:09" />
    </div>
  </AbsoluteFill>
);
