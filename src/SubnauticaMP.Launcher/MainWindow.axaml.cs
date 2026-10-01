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

            // no OS title bar (the window is shaped like the PDA): drag it by the handle or the top bar
            Handle.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
            DragBar.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
            MinButton.Click += (_, _) => WindowState = WindowState.Minimized;
            CloseButton.Click += (_, _) => Close();

            // PLAY, or FIND GAME while the game folder isn't known yet (one main button, never two)
            PlayButton.Click += (_, _) => Run(GameFolder.IsGameDir(GameDir) && _search == null ? PlayGame : SearchPc);
            ServerButton.Click += (_, _) => Run(ToggleServer);
            NewServerButton.Click += (_, _) => Run(OpenCreateServer);
            CreateBackButton.Click += (_, _) => ShowServerView(ServerListView);
            CreateServerButton.Click += (_, _) => Run(CreateServer);
            KickButton.Click += (_, _) => Run(() => KickSelected(false));
            BanButton.Click += (_, _) => Run(() => KickSelected(true));
            BrowseButton.Click += (_, _) => Run(Browse);
            PlayBrowseButton.Click += (_, _) => Run(Browse);
            SearchButton.Click += (_, _) => Run(SearchPc);
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
            CopyLaunchOptionButton.Click += (_, _) => Run(() => Copy(LinuxLaunchOption, "Copied. Paste it in Steam: Subnautica › Properties › Launch options."));

            // Linux / Steam Deck: the game runs in Proton (Steam starts it), Windows firewall rules don't apply
            bool linux = !OperatingSystem.IsWindows();
            LinuxCard.IsVisible = linux;
            LaunchOptionBox.Text = LinuxLaunchOption;
            FirewallButton.IsVisible = !linux;
            GameDirBox.TextChanged += (_, _) => RefreshSetup();
            WorldBox.TextChanged += (_, _) => RefreshWorld();
            ModeBox.SelectionChanged += (_, _) => RefreshWorld();

            _host.Log += line => Dispatcher.UIThread.Post(() => AppendLog(line));
            _host.Changed += () => Dispatcher.UIThread.Post(RefreshServer);
            PlayersList.SelectionChanged += (_, _) => RefreshKickButtons();

            RefreshSetup();
            RefreshWorld();
            RefreshServer();
            RefreshServerList();
            ServerListView.Classes.Add("shown");
            Status(GameFolder.IsGameDir(GameDir)
                ? "Ready. Hit PLAY."
                : "Subnautica isn't found yet: hit FIND GAME on the Home page.");
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
            StatusText.Foreground = Avalonia.Media.Brush.Parse(error ? Bad : Seafoam);
        }

        // theme colors used from code (same as App.axaml)
        const string Ok = "#5BD68B", Warn = "#F2C14E", Bad = "#FF7A6B", Seafoam = "#A8DADC", Scanner = "#3FE0E8";

        // Proton (Linux / Steam Deck) only loads BepInEx's winhttp.dll when told to.
        const string LinuxLaunchOption = "WINEDLLOVERRIDES=\"winhttp=n,b\" %command%";

        static bool GameRunning() =>
            Process.GetProcessesByName("Subnautica").Any() || Process.GetProcessesByName("Subnautica.exe").Any();

        // A status pill on the Play banner: a dot (ok / warn / bad / off) and a word.
        static void Chip(Border chip, TextBlock text, string state, string label)
        {
            text.Text = label;
            foreach (var c in new[] { "ok", "warn", "bad" }) chip.Classes.Set(c, c == state);
        }

        // ---------- play ----------

        // Opens the game on its main menu (multiplayer is started from the game's own Multiplayer button).
        async Task PlayGame()
        {
            var dir = GameDir;
            if (!GameFolder.IsGameDir(dir))
                throw new Exception("Can't find Subnautica yet. Hit FIND GAME on the Home page first.");
            if (!GameFolder.HasBepInEx(dir))
            {
                Tabs.SelectedIndex = 2;
                throw new Exception("BepInEx isn't installed yet. Hit 'Install BepInEx for me' in Setup (or install it yourself).");
            }

            GameFolder.InstallMod(dir); // multiplayer + optimizer
            if (!GameFolder.HasNautilus(dir)) await TryInstallNautilus(dir);
            GameFolder.ClearLaunchInfo(dir); // no auto-join: you pick a server in the game's menu
            SaveSettings();
            RefreshSetup();

            if (GameRunning())
            {
                Status("Subnautica is already running: use the Multiplayer button in its main menu.");
                return;
            }

            if (!OperatingSystem.IsWindows())
            {
                // Linux: Subnautica.exe can't start by itself, Steam starts it in Proton
                try { Process.Start(new ProcessStartInfo("xdg-open", "steam://rungameid/264710") { UseShellExecute = false }); }
                catch { Process.Start(new ProcessStartInfo("steam", "-applaunch 264710") { UseShellExecute = false }); }
                Status("Starting Subnautica through Steam... No Multiplayer button in the game? Set the launch option in Setup › Linux once.");
                return;
            }

            string note = "";
            bool steamCopy = GameFolder.IsSteamCopy(dir) || dir.Replace('\\', '/').IndexOf("/steamapps/", StringComparison.OrdinalIgnoreCase) >= 0;
            if (steamCopy)
            {
                // stops the game from handing itself over to Steam (which would start Steam's own copy, maybe without the mod)
                GameFolder.WriteSteamAppId(dir);
                var steamDir = GameFinder.SteamInstallDir();
                if (steamDir != null && !string.Equals(Path.GetFullPath(steamDir).TrimEnd('\\', '/'), Path.GetFullPath(dir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    note = $"  Heads up: Steam's own Subnautica is in {steamDir}. Starting it from Steam won't have the mod; use PLAY here (or point Setup at that folder).";

                // the Steam version needs Steam running, or it closes right away
                if (!Process.GetProcessesByName("steam").Any())
                {
                    Status("Opening Steam first (Subnautica needs it)...");
                    var steamExe = GameFinder.SteamExe();
                    if (steamExe != null) Process.Start(new ProcessStartInfo(steamExe, "-silent") { UseShellExecute = true });
                    else Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true });
                    for (int i = 0; i < 60 && !Process.GetProcessesByName("steam").Any(); i++) await Task.Delay(500);
                    if (!Process.GetProcessesByName("steam").Any())
                        throw new Exception("Steam didn't start. Open Steam and log in, then hit PLAY again.");
                    Status("Waiting for Steam to finish logging in...");
                    await Task.Delay(8000);
                }
            }

            Process.Start(new ProcessStartInfo(Path.Combine(dir, "Subnautica.exe")) { WorkingDirectory = dir, UseShellExecute = true });
            Status("Starting Subnautica... click Multiplayer in its main menu to host or join." + note);
        }

        // ---------- server ----------

        void ToggleServer()
        {
            if (_host.Running)
            {
                _host.Stop();
                GameFolder.ClearLocalServer(GameDir);
                RefreshServer();
                RefreshServerList();
                ShowServerView(ServerListView);
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
            AppendLog(GameFolder.IsGameDir(GameDir) ? $"Using game files from: '{GameDir}'" : "Subnautica folder not set (Setup tab); the server runs anyway");
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
            WorldBox.IsEnabled = PortBox.IsEnabled = HostPasswordBox.IsEnabled = !running;
            ModeBox.IsEnabled = !running && HostedServer.TryLoadWorld(WorldName) == null;
            var world = running ? _host.World : HostedServer.TryLoadWorld(WorldName);
            WorldSummary.Text = $"{WorldName}  ·  {(world?.GameMode ?? Mode)}" + (world == null ? "  ·  new" : "");
            if (world != null && running)
            {
                LobbyText.Text = world.Started
                    ? "Game started. Friends can still join any time."
                    : "Lobby: new world. Everyone waits on a black screen until the host presses ENTER in game.";
                LobbyText.Foreground = Avalonia.Media.Brush.Parse(world.Started ? Ok : Scanner);
            }
            CodesCard.IsVisible = running;
            if (running)
            {
                RunTitle.Text = WorldName;
                RunSubtitle.Text = $"{world?.GameMode ?? Mode}  ·  port {_host.Port}  ·  {(world?.Started == true ? "in progress" : "lobby")}";
                if (!ServerRunView.IsVisible) ShowServerView(ServerRunView);
            }
            int online = _host.Players.Count;
            Chip(ServerChip, ServerChipText, running ? "ok" : "off",
                 running ? $"Server on  ·  {online} player{(online == 1 ? "" : "s")}" : "Server off");
            InternetCodeText.Text = _host.InternetCode ?? "finding...";
            LanCodeText.Text = _host.LanCode ?? "-";
            CopyInternetButton.IsEnabled = _host.InternetCode != null;
            CopyLanButton.IsEnabled = _host.LanCode != null;
            RouterText.Text = _host.RouterStatus ?? "";
            RouterText.Foreground = Avalonia.Media.Brush.Parse(_host.RouterOk ? Ok : Warn);
            ReachText.Text = _host.ReachStatus ?? "";
            ReachText.Foreground = Avalonia.Media.Brush.Parse(_host.Reachable == true ? Ok : _host.Reachable == false ? Bad : Seafoam);
            int hostId = _host.HostId;
            var rows = _host.Players.Select(p => new PlayerRow(p.Id, p.Id == hostId ? p.Name + "  (host)" : p.Name)).ToList();
            PlayersList.ItemsSource = rows;
            NoPlayersText.Text = running ? "No one online yet" : "Server is off";
            NoPlayersText.IsVisible = rows.Count == 0;
            RefreshKickButtons();
        }

        // ---------- server list (like Nitrox: your worlds, create one, open one) ----------

        // Shows one of the three server views (list / create / running) with a short fade + slide in.
        void ShowServerView(Control view)
        {
            foreach (var v in new Control[] { ServerListView, ServerCreateView, ServerRunView })
            {
                if (v == view) continue;
                v.Classes.Remove("shown");
                v.IsVisible = false;
            }
            view.Classes.Remove("shown");
            view.IsVisible = true;
            Dispatcher.UIThread.Post(() => view.Classes.Add("shown"), DispatcherPriority.Background);
        }

        static string WorldsFolder => Path.GetDirectoryName(HostedServer.WorldPath("x"));

        void RefreshServerList()
        {
            var files = Directory.Exists(WorldsFolder)
                ? new DirectoryInfo(WorldsFolder).GetFiles("*.dat").Where(f => f.Extension == ".dat").OrderByDescending(f => f.LastWriteTime).ToList()
                : new System.Collections.Generic.List<FileInfo>();
            ServerItems.Items.Clear();
            foreach (var f in files)
            {
                var name = Path.GetFileNameWithoutExtension(f.Name);
                WorldState world = null;
                try { world = WorldState.LoadFromFile(f.FullName); } catch { }
                ServerItems.Items.Add(ServerRow(name, world, f.LastWriteTime));
            }
            NoServersCard.IsVisible = files.Count == 0;
        }

        Control ServerRow(string name, WorldState world, DateTime saved)
        {
            var start = new Button { Content = "Start", Classes = { "primary" }, Padding = new Avalonia.Thickness(22, 9), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            start.Click += (_, _) => Run(() => StartWorld(name));
            var delete = new Button { Content = "Delete", Classes = { "danger" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
            bool armed = false;
            delete.Click += (_, _) => Run(() =>
            {
                if (!armed) { armed = true; delete.Content = "Sure?"; return; } // second click deletes
                DeleteWorld(name);
            });
            delete.PointerExited += (_, _) => { armed = false; delete.Content = "Delete"; };

            var details = world == null ? "can't read this save"
                : $"{world.GameMode}  ·  {(world.Started ? "in progress" : "lobby")}  ·  last saved {saved:d MMM, HH:mm}";
            var text = new StackPanel { Spacing = 3, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(12, 0, 8, 0) };
            text.Children.Add(new TextBlock { Text = name, Classes = { "rowtitle" } });
            text.Children.Add(new TextBlock { Text = details, Classes = { "rowdetail" } });

            var icon = new Border { Classes = { "bubble" }, Child = new Avalonia.Controls.Shapes.Path
            {
                Classes = { "glyph" },
                Data = Avalonia.Media.Geometry.Parse("M12,3 C17,3 21,7 21,12 C21,17 17,21 12,21 C7,21 3,17 3,12 C3,7 7,3 12,3 Z M3,12 H21 M12,3 C9,6 9,18 12,21 M12,3 C15,6 15,18 12,21"),
            } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
            grid.Children.Add(icon);
            Grid.SetColumn(text, 1); grid.Children.Add(text);
            Grid.SetColumn(delete, 2); grid.Children.Add(delete);
            Grid.SetColumn(start, 3); grid.Children.Add(start);
            return new Border { Classes = { "card", "server" }, Padding = new Avalonia.Thickness(10, 10, 12, 10), Child = grid };
        }

        void OpenCreateServer()
        {
            if (_host.Running) throw new Exception("Stop the running server first.");
            var name = "My World";
            for (int i = 2; HostedServer.TryLoadWorld(name) != null || File.Exists(HostedServer.WorldPath(name)); i++) name = "My World " + i;
            WorldBox.Text = name;
            ShowServerView(ServerCreateView);
            WorldBox.Focus();
        }

        void CreateServer()
        {
            if (string.IsNullOrWhiteSpace(WorldBox.Text)) throw new Exception("Give the server a name first.");
            if (File.Exists(HostedServer.WorldPath(WorldName)))
                throw new Exception($"There's already a server called '{WorldName}'. Pick another name, or start it from the list.");
            StartServer();
            Status("Server running. Hit PLAY and pick 'Launcher server' in the game's Multiplayer menu to jump in yourself.");
        }

        void StartWorld(string name)
        {
            if (_host.Running) throw new Exception("Another server is already running.");
            WorldBox.Text = name;
            RefreshWorld(); // existing worlds keep their game mode
            StartServer();
            Status($"'{name}' is running. Hit PLAY and pick 'Launcher server' in the game's Multiplayer menu to jump in yourself.");
        }

        // Deleted worlds go to worlds/deleted (not gone for good, in case of a misclick).
        void DeleteWorld(string name)
        {
            var path = HostedServer.WorldPath(name);
            if (!File.Exists(path)) { RefreshServerList(); return; }
            var bin = Path.Combine(WorldsFolder, "deleted");
            Directory.CreateDirectory(bin);
            File.Move(path, Path.Combine(bin, $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.Now:yyyyMMdd-HHmmss}.dat"));
            RefreshServerList();
            Status($"Deleted '{name}' (a copy is kept in {bin}).");
        }

        // Kick / Ban only show up once there's someone selected to kick.
        void RefreshKickButtons() => KickButton.IsVisible = BanButton.IsVisible = _host.Running && PlayersList.SelectedItem != null;

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

        async Task Copy(string text, string done = null)
        {
            if (string.IsNullOrEmpty(text) || Clipboard == null) return;
            await Clipboard.SetTextAsync(text);
            Status(done ?? "Copied " + text + ". Send it to your friends!");
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
        // behind the game). And if that popup was ever closed / answered "private only", Windows made a hidden
        // BLOCK rule for Subnautica, and block rules beat allow rules. So: remove those, then add allow rules for
        // the port, this launcher and Subnautica. One admin popup.
        void AllowFirewall()
        {
            if (!OperatingSystem.IsWindows()) throw new Exception("Only needed on Windows.");
            int port = _host.Running ? _host.Port : int.TryParse(PortBox.Text, out var p) ? p : Protocol.DefaultPort;
            var game = Path.Combine(GameDir ?? "", "Subnautica.exe");
            var me = Environment.ProcessPath ?? "";
            string Q(string x) => "'" + x.Replace("'", "''") + "'";

            var script = new System.Text.StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
            // old hidden block rules for the game / launcher (made when the Windows popup was dismissed)
            script.AppendLine("Get-NetFirewallApplicationFilter | Where-Object { $_.Program -like '*\\Subnautica.exe' -or $_.Program -like '*\\SubnauticaMP-Launcher.exe' } |");
            script.AppendLine("  Get-NetFirewallRule | Where-Object { $_.Action -eq 'Block' -and $_.Direction -eq 'Inbound' } | Remove-NetFirewallRule");
            // our allow rules (replaced if they're already there)
            script.AppendLine("Remove-NetFirewallRule -DisplayName 'Subnautica Multiplayer*'");
            script.AppendLine($"New-NetFirewallRule -DisplayName 'Subnautica Multiplayer TCP {port}' -Direction Inbound -Action Allow -Protocol TCP -LocalPort {port} -Profile Any");
            if (File.Exists(me)) script.AppendLine($"New-NetFirewallRule -DisplayName 'Subnautica Multiplayer Launcher' -Direction Inbound -Action Allow -Program {Q(me)} -Profile Any");
            if (File.Exists(game)) script.AppendLine($"New-NetFirewallRule -DisplayName 'Subnautica Multiplayer Game' -Direction Inbound -Action Allow -Program {Q(game)} -Profile Any");
            var file = Path.Combine(Path.GetTempPath(), "SubnauticaMP-firewall.ps1");
            File.WriteAllText(file, script.ToString());

            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{file}\"")
            {
                UseShellExecute = true,
                Verb = "runas", // Windows asks "allow this app to make changes?" - say yes
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            try
            {
                using var proc = Process.Start(psi);
                proc?.WaitForExit(30000);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new Exception("You need to click Yes on the Windows popup to allow it.");
            }
            finally { try { File.Delete(file); } catch { } }
            Status("Firewall fixed (old block rules removed, allow rules added). Restart Subnautica if it was running, then Test again.");
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
            Mark(PlayGameStatus, game, game ? "Subnautica: " + dir : "Subnautica isn't found yet. FIND GAME searches this PC, or Browse to it.");
            PlayBrowseButton.Content = game ? "Change" : "Browse...";
            RefreshPlayButton();
            InstallHint.IsVisible = !game;
            Mark(BepStatus, bep, bep ? "BepInEx installed" : "BepInEx not installed (needed to load mods)");
            Mark(ModStatus, installed != null && installed >= bundled,
                installed == null ? "Mod not installed yet (Play installs it for you)"
                : installed >= bundled ? $"Mod v{installed.ToString(3)} installed"
                : $"Mod v{installed.ToString(3)} is old, Play will update it to v{bundled.ToString(3)}");
            bool optimizer = game && GameFolder.HasOptimizer(dir), nautilus = game && GameFolder.HasNautilus(dir);
            Mark(OptimizerStatus, optimizer, optimizer ? "Optimizer installed" : "Optimizer not installed yet (Play installs it)");
            Mark(NautilusStatus, nautilus, nautilus ? "Nautilus installed (the library other mods use)" : "Nautilus not installed yet (Play downloads it)");

            Chip(GameChip, GameChipText, game ? "ok" : "warn", game ? "Game found" : "Game not found yet");
            if (!game) Chip(ModChip, ModChipText, "off", "Mod waiting for the game");
            else if (!bep) Chip(ModChip, ModChipText, "warn", "BepInEx missing (Setup)");
            else if (installed == null || installed < bundled || !optimizer || !nautilus) Chip(ModChip, ModChipText, "warn", "Mods install on PLAY");
            else Chip(ModChip, ModChipText, "ok", "Mods ready");

            InstallBepButton.IsEnabled = game;
            InstallModButton.IsEnabled = game;
            OpenFolderButton.IsEnabled = game;
        }

        // Green tick when done, amber dot when it's just not set up yet (red is for real errors only).
        static void Mark(TextBlock block, bool ok, string text)
        {
            block.Text = (ok ? "✔  " : "●  ") + text;
            block.Classes.Set("ok", ok);
            block.Classes.Set("warn", !ok);
        }

        void RefreshPlayButton()
        {
            bool game = GameFolder.IsGameDir(GameDir);
            PlayButtonText.Text = _search != null ? "STOP" : game ? "PLAY" : "FIND GAME";
            PlayButtonText.LetterSpacing = game || _search != null ? 8 : 4;
            PlayCaption.Text = _search != null ? "Searching this PC for Subnautica..."
                : game ? "Installs the mod, then starts the game"
                : "Searches this PC for Subnautica";
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
            SearchButton.Content = "Stop searching";
            RefreshPlayButton();
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
                SearchButton.Content = "Search my PC";
                RefreshPlayButton();
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
                GameFolder.InstallMod(GameDir);
                var nautilus = await TryInstallNautilus(GameDir);
                Status("BepInEx, multiplayer mod and optimizer installed" + (nautilus ? ", plus Nautilus" : "") +
                       ". Start the game once so BepInEx finishes setting itself up.");
            }
            finally { RefreshSetup(); }
        }

        // Nautilus is nice to have (other mods need it), never a reason not to play: a failed download just says so.
        async Task<bool> TryInstallNautilus(string dir)
        {
            try
            {
                await GameFolder.InstallNautilus(dir, new Progress<string>(s => Status(s)));
                return true;
            }
            catch (Exception e)
            {
                Status(e.Message + " (the game still works without it; Setup › Install / update mod tries again)", error: true);
                return false;
            }
            finally { RefreshSetup(); }
        }

        async Task InstallMod()
        {
            GameFolder.InstallMod(GameDir);
            RefreshSetup();
            Status("Multiplayer mod and optimizer installed.");
            if (await TryInstallNautilus(GameDir)) Status("Multiplayer mod, optimizer and Nautilus installed.");
        }

        void OpenModFolder()
        {
            var dir = GameFolder.PluginDir(GameDir);
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
    }
}
