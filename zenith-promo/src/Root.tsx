import { Composition } from "remotion";
import { ZenithPromo } from "./zenith/ZenithPromo";
import { ZenithPromo2 } from "./zenith2/ZenithPromo2";

export const RemotionRoot: React.FC = () => {
  return (
    <>
      <Composition id="ZenithPromo" component={ZenithPromo} durationInFrames={900} fps={30} width={1080} height={1920} />
      <Composition id="ZenithPromo2" component={ZenithPromo2} durationInFrames={810} fps={30} width={1080} height={1920} />
    </>
  );
};
