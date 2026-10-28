using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using ZapretGui.Core;

namespace ZapretGui.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _notifyTimer;

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        Controller = new ZapretController(settings);
        Dns = new DnsManager(settings);

        Home = new HomeViewModel(this);
        Test = new TestViewModel(this);
        Lists = new ListsViewModel(this);
        DnsPage = new DnsViewModel(this);
        Console = new ConsoleViewModel(this);
        SettingsPage = new SettingsViewModel(this);

        Controller.StateChanged += () => Dispatcher.UIThread.Post(() => _ = RefreshStateAsync());

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusTimer.Tick += (_, _) => _ = RefreshStateAsync();
        _statusTimer.Start();

        _notifyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _notifyTimer.Tick += (_, _) => { _notifyTimer.Stop(); IsNotificationOpen = false; };
    }

    public AppSettings Settings { get; }
    public ZapretController Controller { get; }
    public DnsManager Dns { get; }

    public HomeViewModel Home { get; }
    public TestViewModel Test { get; }
    public ListsViewModel Lists { get; }
    public DnsViewModel DnsPage { get; }
    public ConsoleViewModel Console { get; }
    public SettingsViewModel SettingsPage { get; }

    public bool IsAdmin => Environment.IsPrivilegedProcess;
    public bool ShowAdminWarning => OperatingSystem.IsWindows() && !IsAdmin;

    [ObservableProperty] private BypassState _state = new(BypassMode.Off, null, false, null);
    [ObservableProperty] private bool _isNotificationOpen;
    [ObservableProperty] private string _notificationTitle = "";
    [ObservableProperty] private string? _notificationMessage;
    [ObservableProperty] private InfoBarSeverity _notificationSeverity;

    public event Action<string>? NavigateRequested;

    public Func<string, Task>? SetClipboard { get; set; }

    public void NavigateTo(string page) => NavigateRequested?.Invoke(page);

    public void Notify(string title, string? message, InfoBarSeverity severity)
    {
        NotificationTitle = title;
        NotificationMessage = message;
        NotificationSeverity = severity;
        IsNotificationOpen = true;
        _notifyTimer.Stop();
        if (severity != InfoBarSeverity.Error) _notifyTimer.Start();
    }

    private bool _refreshing;

    public async Task RefreshStateAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            State = await Controller.GetStateAsync();
            Home.ApplyState(State);
        }
        catch (Exception ex)
        {
            Log.Warning($"Не удалось получить состояние: {ex.Message}");
        }
        finally
        {
            _refreshing = false;
        }
    }

    [RelayCommand]
    private Task ToggleBypassAsync() => Home.ToggleCommand.ExecuteAsync(null);

    public async Task InitializeAsync(bool startedFromAutostart)
    {
        Log.Info($"Zapret GUI {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)} • {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        if (OperatingSystem.IsWindows() && !IsAdmin)
            Log.Warning("Приложение запущено без прав администратора — запуск обхода, служба и DNS работать не будут");

        Home.ReloadStrategies();
        await RefreshStateAsync();
        await Home.RefreshTogglesAsync();

        if (Settings.StartBypassOnLaunch && !State.IsRunning && !State.ServiceInstalled && Home.SelectedStrategy != null)
        {
            Log.Info("Автоматическое включение обхода при запуске приложения");
            await Home.ToggleCommand.ExecuteAsync(null);
        }

        if (Settings.CheckUpdatesOnStart && !startedFromAutostart)
            _ = SettingsPage.CheckUpdatesQuietAsync();
    }
}
