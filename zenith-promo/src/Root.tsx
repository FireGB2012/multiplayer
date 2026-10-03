import { Composition } from "remotion";
import { ZenithPromo } from "./zenith/ZenithPromo";

export const RemotionRoot: React.FC = () => {
  return (
    <>
      <Composition id="ZenithPromo" component={ZenithPromo} durationInFrames={900} fps={30} width={1080} height={1920} />
    </>
  );
};
