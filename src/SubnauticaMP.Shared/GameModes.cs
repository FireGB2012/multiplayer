using System;

namespace SubnauticaMP.Shared
{
    public static class GameModes
    {
        public const string Survival = "Survival";
        public const string Freedom = "Freedom";
        public const string Hardcore = "Hardcore";
        public const string Creative = "Creative";

        public static readonly string[] All = { Survival, Hardcore, Creative, Freedom };

        public static string Describe(string mode)
        {
            switch (mode)
            {
                case Hardcore: return "Survival, but you only get one life. Die and your save is gone.";
                case Creative: return "No hunger, no damage, everything unlocked and free to build.";
                case Freedom: return "No hunger or thirst. Still need oxygen and blueprints.";
                default: return "The normal game: food, water, oxygen, health.";
            }
        }

        public static string Normalize(string mode)
        {
            foreach (var m in All)
                if (string.Equals(m, mode, StringComparison.OrdinalIgnoreCase)) return m;
            return Survival;
        }

        // The game's GameModeOption flag values for each preset.
        public static int OptionValue(string mode)
        {
            switch (Normalize(mode))
            {
                case Freedom: return 2;
                case Hardcore: return 257;
                case Creative: return 1790;
                default: return 0;
            }
        }
    }
}
