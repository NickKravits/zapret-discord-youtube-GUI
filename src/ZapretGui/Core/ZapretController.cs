using System.Diagnostics;

namespace ZapretGui.Core;

public enum BypassMode { Off, Process, Service }

public sealed record BypassState(BypassMode Mode, string? Strategy, bool ServiceInstalled, string? ServiceStrategy)
{
    public bool IsRunning => Mode != BypassMode.Off;
}

public sealed class ZapretController
{
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private string? _processStrategy;

    public ZapretController(AppSettings settings)
    {
        _settings = settings;
        Install = new ZapretInstall(settings.ZapretDir ?? AppPaths.DefaultZapretDir);
    }

    public ZapretInstall Install { get; private set; }

    public event Action? StateChanged;

    public void SetZapretDir(string dir)
    {
        Install = new ZapretInstall(dir);
        _settings.ZapretDir = dir;
        _settings.Save();
        StateChanged?.Invoke();
    }

    public async Task<BypassState> GetStateAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            var installed = WindowsBackend.IsServiceInstalled;
            var svcStrategy = installed ? WindowsBackend.ServiceStrategy : null;
            if (installed && WindowsBackend.IsServiceRunning)
                return new BypassState(BypassMode.Service, svcStrategy, true, svcStrategy);
            if (_process is { HasExited: false })
                return new BypassState(BypassMode.Process, _processStrategy, installed, svcStrategy);
            return new BypassState(BypassMode.Off, null, installed, svcStrategy);
        }

        LinuxBackend.PumpSessionLog();
        var linInstalled = LinuxBackend.IsServiceInstalled;
        var linSvcStrategy = linInstalled ? LinuxBackend.ServiceStrategy : null;
        if (linInstalled && await LinuxBackend.IsServiceActiveAsync())
            return new BypassState(BypassMode.Service, linSvcStrategy, true, linSvcStrategy);
        if (LinuxBackend.IsSessionRunning(out var s))
            return new BypassState(BypassMode.Process, s ?? _processStrategy, linInstalled, linSvcStrategy);
        return new BypassState(BypassMode.Off, null, linInstalled, linSvcStrategy);
    }

    public async Task StartAsync(Strategy strategy)
    {
        await _gate.WaitAsync();
        try
        {
            _settings.LastStrategy = strategy.Name;
            _settings.Save();

            var state = await GetStateAsync();
            if (state.ServiceInstalled)
            {
                await StopProcessCoreAsync();
                if (!string.Equals(state.ServiceStrategy, strategy.Name, StringComparison.OrdinalIgnoreCase))
                    await InstallServiceCoreAsync(strategy);
                else if (OperatingSystem.IsWindows()) await WindowsBackend.StartServiceAsync();
                else await LinuxBackend.ServiceControlAsync("start");
            }
            else
            {
                await StartProcessCoreAsync(strategy);
            }
        }
        finally
        {
            _gate.Release();
            StateChanged?.Invoke();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopProcessCoreAsync();
            var state = await GetStateAsync();
            if (state.Mode == BypassMode.Service)
            {
                if (OperatingSystem.IsWindows()) await WindowsBackend.StopServiceAsync();
                else await LinuxBackend.ServiceControlAsync("stop");
            }
            Log.Info("Обход выключен");
        }
        finally
        {
            _gate.Release();
            StateChanged?.Invoke();
        }
    }

    public async Task RestartAsync()
    {
        var state = await GetStateAsync();
        if (!state.IsRunning || state.Strategy == null) return;
        var strategy = Install.FindStrategy(state.Strategy);
        if (strategy == null) return;
        if (state.Mode == BypassMode.Service)
        {
            await _gate.WaitAsync();
            try { await InstallServiceCoreAsync(strategy); }
            finally { _gate.Release(); StateChanged?.Invoke(); }
        }
        else await StartAsync(strategy);
    }

    public async Task StartProcessCoreAsync(Strategy strategy)
    {
        await StopProcessCoreAsync();
        var args = Install.BuildArgs(strategy);
        Log.Info($"Запуск стратегии «{strategy.Name}»");

        if (OperatingSystem.IsWindows())
        {
            if (WindowsBackend.IsServiceRunning)
                throw new InvalidOperationException("Обход уже работает как служба. Выключите автозапуск или остановите службу.");
            WindowsBackend.KillAllWinws();
            await WindowsBackend.EnsureTcpTimestampsAsync();

            var p = WindowsBackend.StartWinws(Install, args);
            p.Exited += (_, _) =>
            {
                if (!ReferenceEquals(_process, p)) return;
                int code;
                try { code = p.ExitCode; } catch { code = -1; }
                Log.Error($"winws.exe завершился (код {code}). Проверьте вывод выше.");
                _process = null;
                _processStrategy = null;
                StateChanged?.Invoke();
            };
            _process = p;
            _processStrategy = strategy.Name;

            await Task.Delay(800);
            if (p.HasExited)
                throw new InvalidOperationException($"winws.exe завершился сразу после запуска (код {p.ExitCode}). Подробности в консоли.");
            Log.Success($"Обход включён: «{strategy.Name}»");
        }
        else
        {
            var nfqws = LinuxBackend.FindNfqws(_settings)
                        ?? throw new InvalidOperationException("Не найден nfqws. Скачайте его в «Параметрах» или укажите путь.");
            _processStrategy = strategy.Name;
            await LinuxBackend.StartSessionAsync(Install, strategy, args, nfqws);
            if (!LinuxBackend.IsSessionRunning(out _))
                throw new InvalidOperationException("nfqws завершился сразу после запуска. Подробности в консоли.");
            Log.Success($"Обход включён: «{strategy.Name}»");
        }
    }

    public async Task StopProcessCoreAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            var p = _process;
            _process = null;
            _processStrategy = null;
            if (p is { HasExited: false })
            {
                try { p.Kill(true); await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch { }
            }
            p?.Dispose();
            if (!WindowsBackend.IsServiceRunning && WindowsBackend.AnyWinwsRunning()) WindowsBackend.KillAllWinws();
        }
        else if (LinuxBackend.IsSessionRunning(out _))
        {
            await LinuxBackend.StopSessionAsync();
            _processStrategy = null;
        }
    }

    public async Task InstallServiceAsync(Strategy strategy)
    {
        await _gate.WaitAsync();
        try
        {
            await StopProcessCoreAsync();
            await InstallServiceCoreAsync(strategy);
        }
        finally
        {
            _gate.Release();
            StateChanged?.Invoke();
        }
    }

    private async Task InstallServiceCoreAsync(Strategy strategy)
    {
        var args = Install.BuildArgs(strategy);
        if (OperatingSystem.IsWindows())
        {
            await WindowsBackend.InstallServiceAsync(Install, strategy, args);
        }
        else
        {
            var nfqws = LinuxBackend.FindNfqws(_settings)
                        ?? throw new InvalidOperationException("Не найден nfqws. Скачайте его в «Параметрах» или укажите путь.");
            await LinuxBackend.InstallServiceAsync(Install, strategy, args, nfqws);
        }
        _settings.LastStrategy = strategy.Name;
        _settings.Save();
    }

    public async Task RemoveServiceAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (OperatingSystem.IsWindows()) await WindowsBackend.RemoveServiceAsync();
            else await LinuxBackend.RemoveServiceAsync();
        }
        finally
        {
            _gate.Release();
            StateChanged?.Invoke();
        }
    }

    public async Task ShutdownAsync()
    {
        try { await StopProcessCoreAsync(); } catch { }
        if (!OperatingSystem.IsWindows()) RootShell.Shutdown();
    }
}
