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
        readonly DispatcherTimer _watchTimer;
        System.Threading.CancellationTokenSource _search;

        public MainWindow()
        {
            InitializeComponent();

            VersionText.Text = "v" + GameFolder.BundledModVersion.ToString(3);
            HostPasswordBox.Text = _settings.HostPassword;
            WorldBox.Text = _settings.WorldName;
            ModeBox.ItemsSource = GameModes.All;
            ModeBox.SelectedItem = _settings.Mode;
            PortBox.Text = _settings.Port.ToString();
            GameDirBox.Text = GameFolder.IsGameDir(_settings.GameDir) ? _settings.GameDir : GameFinder.Detect() ?? _settings.GameDir;

            PlayButton.Click += (_, _) => Run(PlayGame);
            ServerButton.Click += (_, _) => Run(ToggleServer);
            KickButton.Click += (_, _) => Run(() => KickSelected(false));
            BanButton.Click += (_, _) => Run(() => KickSelected(true));
            BrowseButton.Click += (_, _) => Run(Browse);
            PlayBrowseButton.Click += (_, _) => Run(Browse);
            SearchButton.Click += (_, _) => Run(SearchPc);
            PlaySearchButton.Click += (_, _) => Run(SearchPc);
            GameDirBox.LostFocus += (_, _) => NormalizeGameDir();

            // Not found yet? Keep an eye out for the game being started.
            _watchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _watchTimer.Tick += (_, _) => WatchForRunningGame();
            _watchTimer.Start();
            InstallBepButton.Click += (_, _) => Run(InstallBepInEx);
            InstallModButton.Click += (_, _) => Run(InstallMod);
            OpenFolderButton.Click += (_, _) => Run(OpenModFolder);
            CopyInternetButton.Click += (_, _) => Run(() => Copy(_host.InternetCode));
            CopyLanButton.Click += (_, _) => Run(() => Copy(_host.LanCode));
            FirewallButton.Click += (_, _) => Run(AllowFirewall);
            TestReachButton.Click += (_, _) => Run(() => _host.CheckReachable());
            GameDirBox.TextChanged += (_, _) => RefreshSetup();
            WorldBox.TextChanged += (_, _) => RefreshWorld();
            ModeBox.SelectionChanged += (_, _) => RefreshWorld();

            _host.Log += line => Dispatcher.UIThread.Post(() => AppendLog(line));
            _host.Changed += () => Dispatcher.UIThread.Post(RefreshServer);

            RefreshSetup();
            RefreshWorld();
            RefreshServer();
            Status(GameFolder.IsGameDir(GameDir)
                ? "Ready. Pick Join or Host."
                : "Couldn't find Subnautica automatically. Hit 'Search my PC' at the top.");
        }

        string GameDir
        {
            get
            {
                var text = (GameDirBox.Text ?? "").Trim().Trim('"');
                // pasted the path to the exe itself? use its folder
                return text.EndsWith(GameFolder.Exe, StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(text) ?? text : text;
            }
        }
        string WorldName => string.IsNullOrWhiteSpace(WorldBox.Text) ? "My World" : WorldBox.Text.Trim();
        string Mode => ModeBox.SelectedItem as string ?? GameModes.Survival;

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            SaveSettings();
            _host.Stop(); // saves the world
            GameFolder.ClearLocalServer(GameDir);
            base.OnClosing(e);
        }

        void SaveSettings()
        {
            _settings.HostPassword = HostPasswordBox.Text ?? "";
            _settings.WorldName = WorldName;
            _settings.Mode = Mode;
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

        // Opens the game on its main menu (multiplayer is started from the game's own Multiplayer button).
        void PlayGame()
        {
            var dir = GameDir;
            if (!GameFolder.IsGameDir(dir))
                throw new Exception("Can't find Subnautica yet. Hit 'Search my PC' at the top of the Play tab first.");
            if (!GameFolder.HasBepInEx(dir))
            {
                Tabs.SelectedIndex = 2;
                throw new Exception("BepInEx isn't installed yet. Hit 'Install BepInEx for me' in Setup (or install it yourself).");
            }

            GameFolder.InstallMod(dir);
            GameFolder.ClearLaunchInfo(dir); // no auto-join: you pick a server in the game's menu
            SaveSettings();
            RefreshSetup();

            if (Process.GetProcessesByName("Subnautica").Any())
            {
                Status("Subnautica is already running: use the Multiplayer button in its main menu.");
                return;
            }
            Process.Start(new ProcessStartInfo(Path.Combine(dir, "Subnautica.exe")) { WorkingDirectory = dir, UseShellExecute = true });
            Status("Starting Subnautica... click Multiplayer in its main menu to host or join.");
        }

        // ---------- server ----------

        void ToggleServer()
        {
            if (_host.Running)
            {
                _host.Stop();
                GameFolder.ClearLocalServer(GameDir);
                RefreshServer();
                Status("Server stopped. World saved.");
            }
            else
            {
                StartServer();
                Status("Server running. Hit PLAY and pick 'Launcher server' in the game's Multiplayer menu to jump in yourself.");
            }
        }

        void StartServer()
        {
            if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
                throw new Exception("Port has to be a number between 1 and 65535.");
            LogBox.Text = "";
            try
            {
                _host.Start(WorldName, port, Mode, HostPasswordBox.Text ?? "");
            }
            catch (System.Net.Sockets.SocketException)
            {
                throw new Exception($"Port {port} is already in use (another server running?). Try a different port.");
            }
            // lets the game list this server as "Launcher server" (joins on 127.0.0.1, no password needed)
            if (GameFolder.IsGameDir(GameDir)) GameFolder.WriteLocalServer(GameDir, _host.Port, WorldName);
            SaveSettings();
            RefreshServer();
        }

        void RefreshServer()
        {
            bool running = _host.Running;
            ServerButton.Content = running ? "STOP SERVER" : "START SERVER";
            WorldBox.IsEnabled = PortBox.IsEnabled = HostPasswordBox.IsEnabled = !running;
            ModeBox.IsEnabled = !running && HostedServer.TryLoadWorld(WorldName) == null;
            var world = running ? _host.World : HostedServer.TryLoadWorld(WorldName);
            WorldSummary.Text = $"{WorldName}  ·  {(world?.GameMode ?? Mode)}" + (world == null ? "  ·  new" : "");
            if (world != null && running)
            {
                LobbyText.Text = world.Started
                    ? "Game started. Friends can still join any time."
                    : "Lobby: new world. Everyone waits on a black screen until the host presses ENTER in game.";
                LobbyText.Foreground = Avalonia.Media.Brush.Parse(world.Started ? "#5BD68B" : "#5FD4E0");
            }
            CodesCard.IsVisible = running;
            InternetCodeText.Text = _host.InternetCode ?? "finding...";
            LanCodeText.Text = _host.LanCode ?? "-";
            CopyInternetButton.IsEnabled = _host.InternetCode != null;
            CopyLanButton.IsEnabled = _host.LanCode != null;
            RouterText.Text = _host.RouterStatus ?? "";
            RouterText.Foreground = Avalonia.Media.Brush.Parse(_host.RouterOk ? "#5BD68B" : "#E8C27A");
            ReachText.Text = _host.ReachStatus ?? "";
            ReachText.Foreground = Avalonia.Media.Brush.Parse(_host.Reachable == true ? "#5BD68B" : _host.Reachable == false ? "#FF8A7A" : "#8FB3C4");
            int hostId = _host.HostId;
            PlayersList.ItemsSource = _host.Players.Select(p => new PlayerRow(p.Id, p.Id == hostId ? p.Name + "  (host)" : p.Name)).ToList();
            KickButton.IsEnabled = BanButton.IsEnabled = running;
        }

        sealed record PlayerRow(int Id, string Label)
        {
            public override string ToString() => Label;
        }

        void KickSelected(bool ban)
        {
            if (!(PlayersList.SelectedItem is PlayerRow row)) throw new Exception("Click a player in the list first.");
            if (!_host.Kick(row.Id, ban)) throw new Exception("They already left.");
            Status((ban ? "Banned " : "Kicked ") + row.Label + ".");
            RefreshServer();
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

        // Existing worlds keep the mode they were made with.
        void RefreshWorld()
        {
            if (ModeBox == null || ModeHint == null) return;
            var existing = HostedServer.TryLoadWorld(WorldName);
            if (existing != null)
            {
                if (!Equals(ModeBox.SelectedItem, existing.GameMode)) ModeBox.SelectedItem = existing.GameMode;
                ModeBox.IsEnabled = false;
                ModeHint.Text = $"'{WorldName}' already exists ({existing.GameMode}" +
                                (existing.Started ? ", in progress" : "") + "). Type a new name to make a new world.";
            }
            else
            {
                ModeBox.IsEnabled = !_host.Running;
                ModeHint.Text = "New world. " + GameModes.Describe(Mode);
            }
            if (WorldSummary != null && !_host.Running) RefreshServer();
        }

        // Windows blocks incoming connections for programs it hasn't asked about (the popup often hides
        // behind the game). Add allow rules for the port, this launcher and Subnautica. Needs admin once.
        void AllowFirewall()
        {
            if (!OperatingSystem.IsWindows()) throw new Exception("Only needed on Windows.");
            int port = _host.Running ? _host.Port : int.TryParse(PortBox.Text, out var p) ? p : Protocol.DefaultPort;
            var rules = new System.Collections.Generic.List<string>
            {
                $"netsh advfirewall firewall add rule name=\"Subnautica Multiplayer TCP {port}\" dir=in action=allow protocol=TCP localport={port} profile=any",
            };
            if (Environment.ProcessPath is string me)
                rules.Add($"netsh advfirewall firewall add rule name=\"Subnautica Multiplayer Launcher\" dir=in action=allow program=\"{me}\" profile=any");
            var game = Path.Combine(GameDir, "Subnautica.exe");
            if (File.Exists(game))
                rules.Add($"netsh advfirewall firewall add rule name=\"Subnautica Multiplayer Game\" dir=in action=allow program=\"{game}\" profile=any");

            var psi = new ProcessStartInfo("cmd.exe", "/c " + string.Join(" & ", rules))
            {
                UseShellExecute = true,
                Verb = "runas", // Windows asks "allow this app to make changes?" - say yes
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            try
            {
                using var proc = Process.Start(psi);
                proc?.WaitForExit(15000);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new Exception("You need to click Yes on the Windows popup to allow it.");
            }
            Status("Firewall rules added. Hit 'Test again'.");
            if (_host.Running) _host.CheckReachable();
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
            Mark(PlayGameStatus, game, game ? "Subnautica: " + dir : "Subnautica not found yet. Hit Search my PC, or Browse to it.");
            PlaySearchButton.IsVisible = !game;
            PlayBrowseButton.Content = game ? "Change" : "Browse...";
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
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pick your Subnautica folder (or any folder above it)" });
            var path = picked.FirstOrDefault()?.TryGetLocalPath();
            if (path == null) return;
            Status("Looking in " + path + "...");
            var found = await Task.Run(() => GameFinder.Normalize(path));
            if (found == null)
                throw new Exception("No Subnautica.exe in that folder or anywhere inside it. Try 'Search my PC'.");
            UseGameDir(found, "Found Subnautica in " + found);
        }

        async Task SearchPc()
        {
            if (_search != null) { _search.Cancel(); return; } // second click = stop
            _search = new System.Threading.CancellationTokenSource();
            SearchButton.Content = PlaySearchButton.Content = "Stop searching";
            try
            {
                var quick = GameFinder.Detect();
                var token = _search.Token;
                var found = quick ?? await Task.Run(() =>
                    GameFinder.SearchAllDrives(token, new Progress<string>(s => Status(s))));
                if (found == null)
                    throw new Exception("Couldn't find Subnautica.exe on any drive. Is it installed? If it's from the Microsoft Store / Xbox app, see the Setup tab.");
                UseGameDir(found, "Found Subnautica in " + found);
            }
            catch (OperationCanceledException) { Status("Search stopped."); }
            finally
            {
                _search = null;
                SearchButton.Content = PlaySearchButton.Content = "Search my PC";
            }
        }

        void WatchForRunningGame()
        {
            if (GameFolder.IsGameDir(GameDir)) return;
            var dir = GameFinder.FromRunningGame();
            if (dir != null) UseGameDir(dir, "Found Subnautica because it's running: " + dir);
        }

        void NormalizeGameDir()
        {
            if (GameFolder.IsGameDir(GameDir)) return;
            var dir = GameFinder.Normalize(GameDir);
            if (dir != null) GameDirBox.Text = dir;
        }

        void UseGameDir(string dir, string message)
        {
            GameDirBox.Text = dir;
            SaveSettings();
            RefreshSetup();
            if (GameFinder.IsLockedStoreInstall(dir))
                Status("That's the Microsoft Store version in a locked folder, mods can't go in there. " +
                       "In the Xbox app: Subnautica > Manage > Files > move it to a normal folder (like C:\\XboxGames), then Search again.", error: true);
            else
                Status(message);
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
