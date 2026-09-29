using SubnauticaMP.Shared;
using Xunit;

namespace SubnauticaMP.Launcher.Tests;

public class GameFolderTests
{
    [Fact]
    public void InstallsModAndLaunchFileIntoGameFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fake-subnautica-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "core"));
            File.WriteAllText(Path.Combine(dir, "Subnautica.exe"), "");
            File.WriteAllText(Path.Combine(dir, "winhttp.dll"), "");
            File.WriteAllText(Path.Combine(dir, "BepInEx", "core", "BepInEx.dll"), "");

            Assert.True(GameFolder.IsGameDir(dir));
            Assert.True(GameFolder.HasBepInEx(dir));
            Assert.Null(GameFolder.InstalledModVersion(dir));

            GameFolder.InstallMod(dir);
            GameFolder.WriteLaunchInfo(dir, "Mark", "86.12.200.7", 11000);

            // the packed DLL is the real mod
            var installed = GameFolder.InstalledModVersion(dir);
            Assert.Equal(GameFolder.BundledModVersion, installed);
            var name = System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(GameFolder.PluginDir(dir), "SubnauticaMP.dll")).Name;
            Assert.Equal("SubnauticaMP", name);
            Assert.True(File.Exists(Path.Combine(GameFolder.PluginDir(dir), "Mono.Nat.dll")));

            var info = LaunchInfo.TryLoad(Path.Combine(GameFolder.PluginDir(dir), LaunchInfo.FileName));
            Assert.Equal("86.12.200.7", info.Host);
            Assert.Equal("Mark", info.PlayerName);
        }
        finally { Directory.Delete(dir, true); }
    }
}
