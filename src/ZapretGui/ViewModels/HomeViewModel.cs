using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using ZapretGui.Core;

namespace ZapretGui.ViewModels;

public sealed partial class HomeViewModel(MainViewModel main) : PageViewModel(main)
{
    private bool _syncing;

    public ObservableCollection<Strategy> Strategies { get; } = new();

    [ObservableProperty] private Strategy? _selectedStrategy;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusTitle = "Обход выключен";
    [ObservableProperty] private string _statusSubtitle = "Выберите стратегию и нажмите «Включить»";
    [ObservableProperty] private string _toggleText = "Включить";
    [ObservableProperty] private bool _canSwitch;
    [ObservableProperty] private bool _zapretMissing;
    [ObservableProperty] private string _argsPreview = "";
    [ObservableProperty] private bool _serviceAutostart;
    [ObservableProperty] private bool _comssDns;
    [ObservableProperty] private string? _downloadStatus;

    public string ServiceDescription => IsWindows
        ? "Служба Windows «zapret» запускает выбранную стратегию при старте системы, даже без входа в приложение"
        : "Служба systemd «zapret-gui» запускает выбранную стратегию при старте системы";

    public void ReloadStrategies()
    {
        var z = Controller.Install;
        ZapretMissing = !z.Exists;
        var current = SelectedStrategy?.Name ?? Settings.LastStrategy;
        Strategies.Clear();
        foreach (var s in z.GetStrategies()) Strategies.Add(s);
        SelectedStrategy = Strategies.FirstOrDefault(s => s.Name == current) ?? Strategies.FirstOrDefault();
        Main.Test.ReloadStrategies(Strategies);
    }

    public void ApplyState(BypassState state)
    {
        IsRunning = state.IsRunning;
        StatusTitle = state.IsRunning ? "Обход включён" : "Обход выключен";
        StatusSubtitle = state.Mode switch
        {
            BypassMode.Service => $"Стратегия «{state.Strategy}» • работает как служба (автозапуск)",
            BypassMode.Process => $"Стратегия «{state.Strategy}»",
            _ => state.ServiceInstalled
                ? $"Служба автозапуска установлена («{state.ServiceStrategy}»), но остановлена"
                : "Выберите стратегию и нажмите «Включить»",
        };
        ToggleText = state.IsRunning ? "Выключить" : "Включить";
        CanSwitch = state.IsRunning && SelectedStrategy != null &&
                    !string.Equals(SelectedStrategy.Name, state.Strategy, StringComparison.OrdinalIgnoreCase);

        _syncing = true;
        ServiceAutostart = state.ServiceInstalled;
        _syncing = false;
    }

    public async Task RefreshTogglesAsync()
    {
        _syncing = true;
        try { ComssDns = await Task.Run(Main.Dns.IsComssActive); }
        catch { }
        finally { _syncing = false; }
    }

    partial void OnSelectedStrategyChanged(Strategy? value)
    {
        if (value == null) { ArgsPreview = ""; return; }
        try
        {
            var root = Controller.Install.Root + Path.DirectorySeparatorChar;
            ArgsPreview = string.Join(" ", Controller.Install.BuildArgs(value).Select(Shell.QuoteWindowsArg)).Replace(root, "");
        }
        catch (Exception ex)
        {
            ArgsPreview = "Ошибка разбора: " + ex.Message;
        }
        CanSwitch = IsRunning && !string.Equals(value.Name, Main.State.Strategy, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (IsRunning)
        {
            await GuardAsync(Controller.StopAsync);
            return;
        }
        if (SelectedStrategy is not { } s)
        {
            Main.Notify("Не выбрана стратегия", ZapretMissing ? "Сначала скачайте файлы zapret" : null, InfoBarSeverity.Warning);
            return;
        }
        await GuardAsync(() => Controller.StartAsync(s));
    }

    [RelayCommand]
    private async Task SwitchAsync()
    {
        if (SelectedStrategy is { } s) await GuardAsync(() => Controller.StartAsync(s), $"Стратегия «{s.Name}» применена");
    }

    partial void OnServiceAutostartChanged(bool value)
    {
        if (_syncing) return;
        _ = SetServiceAutostartAsync(value);
    }

    private async Task SetServiceAutostartAsync(bool enable)
    {
        bool ok;
        if (enable)
        {
            if (SelectedStrategy is not { } s)
            {
                Main.Notify("Не выбрана стратегия", "Выберите стратегию для автозапуска", InfoBarSeverity.Warning);
                ok = false;
            }
            else ok = await GuardAsync(() => Controller.InstallServiceAsync(s), $"Автозапуск включён: «{s.Name}»");
        }
        else ok = await GuardAsync(Controller.RemoveServiceAsync, "Автозапуск выключен, служба удалена");

        if (!ok) { _syncing = true; ServiceAutostart = !enable; _syncing = false; }
        await Main.RefreshStateAsync();
    }

    partial void OnComssDnsChanged(bool value)
    {
        if (_syncing) return;
        _ = SetComssAsync(value);
    }

    private async Task SetComssAsync(bool enable)
    {
        var ok = enable
            ? await GuardAsync(() => Main.Dns.EnableAsync(Settings.DnsUseDoh), "Comss DNS подключён")
            : await GuardAsync(Main.Dns.DisableAsync, "DNS возвращён на автоматический");
        if (!ok) { _syncing = true; ComssDns = !enable; _syncing = false; }
        await Main.DnsPage.RefreshAsync();
    }

    public void SetComssToggleSilently(bool value)
    {
        _syncing = true;
        ComssDns = value;
        _syncing = false;
    }

    [RelayCommand]
    private async Task DownloadZapretAsync()
    {
        var progress = new UiProgress(s => DownloadStatus = s);
        var ok = await GuardAsync(async () =>
        {
            await Controller.StopAsync();
            await ZapretUpdater.DownloadAsync(Controller.Install, progress);
        }, "Файлы zapret загружены");
        progress.Complete(ok ? null : "Не удалось загрузить. Проверьте подключение или скачайте релиз вручную.");
        ReloadStrategies();
        Main.SettingsPage.RefreshInfo();
    }

    [RelayCommand]
    private void OpenZapretFolder() => Shell.OpenInShell(Controller.Install.Root);

    [RelayCommand]
    private void OpenStrategyFile()
    {
        if (SelectedStrategy != null) Shell.OpenInShell(SelectedStrategy.FilePath);
    }

    [RelayCommand]
    private void ReloadList() => ReloadStrategies();
}
