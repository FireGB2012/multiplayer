using System;
using System.Collections.Generic;
using System.IO;

namespace SubnauticaMP.Shared
{
    // The Titanium Bat (a craftable tool, made in the plugin with Nautilus). Hitting another player launches
    // them far the way you're looking: look up and they go up. No damage.
    public static class Bat
    {
        public const string TechName = "TitaniumBat";
        public const float LaunchedSeconds = 5f;   // flying + lying there + getting up
        public const float Reach = 3.4f;           // meters from your eyes
        public const float Cone = 55f;             // degrees off where you look
        public const int MaxTargets = 4;           // one swing can hit a few people standing together
        public const double SwingCooldownMs = 450; // server: hits closer together than this are dropped

        // How fast (m/s, world) a hit launches someone, from where the hitter looked (any length).
        // underwater: straight along the look, very fast, and the water slows them down (see FlightSeconds)
        // on land / in air: always a bit upward (an arc), more the higher you look; indoors softer so they don't
        //   go through walls.
        public static Vec3 LaunchVelocity(Vec3 look, bool underwater, bool indoors)
        {
            double len = Math.Sqrt(look.X * look.X + look.Y * look.Y + look.Z * look.Z);
            if (len < 1e-4 || double.IsNaN(len)) look = new Vec3(0, 0, 1);
            else look = new Vec3((float)(look.X / len), (float)(look.Y / len), (float)(look.Z / len));
            double pitch = Math.Asin(Math.Max(-1, Math.Min(1, look.Y))) * 180 / Math.PI;
            double hx = look.X, hz = look.Z, hl = Math.Sqrt(hx * hx + hz * hz);
            if (hl < 1e-3) { hx = 0; hz = 1; } else { hx /= hl; hz /= hl; } // straight up / down: any way is fine

            double elevation, speed;
            if (underwater)
            {
                elevation = Clamp(pitch, -35, 85);
                speed = 21;
            }
            else
            {
                elevation = Clamp(pitch + 15, 22, 82);
                speed = 15;
            }
            if (indoors) speed *= 0.45;
            if (pitch > 40) speed *= 1.1; // home run

            double e = elevation * Math.PI / 180;
            double c = Math.Cos(e), s = Math.Sin(e);
            return new Vec3((float)(hx * c * speed), (float)(s * speed), (float)(hz * c * speed));
        }

        public static bool HomeRun(Vec3 look)
        {
            double len = Math.Sqrt(look.X * look.X + look.Y * look.Y + look.Z * look.Z);
            return len > 1e-4 && look.Y / len > Math.Sin(40 * Math.PI / 180);
        }

        // How long the launched ragdoll keeps its speed (low water drag) before slowing down normally.
        public static float FlightSeconds(bool underwater) => underwater ? 1.3f : 0f;

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }

    // Animation timing curves (same as CSS cubic-bezier), for the bat's swing.
    public static class Ease
    {
        public static float OutQuint(float t) => Bezier(0.23f, 1f, 0.32f, 1f, t);
        public static float InOutCubic(float t) => Bezier(0.645f, 0.045f, 0.355f, 1f, t);
        public static float OutCubic(float t) => Bezier(0.33f, 1f, 0.68f, 1f, t);
        public static float InCubic(float t) => Bezier(0.32f, 0f, 0.67f, 0f, t);
        public static float OutBack(float t) => Bezier(0.34f, 1.56f, 0.64f, 1f, t);
        public static float Linear(float t) => Clamp01(t);

        // y at x = t on the curve (0,0) (x1,y1) (x2,y2) (1,1)
        public static float Bezier(float x1, float y1, float x2, float y2, float t)
        {
            t = Clamp01(t);
            if (t <= 0f || t >= 1f) return t;
            // solve x(u) = t: Newton first, bisection if it doesn't settle
            double u = t;
            for (int i = 0; i < 8; i++)
            {
                double x = Cubic(x1, x2, u) - t;
                if (Math.Abs(x) < 1e-6) return (float)Cubic(y1, y2, u);
                double d = Slope(x1, x2, u);
                if (Math.Abs(d) < 1e-6) break;
                u -= x / d;
            }
            double lo = 0, hi = 1;
            u = t;
            for (int i = 0; i < 30; i++)
            {
                double x = Cubic(x1, x2, u);
                if (Math.Abs(x - t) < 1e-6) break;
                if (x < t) lo = u; else hi = u;
                u = (lo + hi) / 2;
            }
            return (float)Cubic(y1, y2, u);
        }

        static double Cubic(double p1, double p2, double u) => ((1 - 3 * p2 + 3 * p1) * u + (3 * p2 - 6 * p1)) * u * u + 3 * p1 * u;
        static double Slope(double p1, double p2, double u) => 3 * (1 - 3 * p2 + 3 * p1) * u * u + 2 * (3 * p2 - 6 * p1) * u + 3 * p1;
        static float Clamp01(float t) => t < 0f ? 0f : t > 1f ? 1f : t;
    }

    // client -> server: I'm swinging (Pitch = how far up I'm looking, degrees), server -> everyone else: player Id
    // is swinging. Just so everyone sees the swing.
    public sealed class BatSwingPacket : Packet
    {
        public int Id;
        public float Pitch;
        public override PacketType Type => PacketType.BatSwing;
        public override void Write(BinaryWriter w) { w.Write(Id); w.Write(Pitch); }
        public override void Read(BinaryReader r) { Id = r.ReadInt32(); Pitch = r.ReadSingle(); }
    }

    // client -> server: my swing hit these players, I was looking this way.
    // server -> everyone: HitterId hit them (each target's own game launches them, see Bat.LaunchVelocity).
    public sealed class BatHitPacket : Packet
    {
        public int HitterId;
        public List<int> Targets = new List<int>();
        public Vec3 Direction;
        public override PacketType Type => PacketType.BatHit;
        public override void Write(BinaryWriter w)
        {
            w.Write(HitterId);
            w.Write((byte)Math.Min(Targets.Count, 255));
            for (int i = 0; i < Targets.Count && i < 255; i++) w.Write(Targets[i]);
            Direction.Write(w);
        }
        public override void Read(BinaryReader r)
        {
            HitterId = r.ReadInt32();
            int n = r.ReadByte();
            Targets = new List<int>(n);
            for (int i = 0; i < n; i++) Targets.Add(r.ReadInt32());
            Direction = Vec3.Read(r);
        }
    }
}

namespace SubnauticaMP.Shared
{
    // The bat's swing, as poses over time. Everything is in "look space": x = right, y = up, z = where you look
    // (Unity's axes). The plugin turns each pose into arm bone rotations (two-bone IK, BatPose.cs).
    //   Hand:  which way the right hand is from the right shoulder, Reach = how far (1 = arm straight)
    //   Bat:   which way the bat points out of the fist
    //   Pole:  which way the right elbow bends
    //   Twist: chest turn, degrees (+ = to the right)
    //   Left:  how much the left hand joins on the handle (0 = hanging, 1 = two-handed)
    //   Ease:  the curve used to get INTO this pose from the one before
    // Timing follows the animation rules: snappy, user-started, eases out of moves into rests, ease-in when
    // speeding up into the hit, and the way back is shorter than the way out.
    public static class BatSwing
    {
        public enum Curve : byte { Linear, OutCubic, InCubic, InOutCubic, OutQuint, OutBack }

        public struct Key
        {
            public float T, Reach, Twist, Left;
            public Vec3 Hand, Bat, Pole;
            public Curve Ease;
            public Key(float t, Vec3 hand, float reach, Vec3 bat, Vec3 pole, float twist, float left, Curve ease)
            { T = t; Hand = hand; Reach = reach; Bat = bat; Pole = pole; Twist = twist; Left = left; Ease = ease; }
        }

        // bat resting on your right side, pointing up and forward (Roblox-tool style: in view, bottom right)
        public static readonly Key Idle = new Key(0f, new Vec3(0.25f, -0.2f, 1f), 0.8f, new Vec3(0.22f, 0.72f, 0.66f), new Vec3(0.8f, -0.6f, 0f), 0f, 0f, Curve.Linear);

        public const float StrikeStart = 0.20f; // the bat starts coming round (whoosh)
        public const float Contact = 0.29f;     // the moment it hits
        public const float Seconds = 0.78f;     // back at rest

        public static readonly Key[] Keys =
        {
            Idle,
            // wind up: hands come up by the right shoulder, bat cocked back behind you
            new Key(0.16f, new Vec3(0.5f, 0.3f, 0.8f), 0.7f, new Vec3(0.3f, 0.85f, -0.42f), new Vec3(0.9f, 0.3f, -0.3f), 25f, 1f, Curve.OutCubic),
            // tiny hold: anticipation
            new Key(StrikeStart, new Vec3(0.5f, 0.33f, 0.78f), 0.7f, new Vec3(0.27f, 0.88f, -0.4f), new Vec3(0.9f, 0.3f, -0.3f), 27f, 1f, Curve.InOutCubic),
            // coming round: speeding up, the barrel trails the hands
            new Key(0.255f, new Vec3(0.35f, -0.05f, 0.95f), 0.88f, new Vec3(0.75f, 0.2f, 0.6f), new Vec3(0.6f, -0.6f, -0.2f), 10f, 1f, Curve.InCubic),
            // contact: arms long, bat straight out in front
            new Key(Contact, new Vec3(0f, -0.2f, 1f), 0.95f, new Vec3(-0.3f, 0.12f, 0.95f), new Vec3(0.4f, -0.9f, 0f), -5f, 1f, Curve.Linear),
            // follow-through: wraps round to the left, slowing down
            new Key(0.40f, new Vec3(-0.6f, 0f, 0.8f), 0.85f, new Vec3(-0.85f, 0.25f, -0.45f), new Vec3(-0.2f, -0.9f, 0.2f), -30f, 1f, Curve.OutQuint),
            // settle
            new Key(0.48f, new Vec3(-0.45f, -0.1f, 0.85f), 0.8f, new Vec3(-0.7f, 0.5f, -0.5f), new Vec3(0f, -1f, 0f), -22f, 0.8f, Curve.InOutCubic),
            // back to rest (left hand lets go on the way)
            new Key(Seconds, Idle.Hand, Idle.Reach, Idle.Bat, Idle.Pole, 0f, 0f, Curve.InOutCubic),
        };

        public static float Apply(Curve c, float t)
        {
            switch (c)
            {
                case Curve.OutCubic: return Ease.OutCubic(t);
                case Curve.InCubic: return Ease.InCubic(t);
                case Curve.InOutCubic: return Ease.InOutCubic(t);
                case Curve.OutQuint: return Ease.OutQuint(t);
                case Curve.OutBack: return Ease.OutBack(t);
                default: return Ease.Linear(t);
            }
        }

        // The two poses around `time` and how far between them (already eased).
        public static (Key from, Key to, float f) At(float time)
        {
            if (time <= 0f) return (Keys[0], Keys[0], 0f);
            for (int i = 1; i < Keys.Length; i++)
            {
                if (time > Keys[i].T) continue;
                var a = Keys[i - 1];
                var b = Keys[i];
                float raw = (time - a.T) / Math.Max(1e-4f, b.T - a.T);
                return (a, b, Apply(b.Ease, raw));
            }
            var last = Keys[Keys.Length - 1];
            return (last, last, 0f);
        }
    }
}
