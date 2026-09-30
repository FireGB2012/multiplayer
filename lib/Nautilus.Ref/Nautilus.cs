using System;

namespace Nautilus.Json
{
    public abstract class ConfigFile
    {
        public ConfigFile() => throw new NotImplementedException();
        public void Load(bool createFileIfNotExist = true) => throw new NotImplementedException();
        public void Save() => throw new NotImplementedException();
    }
}

namespace Nautilus.Options.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class MenuAttribute : Attribute
    {
        public MenuAttribute(string name) => throw new NotImplementedException();
    }

    public abstract class ModOptionAttribute : Attribute
    {
        public string Tooltip { get; set; }
        public int Order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class KeybindAttribute : ModOptionAttribute
    {
        public KeybindAttribute(string label = null) => throw new NotImplementedException();
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class ChoiceAttribute : ModOptionAttribute
    {
        public ChoiceAttribute(string label = null, params string[] options) => throw new NotImplementedException();
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class ToggleAttribute : ModOptionAttribute
    {
        public ToggleAttribute(string label = null) => throw new NotImplementedException();
    }
}

namespace Nautilus.Handlers
{
    public static class OptionsPanelHandler
    {
        public static T RegisterModOptions<T>() where T : Nautilus.Json.ConfigFile, new() => throw new NotImplementedException();
    }
}
