using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SubnauticaMP
{
    // Puts a MULTIPLAYER button in Subnautica's main menu by copying the game's own Play button
    // (same trick Nitrox uses), and hides multiplayer saves from the single-player Load list.
    internal static class MainMenuButton
    {
        const string ButtonPath = "Panel/MainMenu/PrimaryOptions/MenuButtons/ButtonPlay";
        static GameObject _button;
        static Component _menuSeen;
        static bool _failed;
        static float _nextHide;

        public static bool Injected => _button != null;

        public static void Update(Action onClick)
        {
            var menu = Game.MainMenu;
            if (menu == null) return;
            if (menu != _menuSeen) { _menuSeen = menu; _failed = false; } // new menu scene, try again
            if (_button == null && !_failed)
            {
                try { Inject(onClick); }
                catch (Exception e)
                {
                    _failed = true;
                    Plugin.Log.LogWarning("Couldn't add the menu button, using the corner button instead: " + e.GetBaseException().Message);
                }
            }

            if (Time.unscaledTime >= _nextHide)
            {
                _nextHide = Time.unscaledTime + 1f;
                try { HideMultiplayerSaves(); } catch { }
            }
        }

        static void Inject(Action onClick)
        {
            var canvas = GameObject.Find("Menu canvas");
            var play = canvas != null ? canvas.transform.Find(ButtonPath) : null;
            if (play == null) throw new Exception("Play button not found");

            var copy = UnityEngine.Object.Instantiate(play.gameObject, play.parent);
            copy.name = "ButtonMultiplayer";
            copy.transform.SetSiblingIndex(play.GetSiblingIndex() + 1);

            // label: the game keeps re-translating it, so remove that and set our own text
            foreach (var c in copy.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var type = c.GetType();
                if (type.Name == "TranslationLiveUpdate") UnityEngine.Object.Destroy(c);
                else if (type.Name.StartsWith("TextMeshPro")) type.GetProperty("text")?.SetValue(c, "Multiplayer", null);
            }

            // click: swap the Play action for ours
            var button = Type.GetType("UnityEngine.UI.Button, UnityEngine.UI");
            var comp = button != null ? copy.GetComponent(button) : null;
            var prop = button?.GetProperty("onClick");
            if (comp == null || prop == null) throw new Exception("Button component not found");
            var evt = (UnityEvent)Activator.CreateInstance(prop.PropertyType);
            evt.AddListener(() => onClick());
            prop.SetValue(comp, evt, null);

            _button = copy;
            Plugin.Log.LogInfo("Added Multiplayer button to the main menu");
        }

        // Multiplayer worlds are listed in our menu, not mixed into the single-player saves.
        static void HideMultiplayerSaves()
        {
            if (Game.MainMenuLoadButton == null) return;
            var slots = WorldSlots.AllSlots();
            if (slots.Count == 0) return;
            foreach (var b in Resources.FindObjectsOfTypeAll(Game.MainMenuLoadButton))
            {
                var c = b as Component;
                if (c == null || !c.gameObject.activeSelf) continue;
                if (Game.TryGet(Game.MainMenuLoadButton, c, "saveGame") is string slot && slots.Contains(slot))
                    c.gameObject.SetActive(false);
            }
        }
    }
}
