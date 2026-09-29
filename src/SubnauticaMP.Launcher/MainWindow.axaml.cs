using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Launcher
{
    public partial class MainWindow : Window
    {
        readonly Settings _settings = Settings.Load();
        readonly HostedServer _host = new HostedServer();

        public MainWindow()
        {
            InitializeComponent();

            VersionText.Text = "v" + GameFolder.BundledModVersion.ToString(3);
            NameBox.Text = _settings.PlayerName;
            JoinBox.Text = _settings.LastJoin;
            WorldBox.Text = _settings.WorldName;
            PortBox.Text = _settings.Port.ToString();
            GameDirBox.Text = GameFolder.IsGameDir(_settings.GameDir) ? _settings.GameDir : GameFolder.Detect() ?? _settings.GameDir;

            JoinButton.Click += (_, _) => Run(JoinAndPlay);
            HostPlayButton.Click += (_, _) => Run(HostAndPlay);
            ServerButton.Click += (_, _) => Run(ToggleServer);
            BrowseButton.Click += (_, _) => Run(Browse);
            InstallBepButton.Click += (_, _) => Run(InstallBepInEx);
            InstallModButton.Click += (_, _) => Run(InstallMod);
            OpenFolderButton.Click += (_, _) => Run(OpenModFolder);
            CopyInternetButton.Click += (_, _) => Run(() => Copy(_host.InternetCode));
            CopyLanButton.Click += (_, _) => Run(() => Copy(_host.LanCode));
            GameDirBox.TextChanged += (_, _) => RefreshSetup();

            _host.Log += line => Dispatcher.UIThread.Post(() => AppendLog(line));
            _host.Changed += () => Dispatcher.UIThread.Post(RefreshServer);

            RefreshSetup();
            RefreshServer();
            Status(GameFolder.IsGameDir(GameDir)
                ? "Ready. Pick Join or Host."
                : "Couldn't find Subnautica. Set the folder in the Setup tab.");
        }

        string GameDir => (GameDirBox.Text ?? "").Trim().Trim('"');
        string PlayerName => Protocol.CleanName(NameBox.Text);

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            SaveSettings();
            _host.Stop(); // saves the world
            base.OnClosing(e);
        }

        void SaveSettings()
        {
            _settings.PlayerName = NameBox.Text ?? "";
            _settings.LastJoin = JoinBox.Text ?? "";
            _settings.WorldName = WorldBox.Text ?? "My World";
            _settings.GameDir = GameDir;
            if (int.TryParse(PortBox.Text, out var p)) _settings.Port = p;
            _settings.Save();
        }

        // Runs a button action and shows any error in the status bar instead of crashing.
        async void Run(Func<Task> action)
        {
            try { await action(); }
            catch (Exception e) { Status(e.Message, error: true); }
        }

        void Run(Action action) => Run(() => { action(); return Task.CompletedTask; });

        void Status(string text, bool error = false)
        {
            StatusText.Text = text;
            StatusText.Foreground = error ? Avalonia.Media.Brush.Parse("#FF8A7A") : Avalonia.Media.Brush.Parse("#8FB3C4");
        }

        // ---------- play ----------

        void JoinAndPlay()
        {
            if (!JoinCode.TryParseAddress(JoinBox.Text, Protocol.DefaultPort, out var host, out var port))
                throw new Exception("Type a join code (like KQ7MX-3HD2P) or an IP address first.");
            LaunchGame(host, port);
            Status($"Starting Subnautica... load a save and you'll join {host}:{port} automatically.");
        }

        void HostAndPlay()
        {
            if (!_host.Running) StartServer();
            LaunchGame("127.0.0.1", _host.Port);
            Tabs.SelectedIndex = 1;
            Status("Server running + game starting. Send friends the join code. Keep this window open!");
        }

        void LaunchGame(string host, int port)
        {
            var dir = GameDir;
            if (!GameFolder.IsGameDir(dir))
            {
                Tabs.SelectedIndex = 2;
                throw new Exception("Subnautica.exe isn't in that folder. Set the right one in Setup.");
            }
            if (!GameFolder.HasBepInEx(dir))
            {
                Tabs.SelectedIndex = 2;
                throw new Exception("BepInEx isn't installed yet. Hit 'Install BepInEx for me' in Setup (or install it yourself).");
            }

            GameFolder.InstallMod(dir);
            GameFolder.WriteLaunchInfo(dir, PlayerName, host, port);
            SaveSettings();
            RefreshSetup();

            if (Process.GetProcessesByName("Subnautica").Any())
            {
                Status("Subnautica is already running: press F8 in game and join from there.");
                return;
            }
            Process.Start(new ProcessStartInfo(Path.Combine(dir, "Subnautica.exe")) { WorkingDirectory = dir, UseShellExecute = true });
        }

        // ---------- server ----------

        void ToggleServer()
        {
            if (_host.Running)
            {
                _host.Stop();
                RefreshServer();
                Status("Server stopped. World saved.");
            }
            else
            {
                StartServer();
                Status("Server running. Hit HOST & PLAY on the Play tab to jump in yourself.");
            }
        }

        void StartServer()
        {
            if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
                throw new Exception("Port has to be a number between 1 and 65535.");
            var world = string.IsNullOrWhiteSpace(WorldBox.Text) ? "My World" : WorldBox.Text.Trim();
            LogBox.Text = "";
            try
            {
                _host.Start(world, port);
            }
            catch (System.Net.Sockets.SocketException)
            {
                throw new Exception($"Port {port} is already in use (another server running?). Try a different port.");
            }
            SaveSettings();
            RefreshServer();
        }

        void RefreshServer()
        {
            bool running = _host.Running;
            ServerButton.Content = running ? "STOP SERVER" : "START SERVER";
            WorldBox.IsEnabled = PortBox.IsEnabled = !running;
            CodesCard.IsVisible = running;
            InternetCodeText.Text = _host.InternetCode ?? "finding...";
            LanCodeText.Text = _host.LanCode ?? "-";
            CopyInternetButton.IsEnabled = _host.InternetCode != null;
            CopyLanButton.IsEnabled = _host.LanCode != null;
            RouterText.Text = _host.RouterStatus ?? "";
            RouterText.Foreground = Avalonia.Media.Brush.Parse(_host.RouterOk ? "#5BD68B" : "#E8C27A");
            PlayersList.ItemsSource = _host.Players.Select(p => p.Name).ToList();
        }

        void AppendLog(string line)
        {
            var text = (LogBox.Text ?? "") + $"[{DateTime.Now:HH:mm:ss}] {line}\n";
            if (text.Length > 20000) text = text.Substring(text.Length - 20000);
            LogBox.Text = text;
            LogBox.CaretIndex = text.Length;
        }

        async Task Copy(string text)
        {
            if (string.IsNullOrEmpty(text) || Clipboard == null) return;
            await Clipboard.SetTextAsync(text);
            Status("Copied " + text + ". Send it to your friends!");
        }

        // ---------- setup ----------

        void RefreshSetup()
        {
            var dir = GameDir;
            bool game = GameFolder.IsGameDir(dir);
            bool bep = game && GameFolder.HasBepInEx(dir);
            var installed = game ? GameFolder.InstalledModVersion(dir) : null;
            var bundled = GameFolder.BundledModVersion;

            Mark(GameStatus, game, game ? "Subnautica found" : "Subnautica.exe not found in this folder");
            Mark(BepStatus, bep, bep ? "BepInEx installed" : "BepInEx not installed (needed to load mods)");
            Mark(ModStatus, installed != null && installed >= bundled,
                installed == null ? "Mod not installed yet (Play installs it for you)"
                : installed >= bundled ? $"Mod v{installed.ToString(3)} installed"
                : $"Mod v{installed.ToString(3)} is old, Play will update it to v{bundled.ToString(3)}");

            InstallBepButton.IsEnabled = game;
            InstallModButton.IsEnabled = game;
            OpenFolderButton.IsEnabled = game;
        }

        static void Mark(TextBlock block, bool ok, string text)
        {
            block.Text = (ok ? "✔  " : "✖  ") + text;
            block.Classes.Set("ok", ok);
            block.Classes.Set("bad", !ok);
        }

        async Task Browse()
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Find your Subnautica folder" });
            var path = picked.FirstOrDefault()?.TryGetLocalPath();
            if (path != null) GameDirBox.Text = path;
            RefreshSetup();
        }

        async Task InstallBepInEx()
        {
            InstallBepButton.IsEnabled = false;
            try
            {
                await GameFolder.InstallBepInEx(GameDir, new Progress<string>(s => Status(s)));
                Status("BepInEx installed. Start the game once so it finishes setting itself up.");
            }
            finally { RefreshSetup(); }
        }

        void InstallMod()
        {
            GameFolder.InstallMod(GameDir);
            RefreshSetup();
            Status("Mod installed.");
        }

        void OpenModFolder()
        {
            var dir = GameFolder.PluginDir(GameDir);
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
    }
}
