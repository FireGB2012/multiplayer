using System.Threading;
using Xunit;

namespace SubnauticaMP.Launcher.Tests;

public class GameFinderTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "finder-" + Guid.NewGuid().ToString("N"));
    readonly string _game;

    public GameFinderTests()
    {
        // D:\Games\Stuff\SteamLibrary\steamapps\common\Subnautica style tree, plus some junk
        _game = Path.Combine(_root, "Games", "Stuff", "SteamLibrary", "steamapps", "common", "Subnautica");
        Directory.CreateDirectory(Path.Combine(_game, "Subnautica_Data"));
        File.WriteAllText(Path.Combine(_game, "Subnautica.exe"), "");
        Directory.CreateDirectory(Path.Combine(_root, "Games", "OtherGame", "Data"));
        Directory.CreateDirectory(Path.Combine(_root, "Windows", "Subnautica")); // skipped folder
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void PickingTheExeOrAnyFolderAboveItWorks()
    {
        Assert.Equal(_game, GameFinder.Normalize(Path.Combine(_game, "Subnautica.exe")));
        Assert.Equal(_game, GameFinder.Normalize(_game));
        Assert.Equal(_game, GameFinder.Normalize("\"" + _game + "\""));
        Assert.Equal(_game, GameFinder.Normalize(Path.Combine(_root, "Games", "Stuff", "SteamLibrary")));
        Assert.Null(GameFinder.Normalize(Path.Combine(_root, "Games", "OtherGame")));
    }

    [Fact]
    public void SearchFindsItDeepDown()
    {
        Assert.Equal(_game, GameFinder.SearchUnder(new[] { _root }, 7, CancellationToken.None, null));
        Assert.Null(GameFinder.SearchUnder(new[] { _root }, 3, CancellationToken.None, null)); // too shallow
    }

    [Fact]
    public void SpotsLockedStoreInstalls()
    {
        Assert.True(GameFinder.IsLockedStoreInstall(@"C:\Program Files\WindowsApps\UnknownWorlds.Subnautica_1.0\Subnautica"));
        Assert.False(GameFinder.IsLockedStoreInstall(@"C:\XboxGames\Subnautica\Content"));
    }
}
