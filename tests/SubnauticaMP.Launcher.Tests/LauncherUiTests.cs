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
    public void HostingShowsJoinCodes()
    {
        var w = new MainWindow();
        w.Show();
        w.FindControl<TabControl>("Tabs").SelectedIndex = 1;
        w.FindControl<TextBox>("PortBox").Text = FreePort().ToString();
        w.FindControl<TextBox>("WorldBox").Text = "My World";
        DeleteIfThere(HostedServer.WorldPath("My World"));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        w.FindControl<ComboBox>("ModeBox").SelectedItem = "Creative";
        w.FindControl<Button>("ServerButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.True(w.FindControl<Border>("CodesCard").IsVisible);
        Assert.Contains("Lobby", w.FindControl<TextBlock>("LobbyText").Text);
        Assert.Equal("STOP SERVER", w.FindControl<Button>("ServerButton").Content);
        Snap(w, "4-server-running");

        w.FindControl<Button>("ServerButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("START SERVER", w.FindControl<Button>("ServerButton").Content);
        w.Close();
        DeleteIfThere(HostedServer.WorldPath("My World"));
    }

    [AvaloniaFact]
    public void JoinWithoutGameFolderExplainsWhy()
    {
        var w = new MainWindow();
        w.Show();
        w.FindControl<TextBox>("GameDirBox").Text = "/nope";
        w.FindControl<TextBox>("JoinBox").Text = "KQ7MX-3HD2P";
        w.FindControl<Button>("JoinButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("Search my PC", w.FindControl<TextBlock>("StatusText").Text);
        Assert.Contains("not found", w.FindControl<TextBlock>("PlayGameStatus").Text);
        Assert.True(w.FindControl<Button>("PlaySearchButton").IsVisible);
        w.Close();
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
