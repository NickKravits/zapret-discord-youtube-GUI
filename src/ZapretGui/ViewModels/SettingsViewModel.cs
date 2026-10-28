using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using ZapretGui.Core;

namespace ZapretGui.ViewModels;

public sealed partial class SettingsViewModel(MainViewModel main) : PageViewModel(main)
{
    private bool _syncing;

    public IReadOnlyList<string> GameFilterModes { get; } = ["Выключен", "TCP и UDP", "Только TCP", "Только UDP"];
    private static readonly string[] GameFilterValues = ["disabled", "all", "tcp", "udp"];
    public IReadOnlyList<string> IpsetModes { get; } = ["loaded — по списку IP", "none — выключен", "any — все IP"];
    public IReadOnlyList<string> Themes { get; } = ["Как в системе", "Светлая", "Тёмная"];
    private static readonly string[] ThemeValues = ["System", "Light", "Dark"];

    [ObservableProperty] private bool _appAutostart;
    [ObservableProperty] private bool _startBypassOnLaunch;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _checkUpdatesOnStart;
    [ObservableProperty] private int _gameFilterIndex;
    [ObservableProperty] private string _gameTcpRange = GameFilterSettings.DefaultRange;
    [ObservableProperty] private string _gameUdpRange = GameFilterSettings.DefaultRange;
    [ObservableProperty] private int _ipsetIndex;
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private string _zapretDir = "";
    [ObservableProperty] private string _localVersion = "—";
    [ObservableProperty] private string _remoteVersion = "не проверялась";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _nfqwsPath = "";
    [ObservableProperty] private string _nfqwsStatus = "";
    [ObservableProperty] private string? _progressText;

    public string AppVersion => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public string AppAutostartDescription => IsWindows
        ? "Через Планировщик заданий с правами администратора, без запроса UAC. Приложение стартует свёрнутым в трей"
        : "Через ~/.config/autostart. Приложение стартует свёрнутым в трей";

    public void LoadFromSettings()
    {
        _syncing = true;
        StartBypassOnLaunch = Settings.StartBypassOnLaunch;
        MinimizeToTray = Settings.MinimizeToTray;
        CheckUpdatesOnStart = Settings.CheckUpdatesOnStart;
        ThemeIndex = Math.Max(0, Array.IndexOf(ThemeValues, Settings.Theme));
        _syncing = false;
        ApplyTheme(Settings.Theme);
        RefreshInfo();
    }

    public void RefreshInfo()
    {
        _syncing = true;
        var z = Controller.Install;
        ZapretDir = z.Root;
        LocalVersion = z.LocalVersion ?? (z.Exists ? "неизвестна" : "не установлен");
        var gf = z.LoadGameFilter();
        GameFilterIndex = Math.Max(0, Array.IndexOf(GameFilterValues, gf.Mode));
        GameTcpRange = gf.TcpRange;
        GameUdpRange = gf.UdpRange;
        IpsetIndex = z.Exists ? (int)z.GetIpsetMode() : 0;
        NfqwsPath = Settings.NfqwsPath ?? "";
        if (IsLinux)
        {
            var found = LinuxBackend.FindNfqws(Settings);
            NfqwsStatus = found != null ? $"Найден: {found}" : "nfqws не найден — скачайте его или укажите путь";
        }
        _syncing = false;
    }

    public override async Task OnNavigatedToAsync()
    {
        RefreshInfo();
        _syncing = true;
        AppAutostart = OperatingSystem.IsWindows() ? await WindowsBackend.IsAppAutostartEnabledAsync() : LinuxBackend.IsAppAutostartEnabled;
        _syncing = false;
    }

    partial void OnAppAutostartChanged(bool value)
    {
        if (_syncing) return;
        _ = SetAppAutostartAsync(value);
    }

    private async Task SetAppAutostartAsync(bool value)
    {
        var ok = await GuardAsync(async () =>
        {
            if (OperatingSystem.IsWindows()) await WindowsBackend.SetAppAutostartAsync(value);
            else LinuxBackend.SetAppAutostart(value);
        }, value ? "Приложение будет запускаться при входе в систему" : "Автозапуск приложения выключен");
        if (!ok) { _syncing = true; AppAutostart = !value; _syncing = false; }
    }

    partial void OnStartBypassOnLaunchChanged(bool value)
    {
        if (_syncing) return;
        Settings.StartBypassOnLaunch = value;
        Settings.Save();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_syncing) return;
        Settings.MinimizeToTray = value;
        Settings.Save();
    }

    partial void OnCheckUpdatesOnStartChanged(bool value)
    {
        if (_syncing) return;
        Settings.CheckUpdatesOnStart = value;
        Settings.Save();
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_syncing || value < 0) return;
        Settings.Theme = ThemeValues[value];
        Settings.Save();
        ApplyTheme(Settings.Theme);
    }

    public static void ApplyTheme(string theme)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    partial void OnIpsetIndexChanged(int value)
    {
        if (_syncing || value < 0) return;
        _ = SetIpsetAsync((IpsetMode)value);
    }

    private async Task SetIpsetAsync(IpsetMode mode)
    {
        var ok = await GuardAsync(async () =>
        {
            Controller.Install.SetIpsetMode(mode);
            await Controller.RestartAsync();
        }, $"Режим IPSet: {ZapretInstall.IpsetModeText(mode)}");
        if (!ok) RefreshInfo();
    }

    [RelayCommand]
    private async Task SaveGameFilterAsync()
    {
        var tcp = GameTcpRange.Replace(" ", "");
        var udp = GameUdpRange.Replace(" ", "");
        if (!GameFilterSettings.IsValidRange(tcp) || !GameFilterSettings.IsValidRange(udp))
        {
            Main.Notify("Неверный диапазон портов", "Пример: 1024-65535 или 1024-1934,1936-65535", InfoBarSeverity.Warning);
            return;
        }
        await GuardAsync(async () =>
        {
            var gf = new GameFilterSettings { Mode = GameFilterValues[Math.Max(0, GameFilterIndex)], TcpRange = tcp, UdpRange = udp };
            gf.Save(Controller.Install.GameFilterFile);
            Log.Info($"Game Filter: {gf.Mode}, TCP {gf.TcpRange}, UDP {gf.UdpRange}");
            await Controller.RestartAsync();
        }, "Game Filter сохранён");
    }

    [RelayCommand]
    private async Task UpdateIpsetAsync()
    {
        await GuardAsync(async () =>
        {
            await ZapretUpdater.UpdateIpsetAsync(Controller.Install);
            await Controller.RestartAsync();
        }, "Список IPSet обновлён");
        RefreshInfo();
    }

    public async Task CheckUpdatesQuietAsync()
    {
        var remote = await ZapretUpdater.GetRemoteVersionAsync();
        if (remote == null) return;
        RemoteVersion = remote;
        var local = Controller.Install.LocalVersion;
        UpdateAvailable = local != null && local != remote;
        if (UpdateAvailable)
        {
            Log.Info($"Доступна новая версия zapret-discord-youtube: {remote} (установлена {local})");
            Main.Notify("Доступно обновление zapret", $"Версия {remote}. Обновить можно в «Параметрах»", InfoBarSeverity.Informational);
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        try
        {
            await CheckUpdatesQuietAsync();
            if (!UpdateAvailable && RemoteVersion != "не проверялась")
                Main.Notify("Обновлений нет", $"Установлена актуальная версия {LocalVersion}", InfoBarSeverity.Success);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DownloadZapretAsync()
    {
        var progress = new UiProgress(s => ProgressText = s);
        await GuardAsync(async () =>
        {
            var state = await Controller.GetStateAsync();
            await Controller.StopAsync();
            await ZapretUpdater.DownloadAsync(Controller.Install, progress);
            Main.Home.ReloadStrategies();
            if (state.IsRunning && state.Strategy != null && Controller.Install.FindStrategy(state.Strategy) is { } s)
                await Controller.StartAsync(s);
        }, "Файлы zapret обновлены");
        progress.Complete();
        UpdateAvailable = false;
        RefreshInfo();
    }

    [RelayCommand]
    private void OpenReleases() => Shell.OpenInShell(ZapretUpdater.ReleasesUrl);

    [RelayCommand]
    private void OpenZapretFolder() => Shell.OpenInShell(Controller.Install.Root);

    public void ChangeZapretDir(string dir)
    {
        Controller.SetZapretDir(dir);
        Main.Home.ReloadStrategies();
        RefreshInfo();
        Log.Info($"Папка zapret: {dir}");
    }

    [RelayCommand]
    private void ResetZapretDir() => ChangeZapretDir(AppPaths.DefaultZapretDir);

    [RelayCommand]
    private void SaveNfqwsPath()
    {
        Settings.NfqwsPath = string.IsNullOrWhiteSpace(NfqwsPath) ? null : NfqwsPath.Trim();
        Settings.Save();
        RefreshInfo();
    }

    [RelayCommand]
    private async Task DownloadNfqwsAsync()
    {
        var progress = new UiProgress(s => ProgressText = s);
        await GuardAsync(async () =>
        {
            var path = await LinuxBackend.DownloadNfqwsAsync(progress);
            Settings.NfqwsPath = path;
            Settings.Save();
        }, "nfqws загружен");
        progress.Complete();
        RefreshInfo();
    }

    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        Main.NavigateTo("console");
        await GuardAsync(() => Diagnostics.RunAsync(Controller.Install));
    }

    [RelayCommand]
    private void OpenProject() => Shell.OpenInShell("https://github.com/Flowseal/zapret-discord-youtube");

    [RelayCommand]
    private void OpenDataFolder() => Shell.OpenInShell(AppPaths.DataDir);
}
