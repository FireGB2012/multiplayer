import React, { useLayoutEffect, useMemo, useState } from "react";
import * as THREE from "three";
import { useThree } from "@react-three/fiber";
import { ThreeCanvas } from "@remotion/three";
import { RoomEnvironment } from "three/examples/jsm/environments/RoomEnvironment.js";
import { cancelRender, continueRender, delayRender, random, staticFile, useVideoConfig } from "remotion";
import { easeInOut, easeOut, prog, useTime } from "../zenith/anim";
import { IMPACT, PANE_H, PANE_R, PANE_W, fracture, roundedRect, type Cell } from "./fracture";
import { TT } from "./tt";

// Real 3D glass: a physically based pane (transmission, thickness, IOR, green-tinted
// edges) printed with the old stock home screen. It cracks, flies apart in slow motion
// and the pieces spiral in to become the Zenith icon. Everything is driven by the frame.

const DEPTH = 0.036;
const BEVEL = 0.008;
/** The screen image sits just in front of the bevelled glass face. */
const DECAL_Z = DEPTH / 2 + BEVEL + 0.004;
const CAM_Z = 18;
const FOV = 30;
/** Frame pixels per world unit on the z=0 plane. */
export const PX_PER_UNIT = 1920 / (2 * CAM_Z * Math.tan((FOV / 2) * (Math.PI / 180)));

/**
 * Loads a texture and holds the render until it is on screen. While rendering, the
 * canvas only redraws on frame changes, so redraw once more when the image arrives.
 */
const useTextureFile = (file: string) => {
  const advance = useThree((s) => s.advance);
  const [handle] = useState(() => delayRender(`texture ${file}`));
  return useMemo(() => {
    const tex = new THREE.TextureLoader().load(
      staticFile(file),
      () => {
        advance(performance.now());
        continueRender(handle);
      },
      undefined,
      (e) => cancelRender(e),
    );
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = 8;
    return tex;
  }, [file, handle, advance]);
};

/** Studio-style reflections (no network needed) so the glass has something to mirror. */
const Environment: React.FC = () => {
  const { gl, scene } = useThree();
  const env = useMemo(() => {
    const pm = new THREE.PMREMGenerator(gl);
    const rt = pm.fromScene(new RoomEnvironment(), 0.04);
    pm.dispose();
    return rt.texture;
  }, [gl]);
  useLayoutEffect(() => {
    scene.environment = env;
    scene.environmentIntensity = 1.1;
    return () => {
      scene.environment = null;
    };
  }, [env, scene]);
  return null;
};

/** Faces: clear, barely-there glass. Sides: the green edge real glass shows when it's thick. */
const glassMaterials = (): THREE.Material[] => [
  new THREE.MeshPhysicalMaterial({
    color: new THREE.Color("#ffffff"),
    metalness: 0,
    roughness: 0,
    transmission: 1,
    thickness: 0.12,
    ior: 1.5,
    dispersion: 0.35, // rainbow fringes on the edges, like real glass
    attenuationColor: new THREE.Color("#bfeee0"),
    attenuationDistance: 1.6,
    specularIntensity: 1,
    clearcoat: 0.3,
    clearcoatRoughness: 0,
    envMapIntensity: 1.2,
  }),
  new THREE.MeshPhysicalMaterial({
    color: new THREE.Color("#c4f0e2"),
    metalness: 0,
    roughness: 0.03,
    transmission: 0.9,
    thickness: 0.3,
    ior: 1.5,
    attenuationColor: new THREE.Color("#5fcfa8"),
    attenuationDistance: 0.3,
    emissive: new THREE.Color("#4fbf9f"),
    emissiveIntensity: 0.07,
    envMapIntensity: 1.2,
  }),
];

/** Map pane-space coordinates (centred on the pane) to the screen texture. */
const paneUV = (x: number, y: number) => [(x + PANE_W / 2) / PANE_W, (y + PANE_H / 2) / PANE_H] as const;

const decalGeometry = (poly: [number, number][], cx: number, cy: number) => {
  const shape = new THREE.Shape(poly.map(([x, y]) => new THREE.Vector2(x, y)));
  const geo = new THREE.ShapeGeometry(shape);
  const pos = geo.attributes.position;
  const uv = geo.attributes.uv;
  for (let i = 0; i < pos.count; i++) {
    const [u, v] = paneUV(pos.getX(i) + cx, pos.getY(i) + cy);
    uv.setXY(i, u, v);
  }
  uv.needsUpdate = true;
  return geo;
};

const glassGeometry = (poly: [number, number][]) => {
  const shape = new THREE.Shape(poly.map(([x, y]) => new THREE.Vector2(x, y)));
  const geo = new THREE.ExtrudeGeometry(shape, {
    depth: DEPTH,
    bevelEnabled: true,
    bevelThickness: BEVEL,
    bevelSize: 0.006,
    bevelSegments: 3,
    steps: 1,
  });
  geo.translate(0, 0, -DEPTH / 2);
  return geo;
};

type ShardSim = {
  cell: Cell;
  glass: THREE.ExtrudeGeometry;
  decal: THREE.ShapeGeometry;
  dir: [number, number];
  vel: [number, number, number];
  spin: [number, number, number];
  jit: [number, number, number];
  delay: number;
};

const useShards = () =>
  useMemo<ShardSim[]>(() => {
    return fracture().map((cell, i) => {
      const dx = cell.cx - IMPACT[0];
      const dy = cell.cy - IMPACT[1];
      const dl = Math.hypot(dx, dy) || 1;
      // a frozen-in-time burst: pieces near the hit move most, the rest just separate
      const speed = (0.35 + 0.95 / (1 + 2.5 * cell.dist)) * (0.7 + random(`v${i}`) * 0.6);
      const toCam = random(`vz${i}`) > 0.85 ? 2.6 + random(`vzz${i}`) * 2 : 0.3 + random(`vz2${i}`) * 1.2;
      return {
        cell,
        glass: glassGeometry(cell.poly),
        decal: decalGeometry(cell.poly, cell.cx, cell.cy),
        dir: [dx / dl, dy / dl],
        vel: [(dx / dl) * speed, (dy / dl) * speed, toCam],
        spin: [(random(`s1${i}`) - 0.5) * 3.6, (random(`s2${i}`) - 0.5) * 3.6, (random(`s3${i}`) - 0.5) * 2],
        jit: [random(`j1${i}`) - 0.5, random(`j2${i}`) - 0.5, random(`j3${i}`) - 0.5],
        delay: Math.min(0.12, cell.dist * 0.035),
      };
    });
  }, []);

const Lights: React.FC<{ t: number }> = ({ t }) => (
  <>
    <ambientLight intensity={0.15} />
    <directionalLight position={[4, 6, 9]} intensity={2.2} />
    <directionalLight position={[-6, -2, 4]} intensity={0.6} color="#c9a8ff" />
    <pointLight position={[-4, 3, 6]} intensity={60} color="#9fd7ff" />
    <pointLight position={[4, -3, 6]} intensity={50} color="#ff9fd8" />
    {/* a light that sweeps across so edges catch glints as they turn */}
    <pointLight position={[-7 + ((t * 2.4) % 14), 2.5, 5]} intensity={90} color="#ffffff" />
  </>
);

const Backdrop: React.FC = () => {
  const tex = useTextureFile("trailer/backdrop.png");
  return (
    <mesh position={[0, 0, -10]}>
      <planeGeometry args={[11, 19.5]} />
      <meshBasicMaterial map={tex} toneMapped={false} />
    </mesh>
  );
};

// ---------------------------------------------------------------- shatter scene

const ShatterScene: React.FC<{ t: number }> = ({ t }) => {
  const screen = useTextureFile("trailer/stock.png");
  const shards = useShards();
  const glass = useMemo(glassMaterials, []);
  const paneGlass = useMemo(() => glassGeometry(roundedRect(PANE_W, PANE_H, PANE_R, 10)), []);
  const paneDecal = useMemo(() => decalGeometry(roundedRect(PANE_W, PANE_H, PANE_R, 10), 0, 0), []);
  const decalMat = useMemo(() => new THREE.MeshBasicMaterial({ map: screen, transparent: true, toneMapped: false }), [screen]);

  // whole pane drifting in, with a jolt on impact
  const yaw = THREE.MathUtils.lerp(-0.34, 0.1, prog(t, 0, TT.crack, easeInOut));
  const jolt = t > TT.crack ? Math.exp(-(t - TT.crack) * 7) * Math.sin((t - TT.crack) * 40) : 0;
  const pitch = 0.06 + jolt * 0.035;
  const intro = prog(t, 0.1, 1.2, easeOut);
  const paneScale = 0.94 + 0.06 * intro;
  // the old screen stays printed on the pieces through the burst, then they turn to clear glass
  decalMat.opacity = 0.95 * (1 - prog(t, TT.converge - 0.25, TT.converge + 0.45));

  const cracked = t >= TT.crack;
  const crackK = prog(t, TT.crack, TT.crack + 0.06, easeOut);
  const ex = Math.max(0, t - TT.explode);
  const slow = 1 - Math.exp(-ex * 1.6); // decelerating slow-motion drift
  const cv = prog(t, TT.converge, TT.icon - 0.05, easeInOut);

  return (
    <group rotation={[pitch, yaw, jolt * 0.02]} scale={paneScale}>
      {!cracked ? (
        <group>
          <mesh geometry={paneGlass} material={glass} />
          <mesh geometry={paneDecal} material={decalMat} position={[0, 0, DECAL_Z]} />
        </group>
      ) : (
        shards.map((s, i) => {
          const c = s.cell;
          // 1) cracked in place: hairline gaps + tiny tilts so every edge catches the light
          const gap = (0.012 + 0.02 * Math.abs(s.jit[0])) * crackK;
          let x = c.cx + s.dir[0] * gap;
          let y = c.cy + s.dir[1] * gap;
          let z = s.jit[2] * 0.04 * crackK;
          let rx = s.jit[0] * 0.05 * crackK;
          let ry = s.jit[1] * 0.05 * crackK;
          let rz = s.jit[2] * 0.03 * crackK;
          // 2) slow-motion burst
          const e = Math.max(0, slow - s.delay) * 1.6;
          x += s.vel[0] * e;
          y += s.vel[1] * e - 0.12 * e * e;
          z += s.vel[2] * e;
          rx += s.spin[0] * e;
          ry += s.spin[1] * e;
          rz += s.spin[2] * e;
          // 3) spiral into the icon at the centre of frame
          let sc = 1;
          if (cv > 0) {
            const ang = cv * 2.2 * (i % 2 ? 1 : -1);
            const k = Math.pow(1 - cv, 1.3);
            const ox = x * Math.cos(ang) - y * Math.sin(ang);
            const oy = x * Math.sin(ang) + y * Math.cos(ang);
            x = ox * k;
            y = oy * k;
            z = z * k + 1.2 * cv;
            sc = 1 - 0.82 * cv;
            rx += cv * 3;
            ry += cv * 2;
          }
          sc *= 1 - prog(t, TT.icon - 0.12, TT.icon, easeOut);
          if (sc <= 0.001) return null;
          return (
            <group key={i} position={[x, y, z]} rotation={[rx, ry, rz]} scale={sc}>
              <mesh geometry={s.glass} material={glass} />
              <mesh geometry={s.decal} material={decalMat} position={[0, 0, DECAL_Z]} />
            </group>
          );
        })
      )}
    </group>
  );
};

// ---------------------------------------------------------------- hero orbit

const OrbitScene: React.FC<{ t: number; centerY: number }> = ({ t, centerY }) => {
  const shards = useShards();
  const glass = useMemo(glassMaterials, []);
  const picks = useMemo(() => shards.filter((s) => s.cell.area > 0.08 && s.cell.area < 0.35).slice(0, 10), [shards]);
  const vis = prog(t, TT.hero + 0.2, TT.hero + 1.3, easeOut);
  return (
    <group position={[0, centerY, 0]}>
      {picks.map((s, i) => {
        const a = (i / picks.length) * Math.PI * 2 + t * (0.22 + (i % 3) * 0.05);
        const rx = 2.3 + (i % 4) * 0.35;
        const rz = 1.6 + (i % 3) * 0.3;
        const sc = (0.55 + random(`os${i}`) * 0.35) * vis;
        return (
          <mesh
            key={i}
            geometry={s.glass}
            material={glass}
            position={[Math.cos(a) * rx, Math.sin(a * 1.3 + i) * 0.5 + (i % 2 ? 0.35 : -0.35), Math.sin(a) * rz - 0.6]}
            rotation={[t * 0.6 + i, t * 0.8 + i * 2, t * 0.3]}
            scale={sc}
          />
        );
      })}
    </group>
  );
};

// ---------------------------------------------------------------- canvases

export const ShatterCanvas: React.FC = () => {
  const { width, height } = useVideoConfig();
  const t = useTime();
  if (t > TT.icon + 0.1) return null;
  return (
    <ThreeCanvas width={width} height={height} camera={{ fov: FOV, position: [0, 0, CAM_Z], near: 0.1, far: 100 }} gl={{ antialias: true }}>
      <Environment />
      <Lights t={t} />
      <Backdrop />
      <ShatterScene t={t} />
    </ThreeCanvas>
  );
};

export const OrbitCanvas: React.FC<{ iconFrameY: number }> = ({ iconFrameY }) => {
  const { width, height } = useVideoConfig();
  const t = useTime();
  if (t < TT.hero - 0.05) return null;
  return (
    <ThreeCanvas width={width} height={height} camera={{ fov: FOV, position: [0, 0, CAM_Z], near: 0.1, far: 100 }} gl={{ antialias: true, alpha: true }}>
      <Environment />
      <Lights t={t} />
      <OrbitScene t={t} centerY={(960 - iconFrameY) / PX_PER_UNIT} />
    </ThreeCanvas>
  );
};

/** Where the impact point lands in frame pixels (for the HTML flash). */
export const IMPACT_FRAME = { x: 540 + IMPACT[0] * PX_PER_UNIT, y: 960 - IMPACT[1] * PX_PER_UNIT };
