using System.IO;

namespace SubnauticaMP.Shared
{
    // Suit colors to pick from in the party lobby (0xRRGGBB).
    public static class DiverColors
    {
        public const int Default = 0xFFFFFF; // the game's normal suit
        public static readonly int[] All = { 0xFFFFFF, 0xFF8A3D, 0xFFD23D, 0x5BD68B, 0x3DC8FF, 0x5B7BFF, 0xB05BFF, 0xFF5BA8, 0xFF4D4D, 0x3D3D3D };
        public static readonly string[] Names = { "Standard", "Orange", "Yellow", "Green", "Cyan", "Blue", "Purple", "Pink", "Red", "Black" };

        public static string NameOf(int color)
        {
            for (int i = 0; i < All.Length; i++) if (All[i] == color) return Names[i];
            return "Custom";
        }

        public static int Next(int color)
        {
            for (int i = 0; i < All.Length; i++) if (All[i] == color) return All[(i + 1) % All.Length];
            return All[0];
        }

        public static string Hex(int color) => (color & 0xFFFFFF).ToString("X6");
    }

    // client -> server: my new name / color. server -> all: someone's name / color now.
    public sealed class PlayerProfilePacket : Packet
    {
        public int Id;
        public string Name;
        public int Color = DiverColors.Default;
        public override PacketType Type => PacketType.PlayerProfile;
        public override void Write(BinaryWriter w) { w.Write(Id); w.Write(Name ?? ""); w.Write(Color); }
        public override void Read(BinaryReader r) { Id = r.ReadInt32(); Name = r.ReadString(); Color = r.ReadInt32(); }
    }
}
