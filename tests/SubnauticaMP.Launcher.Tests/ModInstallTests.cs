using System;
using System.IO;
using System.IO.Compression;
using SubnauticaMP.Launcher;
using Xunit;

public class ModInstallTests
{
    static string FakeGame()
    {
        var dir = Path.Combine(Path.GetTempPath(), "snmp-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Subnautica.exe"), "");
        return dir;
    }

    static byte[] Zip(params string[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create))
            foreach (var e in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(e).Open());
                w.Write("x");
            }
        return ms.ToArray();
    }

    [Fact]
    public void InstallingTheModAlsoInstallsTheOptimizer()
    {
        var dir = FakeGame();
        try
        {
            GameFolder.InstallMod(dir);
            Assert.True(File.Exists(Path.Combine(GameFolder.PluginDir(dir), "SubnauticaMP.dll")));
            Assert.True(GameFolder.HasOptimizer(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("plugins/Nautilus/Nautilus.dll")]          // how Nautilus ships it
    [InlineData("BepInEx/plugins/Nautilus/Nautilus.dll")]  // in case it ever ships with the BepInEx folder
    public void NautilusZipLandsInBepInExPlugins(string entry)
    {
        var dir = FakeGame();
        try
        {
            Assert.False(GameFolder.HasNautilus(dir));
            GameFolder.ExtractNautilus(Zip(entry, "README.md", "../evil.dll"), dir);
            Assert.True(GameFolder.HasNautilus(dir));
            Assert.False(File.Exists(Path.Combine(dir, "README.md")));          // only the plugin files
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(dir), "evil.dll")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ZipWithoutNautilusIsRejected()
    {
        var dir = FakeGame();
        try { Assert.Throws<InvalidDataException>(() => GameFolder.ExtractNautilus(Zip("something/else.txt"), dir)); }
        finally { Directory.Delete(dir, true); }
    }
}
