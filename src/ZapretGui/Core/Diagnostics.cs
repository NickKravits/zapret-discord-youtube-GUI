using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Win32;

namespace ZapretGui.Core;

public static class Diagnostics
{
    public static async Task RunAsync(ZapretInstall z)
    {
        Log.Info("===== Диагностика =====");
        Log.Info($"Папка zapret: {z.Root}");
        Log.Write(z.Exists ? LogLevel.Success : LogLevel.Error, z.Exists ? "Файлы zapret найдены" : "[X] Файлы zapret не найдены");

        if (OperatingSystem.IsWindows()) await RunWindowsAsync(z);
        else await RunLinuxAsync();
        Log.Info("===== Диагностика завершена =====");
    }

    [SupportedOSPlatform("windows")]
    private static async Task RunWindowsAsync(ZapretInstall z)
    {
        Check(Environment.IsPrivilegedProcess, "Приложение запущено с правами администратора", "[X] Нет прав администратора — запустите приложение от имени администратора");
        Check(File.Exists(Path.Combine(z.BinDir, "WinDivert64.sys")), "WinDivert64.sys найден", "[X] Не найден bin\\WinDivert64.sys (возможно, удалён антивирусом)");
        Check(WindowsBackend.GetServiceStatus("BFE") == ServiceControllerStatus.Running,
            "Base Filtering Engine работает", "[X] Служба Base Filtering Engine не запущена — она нужна для работы zapret");

        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
        {
            if (key?.GetValue("ProxyEnable") is int enabled && enabled == 1)
                Log.Warning($"[?] Включён системный прокси: {key.GetValue("ProxyServer")}. Убедитесь, что он нужен");
            else Log.Success("Системный прокси не используется");
        }

        var services = await GetServicesAsync();
        bool Has(params string[] words) => services.Any(s => words.All(w => s.Contains(w, StringComparison.OrdinalIgnoreCase)));

        Check(!IsProcessRunning("AdguardSvc"), "Adguard не найден", "[X] Найден Adguard — может мешать работе Discord");
        Check(!Has("Killer"), "Killer не найден", "[X] Найдены службы Killer — конфликтуют с zapret");
        Check(!Has("Intel", "Connectivity", "Network"), "Intel Connectivity не найден", "[X] Найдена Intel Connectivity Network Service — конфликтует с zapret");
        Check(!Has("TracSrvWrapper") && !Has("EPWD"), "Check Point не найден", "[X] Найдены службы Check Point — конфликтуют с zapret");
        Check(!Has("SmartByte"), "SmartByte не найден", "[X] Найдены службы SmartByte — конфликтуют с zapret");

        var vpn = services.Where(s => s.Contains("VPN", StringComparison.OrdinalIgnoreCase) || s.Contains("HappService", StringComparison.OrdinalIgnoreCase)).ToList();
        if (vpn.Count > 0) Log.Warning($"[?] Найдены VPN-службы: {string.Join(", ", vpn)}. Некоторые VPN конфликтуют с zapret");
        else Log.Success("VPN-службы не найдены");

        if (z.Root.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё'))
            Log.Warning("[?] В пути к zapret есть кириллица. Если обход не работает, перенесите папку, например в C:\\zapret");
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrive) && z.Root.StartsWith(oneDrive, StringComparison.OrdinalIgnoreCase))
            Log.Error("[X] zapret находится в папке OneDrive — перенесите его, например в C:\\zapret");

        var conflicts = new[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2" }
            .Where(n => WindowsBackend.GetServiceStatus(n) != null).ToList();
        Check(conflicts.Count == 0, "Конфликтующие службы обхода не найдены",
            $"[X] Найдены конфликтующие службы: {string.Join(", ", conflicts)}. Удалите их (sc delete <имя>)");

        if (!WindowsBackend.AnyWinwsRunning() && WindowsBackend.GetServiceStatus("WinDivert") is ServiceControllerStatus.Running or ServiceControllerStatus.StopPending)
        {
            Log.Warning("[?] winws.exe не запущен, но служба WinDivert активна. Удаление WinDivert…");
            await WindowsBackend.RemoveWinDivertAsync();
        }

        var hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
        if (File.Exists(hosts))
        {
            var text = await File.ReadAllTextAsync(hosts);
            if (text.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || text.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                Log.Warning("[?] В файле hosts есть записи youtube.com/youtu.be — это может мешать YouTube");
        }

        var doh = await Shell.PowerShellAsync(
            "(Get-ChildItem -Recurse -Path 'HKLM:System\\CurrentControlSet\\Services\\Dnscache\\InterfaceSpecificParameters\\' -ErrorAction SilentlyContinue | Get-ItemProperty | Where-Object { $_.DohFlags -gt 0 } | Measure-Object).Count",
            log: false);
        if (int.TryParse(doh.Output.Trim(), out var dohCount) && dohCount > 0) Log.Success("Зашифрованный DNS настроен");
        else Log.Warning("[?] Зашифрованный DNS не настроен. Включите Comss DNS (DoH) на странице «DNS» или настройте DoH в браузере");
    }

    private static async Task RunLinuxAsync()
    {
        var nft = await Shell.RunAsync("/bin/sh", ["-c", "command -v nft"], log: false);
        Check(nft.Ok, "nftables найден", "[X] Не найден nft — установите пакет nftables");
        var pk = await Shell.RunAsync("/bin/sh", ["-c", "command -v pkexec"], log: false);
        Check(pk.Ok || Environment.IsPrivilegedProcess, "pkexec найден", "[X] Не найден pkexec — установите polkit или запускайте приложение через sudo");
        var resolved = await Shell.RunAsync("/bin/sh", ["-c", "command -v resolvectl"], log: false);
        if (!resolved.Ok) Log.Warning("[?] systemd-resolved не найден — Comss DNS придётся указать в настройках сети вручную");
        var mod = await Shell.RunAsync("/bin/sh", ["-c", "lsmod | grep -q nfnetlink_queue || modinfo nfnetlink_queue >/dev/null 2>&1"], log: false);
        Check(mod.Ok, "Модуль ядра nfnetlink_queue доступен", "[X] Модуль nfnetlink_queue не найден — nfqws не сможет работать");
    }

    private static void Check(bool ok, string okText, string failText)
    {
        if (ok) Log.Success(okText);
        else Log.Error(failText);
    }

    private static bool IsProcessRunning(string name)
    {
        var list = Process.GetProcessesByName(name);
        foreach (var p in list) p.Dispose();
        return list.Length > 0;
    }

    [SupportedOSPlatform("windows")]
    private static Task<List<string>> GetServicesAsync() => Task.Run(() =>
    {
        try
        {
            return ServiceController.GetServices()
                .Select(s => { using (s) return $"{s.ServiceName} ({s.DisplayName})"; })
                .ToList();
        }
        catch { return new List<string>(); }
    });
}
