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
        w.FindControl<TextBox>("WorldBox").Text = "UiTest-" + Guid.NewGuid().ToString("N");
        w.FindControl<Button>("ServerButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.True(w.FindControl<Border>("CodesCard").IsVisible);
        Assert.Equal("STOP SERVER", w.FindControl<Button>("ServerButton").Content);
        Snap(w, "4-server-running");

        w.FindControl<Button>("ServerButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("START SERVER", w.FindControl<Button>("ServerButton").Content);
        w.Close();
    }

    [AvaloniaFact]
    public void JoinWithoutGameFolderExplainsWhy()
    {
        var w = new MainWindow();
        w.Show();
        w.FindControl<TextBox>("GameDirBox").Text = "/nope";
        w.FindControl<TextBox>("JoinBox").Text = "KQ7MX-3HD2P";
        w.FindControl<Button>("JoinButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("Subnautica.exe", w.FindControl<TextBlock>("StatusText").Text);
        Assert.Equal(2, w.FindControl<TabControl>("Tabs").SelectedIndex);
        w.Close();
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
