import { Composition, Folder, Still } from "remotion";
import { ZenithPromo } from "./zenith/ZenithPromo";
import { ZenithPromo2 } from "./zenith2/ZenithPromo2";
import { ZenithPlusAd } from "./zenith3/ZenithPlusAd";
import { ZenithTrailer } from "./trailer/Trailer";
import { StockStill } from "./trailer/StockStill";

export const RemotionRoot: React.FC = () => {
  return (
    <>
      <Composition id="ZenithPromo" component={ZenithPromo} durationInFrames={900} fps={30} width={1080} height={1920} />
      <Composition id="ZenithPromo2" component={ZenithPromo2} durationInFrames={810} fps={30} width={1080} height={1920} />
      <Composition id="ZenithPlusAd" component={ZenithPlusAd} durationInFrames={900} fps={30} width={1080} height={1920} />
      <Composition id="ZenithTrailer" component={ZenithTrailer} durationInFrames={600} fps={30} width={1080} height={1920} defaultProps={{ launch: "date" as const }} />
      <Folder name="Assets">
        <Still id="TrailerStockScreen" component={StockStill} width={1008} height={2148} />
      </Folder>
      <Composition id="ZenithTrailerOutNow" component={ZenithTrailer} durationInFrames={600} fps={30} width={1080} height={1920} defaultProps={{ launch: "now" as const }} />
    </>
  );
};
