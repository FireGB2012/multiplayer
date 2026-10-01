using System.Linq;
using Avalonia.LogicalTree;
using Avalonia;
using Xunit;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using SubnauticaMP.Launcher;

[assembly: AvaloniaTestApplication(typeof(SubnauticaMP.Launcher.Tests.TestApp))]

namespace SubnauticaMP.Launcher.Tests;

public class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class LauncherUiTests
{
    // Set SNMP_SCREENSHOTS=<dir> to also save what each tab looks like.
    static void Snap(Window w, string name)
    {
        var dir = Environment.GetEnvironmentVariable("SNMP_SCREENSHOTS");
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (!string.IsNullOrEmpty(dir)) frame.Save(Path.Combine(dir, name + ".png"));
    }

    [AvaloniaFact]
    public void AllTabsRender()
    {
        var w = new MainWindow();
        w.Show();
        var tabs = w.FindControl<TabControl>("Tabs");
        Snap(w, "1-play");
        tabs.SelectedIndex = 1;
        Snap(w, "2-server-stopped");
        tabs.SelectedIndex = 2;
        Snap(w, "3-setup");
        w.Close();
    }

    [AvaloniaFact]
    public void CreatingAServerShowsJoinCodesThenListsIt()
    {
        var name = "Test World " + Guid.NewGuid().ToString("N").Substring(0, 6);
        var w = new MainWindow();
        w.Show();
        void Click(string button)
        {
            w.FindControl<Button>(button).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        try
        {
            w.FindControl<TabControl>("Tabs").SelectedIndex = 1;
            Assert.True(w.FindControl<Grid>("ServerListView").IsVisible);

            Click("NewServerButton");
            Assert.True(w.FindControl<Grid>("ServerCreateView").IsVisible);
            w.FindControl<TextBox>("PortBox").Text = FreePort().ToString();
            w.FindControl<TextBox>("WorldBox").Text = name;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            w.FindControl<ComboBox>("ModeBox").SelectedItem = "Creative";
            Click("CreateServerButton");

            Assert.True(w.FindControl<Grid>("ServerRunView").IsVisible); // the open server: codes, players, log
            Assert.Contains("Lobby", w.FindControl<TextBlock>("LobbyText").Text);
            Assert.Equal(name, w.FindControl<TextBlock>("RunTitle").Text);
            Snap(w, "4-server-running");

            Click("ServerButton"); // stop: back to the list, which now has it
            Assert.True(w.FindControl<Grid>("ServerListView").IsVisible);
            Assert.Contains(w.FindControl<ItemsControl>("ServerItems").Items.OfType<Border>(),
                b => b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == name));
        }
        finally
        {
            w.Close();
            DeleteIfThere(HostedServer.WorldPath(name));
        }
    }

    [AvaloniaFact]
    public void PlayButtonTurnsIntoFindGameWithoutAGameFolder()
    {
        var w = new MainWindow();
        w.Show();
        var label = w.FindControl<TextBlock>("PlayButtonText");
        w.FindControl<TextBox>("GameDirBox").Text = "/nope";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("FIND GAME", label.Text); // one main button: it searches instead of failing
        Assert.Contains("isn't found", w.FindControl<TextBlock>("PlayGameStatus").Text);
        Assert.Contains("Searches", w.FindControl<TextBlock>("PlayCaption").Text);

        var dir = Path.Combine(Path.GetTempPath(), "snmp-fakegame-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Subnautica.exe"), "");
            w.FindControl<TextBox>("GameDirBox").Text = dir;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("PLAY", label.Text);
            Assert.Contains("Game found", w.FindControl<TextBlock>("GameChipText").Text);
        }
        finally { w.Close(); Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void ExistingWorldKeepsItsGameMode()
    {
        var name = "ModeTest-" + Guid.NewGuid().ToString("N");
        var saved = new SubnauticaMP.Shared.WorldState { GameMode = SubnauticaMP.Shared.GameModes.Creative };
        saved.SaveToFile(HostedServer.WorldPath(name));
        try
        {
            var w = new MainWindow();
            w.Show();
            var mode = w.FindControl<ComboBox>("ModeBox");
            w.FindControl<TextBox>("WorldBox").Text = "Brand new " + Guid.NewGuid().ToString("N");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(mode.IsEnabled);
            mode.SelectedItem = "Hardcore";
            Assert.Contains("one life", w.FindControl<TextBlock>("ModeHint").Text);

            w.FindControl<TextBox>("WorldBox").Text = name;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // TextChanged fires on the next UI tick
            Assert.False(mode.IsEnabled);
            Assert.Equal("Creative", mode.SelectedItem);
            Assert.Contains("already exists", w.FindControl<TextBlock>("ModeHint").Text);
            w.Close();
        }
        finally { File.Delete(HostedServer.WorldPath(name)); }
    }

    static void DeleteIfThere(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
