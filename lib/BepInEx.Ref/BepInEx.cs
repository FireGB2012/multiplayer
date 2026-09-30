using System;
using UnityEngine;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class BepInPlugin : Attribute
    {
        public BepInPlugin(string GUID, string Name, string Version) => throw new NotImplementedException();
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInDependency : Attribute
    {
        [Flags]
        public enum DependencyFlags { HardDependency = 1, SoftDependency = 2 }
        public BepInDependency(string DependencyGUID, DependencyFlags Flags = DependencyFlags.HardDependency) => throw new NotImplementedException();
    }

    public abstract class BaseUnityPlugin : MonoBehaviour
    {
        protected Logging.ManualLogSource Logger => throw new NotImplementedException();
        public Configuration.ConfigFile Config => throw new NotImplementedException();
    }

}

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public void LogError(object data) => throw new NotImplementedException();
        public void LogWarning(object data) => throw new NotImplementedException();
        public void LogInfo(object data) => throw new NotImplementedException();
    }
}

namespace BepInEx.Configuration
{
    public class ConfigFile
    {
        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description) => throw new NotImplementedException();
    }

    public abstract class ConfigEntryBase { }

    public sealed class ConfigEntry<T> : ConfigEntryBase
    {
        public T Value
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }
    }
}
