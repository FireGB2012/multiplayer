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

    // Steam copies get steam_appid.txt so the game doesn't hand itself to Steam (which could start an unmodded copy).
    [Fact]
    public void SteamCopiesGetTheAppIdFileOthersDont()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fake-subnautica-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Subnautica_Data", "Plugins", "x86_64"));
            Assert.False(GameFolder.IsSteamCopy(dir)); // Epic copy: no Steam DLL
            File.WriteAllText(Path.Combine(dir, "Subnautica_Data", "Plugins", "x86_64", "steam_api64.dll"), "");
            Assert.True(GameFolder.IsSteamCopy(dir));
            GameFolder.WriteSteamAppId(dir);
            Assert.Equal("264710", File.ReadAllText(Path.Combine(dir, "steam_appid.txt")).Trim());
        }
        finally { Directory.Delete(dir, true); }
    }
}
