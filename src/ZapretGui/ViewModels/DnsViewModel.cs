using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretGui.Core;

namespace ZapretGui.ViewModels;

public sealed partial class DnsViewModel(MainViewModel main) : PageViewModel(main)
{
    [ObservableProperty] private IReadOnlyList<AdapterDns> _adapters = [];
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private string _primary = "";
    [ObservableProperty] private string _secondary = "";
    [ObservableProperty] private string _dohServer = "";
    [ObservableProperty] private string _dohTemplate = "";

    public IReadOnlyList<string> Modes { get; } = OperatingSystem.IsWindows()
        ? ["Обычный DNS (UDP)", "DNS-over-HTTPS (Windows 11)"]
        : ["Обычный DNS (systemd-resolved)"];

    public bool IsDoh => ModeIndex == 1;

    partial void OnModeIndexChanged(int value) => OnPropertyChanged(nameof(IsDoh));

    public override async Task OnNavigatedToAsync()
    {
        Primary = Settings.DnsPrimary;
        Secondary = Settings.DnsSecondary;
        DohServer = Settings.DohServer;
        DohTemplate = Settings.DohTemplate;
        ModeIndex = IsWindows && Settings.DnsUseDoh ? 1 : 0;
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Adapters = await Task.Run(DnsManager.GetAdapters);
        IsActive = await Task.Run(Main.Dns.IsComssActive);
        StatusText = IsActive ? "Comss DNS подключён" : "Comss DNS не подключён";
        Main.Home.SetComssToggleSilently(IsActive);
    }

    private void SaveServers()
    {
        Settings.DnsPrimary = Primary.Trim();
        Settings.DnsSecondary = Secondary.Trim();
        Settings.DohServer = DohServer.Trim();
        Settings.DohTemplate = DohTemplate.Trim();
        Settings.Save();
    }

    [RelayCommand]
    private async Task EnableAsync()
    {
        SaveServers();
        await GuardAsync(() => Main.Dns.EnableAsync(IsDoh), "Comss DNS подключён");
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DisableAsync()
    {
        await GuardAsync(Main.Dns.DisableAsync, "DNS возвращён на автоматический");
        await RefreshAsync();
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        var d = new AppSettings();
        Primary = d.DnsPrimary;
        Secondary = d.DnsSecondary;
        DohServer = d.DohServer;
        DohTemplate = d.DohTemplate;
        SaveServers();
    }

    [RelayCommand]
    private void OpenComss() => Shell.OpenInShell("https://www.comss.ru/page.php?id=7315");
}
