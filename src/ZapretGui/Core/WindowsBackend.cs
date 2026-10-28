using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Win32;

namespace ZapretGui.Core;

[SupportedOSPlatform("windows")]
public static class WindowsBackend
{
    public const string ServiceName = "zapret";
    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\zapret";
    private const string StrategyValue = "zapret-discord-youtube";
    private const string TaskName = "ZapretGUI";
    private static bool _timestampsChecked;

    public static Process StartWinws(ZapretInstall z, IReadOnlyList<string> args)
    {
        if (!File.Exists(z.WinwsPath)) throw new FileNotFoundException("Не найден winws.exe", z.WinwsPath);
        var psi = new ProcessStartInfo(z.WinwsPath)
        {
            WorkingDirectory = z.BinDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Shell.ConsoleEncoding,
            StandardErrorEncoding = Shell.ConsoleEncoding,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Log.Command("> winws.exe " + string.Join(' ', args.Select(Shell.QuoteWindowsArg)));
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log.Output(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log.Warning(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    public static bool AnyWinwsRunning()
    {
        var list = Process.GetProcessesByName("winws");
        foreach (var p in list) p.Dispose();
        return list.Length > 0;
    }

    public static void KillAllWinws()
    {
        foreach (var p in Process.GetProcessesByName("winws"))
        {
            try
            {
                p.Kill(true);
                p.WaitForExit(3000);
                Log.Info($"Остановлен winws.exe (PID {p.Id})");
            }
            catch (Exception ex) { Log.Warning($"Не удалось завершить winws.exe (PID {p.Id}): {ex.Message}"); }
            finally { p.Dispose(); }
        }
    }

    public static async Task EnsureTcpTimestampsAsync()
    {
        if (_timestampsChecked) return;
        _timestampsChecked = true;
        var r = await Shell.RunAsync("netsh", ["interface", "tcp", "show", "global"], log: false);
        var line = r.Output.Split('\n').FirstOrDefault(l => l.Contains("timestamps", StringComparison.OrdinalIgnoreCase)
                                                         || l.Contains("отметки времени", StringComparison.OrdinalIgnoreCase));
        if (line != null && (line.Contains("enabled", StringComparison.OrdinalIgnoreCase) || line.Contains("включ", StringComparison.OrdinalIgnoreCase)))
            return;
        Log.Info("Включение TCP timestamps…");
        await Shell.RunAsync("netsh", ["interface", "tcp", "set", "global", "timestamps=enabled"]);
    }

    public static ServiceControllerStatus? GetServiceStatus(string name = ServiceName)
    {
        try
        {
            using var sc = new ServiceController(name);
            return sc.Status;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static bool IsServiceInstalled => GetServiceStatus() != null;
    public static bool IsServiceRunning => GetServiceStatus() == ServiceControllerStatus.Running;

    public static string? ServiceStrategy
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(ServiceKey);
                return key?.GetValue(StrategyValue) as string;
            }
            catch { return null; }
        }
    }

    public static async Task InstallServiceAsync(ZapretInstall z, Strategy s, IReadOnlyList<string> args)
    {
        if (!File.Exists(z.WinwsPath)) throw new FileNotFoundException("Не найден winws.exe", z.WinwsPath);
        await EnsureTcpTimestampsAsync();
        await StopServiceAsync();
        if (IsServiceInstalled) await Shell.RunAsync("sc.exe", ["delete", ServiceName]);

        var binPath = Shell.QuoteWindowsArg(z.WinwsPath) + " " + string.Join(' ', args.Select(Shell.QuoteWindowsArg));
        var r = await Shell.RunAsync("sc.exe", ["create", ServiceName, "binPath=", binPath, "DisplayName=", "zapret", "start=", "auto"]);
        if (!r.Ok) throw new InvalidOperationException($"sc create завершился с кодом {r.ExitCode}");
        await Shell.RunAsync("sc.exe", ["description", ServiceName, "Zapret DPI bypass software"], log: false);

        using (var key = Registry.LocalMachine.OpenSubKey(ServiceKey, writable: true))
            key?.SetValue(StrategyValue, s.Name, RegistryValueKind.String);

        await StartServiceAsync();
        Log.Success($"Служба zapret установлена (стратегия «{s.Name}»), автозапуск включён");
    }

    public static async Task StartServiceAsync()
    {
        await Task.Run(() =>
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running) return;
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
        });
        Log.Success("Служба zapret запущена");
    }

    public static async Task StopServiceAsync(string name = ServiceName)
    {
        await Task.Run(() =>
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status is ServiceControllerStatus.Stopped) return;
                if (sc.CanStop) sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                Log.Info($"Служба {name} остановлена");
            }
            catch (InvalidOperationException) { }
            catch (System.ServiceProcess.TimeoutException) { Log.Warning($"Служба {name} не остановилась за 15 секунд"); }
        });
    }

    public static async Task RemoveServiceAsync()
    {
        if (IsServiceInstalled)
        {
            await StopServiceAsync();
            await Shell.RunAsync("sc.exe", ["delete", ServiceName]);
        }
        KillAllWinws();
        await RemoveWinDivertAsync();
        Log.Success("Служба zapret удалена");
    }

    public static async Task RemoveWinDivertAsync()
    {
        foreach (var name in new[] { "WinDivert", "WinDivert14" })
        {
            if (GetServiceStatus(name) == null) continue;
            await StopServiceAsync(name);
            await Shell.RunAsync("sc.exe", ["delete", name], log: false);
        }
    }

    public static async Task<bool> IsAppAutostartEnabledAsync()
    {
        var r = await Shell.RunAsync("schtasks.exe", ["/Query", "/TN", TaskName], log: false);
        return r.Ok;
    }

    public static async Task SetAppAutostartAsync(bool enabled)
    {
        if (!enabled)
        {
            await Shell.RunAsync("schtasks.exe", ["/Delete", "/TN", TaskName, "/F"]);
            return;
        }
        var exe = AppPaths.ExecutablePath.Replace("'", "''");
        var script = $"""
            $a = New-ScheduledTaskAction -Execute '{exe}' -Argument '--tray'
            $t = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
            $p = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -RunLevel Highest -LogonType Interactive
            $s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
            Register-ScheduledTask -TaskName '{TaskName}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null
            'Задача автозапуска создана'
            """;
        var r = await Shell.PowerShellAsync(script.Replace("\r\n", "; ").Replace("\n", "; "));
        if (!r.Ok) throw new InvalidOperationException("Не удалось создать задачу автозапуска");
    }
}
