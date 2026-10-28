using System.Text.Json;

namespace ZapretGui.Core;

public sealed class AppSettings
{
    public string? ZapretDir { get; set; }
    public string? LastStrategy { get; set; }
    public bool StartBypassOnLaunch { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public string Theme { get; set; } = "System";
    public string? NfqwsPath { get; set; }
    public bool CheckUpdatesOnStart { get; set; } = true;

    public string DnsPrimary { get; set; } = "83.220.169.155";
    public string DnsSecondary { get; set; } = "212.109.195.93";
    public string DohServer { get; set; } = "195.133.25.16";
    public string DohTemplate { get; set; } = "https://dns.comss.one/dns-query";
    public bool DnsUseDoh { get; set; }
    public List<int> DnsChangedInterfaces { get; set; } = new();

    public int TestTimeoutSeconds { get; set; } = 5;
    public List<string> TestSelection { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Warning($"Не удалось прочитать настройки: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Warning($"Не удалось сохранить настройки: {ex.Message}");
        }
    }
}
