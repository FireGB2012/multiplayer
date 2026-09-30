using Nautilus.Json;
using Nautilus.Options.Attributes;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Our settings in the game's Options > Mods tab (only when Nautilus is installed; this class is never
    // touched otherwise). Values are copied to/from the mod's normal BepInEx config.
    [Menu("Subnautica Multiplayer")]
    public sealed class NautilusOptionsPage : ConfigFile
    {
        [Keybind("Multiplayer menu key", Tooltip = "Opens the multiplayer page (players, chat, join code).")]
        public KeyCode MenuKey = KeyCode.F8;

        [Choice("Suit color", "Standard", "Orange", "Yellow", "Green", "Cyan", "Blue", "Purple", "Pink", "Red", "Black",
            Tooltip = "The color other players see your diver in.")]
        public int SuitColor;

        [Toggle("Enter opens chat", Tooltip = "Press Enter in game to jump to the chat box.")]
        public bool EnterForChat = true;

        static NautilusOptionsPage _page;

        internal static void Register()
        {
            _page = Nautilus.Handlers.OptionsPanelHandler.RegisterModOptions<NautilusOptionsPage>();
            if (_page == null) return;
            // start from the BepInEx config values
            _page.MenuKey = Plugin.MenuKey.Value;
            _page.SuitColor = System.Array.IndexOf(DiverColors.All, Plugin.DiverColor.Value) is int i && i >= 0 ? i : 0;
            _page.EnterForChat = Plugin.EnterForChat.Value;
            Plugin.Log.LogInfo("Settings added to Options > Mods (Nautilus)");
        }

        // Called now and then: picks up changes made in the options menu.
        internal static void Sync()
        {
            if (_page == null) return;
            if (_page.MenuKey != Plugin.MenuKey.Value) Plugin.MenuKey.Value = _page.MenuKey;
            if (_page.EnterForChat != Plugin.EnterForChat.Value) Plugin.EnterForChat.Value = _page.EnterForChat;
            if (_page.SuitColor >= 0 && _page.SuitColor < DiverColors.All.Length && DiverColors.All[_page.SuitColor] != Plugin.DiverColor.Value)
                Session.Instance?.SendProfile(Plugin.PlayerName.Value, DiverColors.All[_page.SuitColor]);
        }

        internal static bool Active => _page != null;
    }
}
