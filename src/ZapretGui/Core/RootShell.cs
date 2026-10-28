using System.Diagnostics;
using System.Text;

namespace ZapretGui.Core;

public static class RootShell
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Process? _shell;
    private static TaskCompletionSource<int>? _pending;
    private static string? _pendingMarker;
    private static StringBuilder? _pendingOutput;
    private static bool _pendingLog;

    public static bool IsRoot => Environment.IsPrivilegedProcess;

    public static async Task<ShellResult> RunAsync(string script, bool log = true, TimeSpan? timeout = null)
    {
        await Gate.WaitAsync();
        try
        {
            EnsureShell();
            var marker = "__ZAPRET_GUI_DONE_" + Guid.NewGuid().ToString("N");
            var output = new StringBuilder();
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (typeof(RootShell))
            {
                _pending = tcs;
                _pendingMarker = marker;
                _pendingOutput = output;
                _pendingLog = log;
            }

            var file = Path.Combine(Path.GetTempPath(), $"zapret-gui-{Guid.NewGuid():N}.sh");
            await File.WriteAllTextAsync(file, script.Replace("\r\n", "\n"));
            await _shell!.StandardInput.WriteAsync($"/bin/sh {Shell.QuoteSh(file)} </dev/null 2>&1; echo \"{marker}:$?\"; rm -f {Shell.QuoteSh(file)}\n");
            await _shell.StandardInput.FlushAsync();

            var finished = await Task.WhenAny(tcs.Task, Task.Delay(timeout ?? TimeSpan.FromMinutes(5)));
            if (finished != tcs.Task) throw new TimeoutException("Команда с правами root не ответила вовремя");
            var code = await tcs.Task;
            return new ShellResult(code, output.ToString());
        }
        finally
        {
            lock (typeof(RootShell)) { _pending = null; _pendingMarker = null; _pendingOutput = null; }
            Gate.Release();
        }
    }

    private static void EnsureShell()
    {
        if (_shell is { HasExited: false }) return;

        var psi = IsRoot
            ? new ProcessStartInfo("/bin/sh")
            : new ProcessStartInfo("pkexec", ["/bin/sh"]);
        psi.UseShellExecute = false;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;

        if (!IsRoot) Log.Info("Запрос прав администратора (pkexec)…");
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => OnLine(e.Data, false);
        p.ErrorDataReceived += (_, e) => OnLine(e.Data, true);
        p.Exited += (_, _) =>
        {
            int code;
            try { code = p.ExitCode; } catch { code = -1; }
            if (code == 126 || code == 127) Log.Error("Права администратора не получены (pkexec отменён или недоступен)");
            TaskCompletionSource<int>? pending;
            lock (typeof(RootShell)) pending = _pending;
            pending?.TrySetException(new InvalidOperationException("Не удалось получить права root (pkexec)"));
        };
        try
        {
            p.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Не удалось запустить pkexec: {ex.Message}. Установите polkit или запустите приложение через sudo.");
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _shell = p;
    }

    private static void OnLine(string? line, bool isError)
    {
        if (line == null) return;
        TaskCompletionSource<int>? pending;
        string? marker;
        StringBuilder? output;
        bool log;
        lock (typeof(RootShell))
        {
            pending = _pending; marker = _pendingMarker; output = _pendingOutput; log = _pendingLog;
        }

        if (marker != null && line.StartsWith(marker + ":", StringComparison.Ordinal))
        {
            int.TryParse(line.AsSpan(marker.Length + 1), out var code);
            pending?.TrySetResult(code);
            return;
        }

        output?.AppendLine(line);
        if (pending == null || log)
        {
            if (!string.IsNullOrWhiteSpace(line))
                Log.Write(isError ? LogLevel.Warning : LogLevel.Output, line);
        }
    }

    public static void Shutdown()
    {
        try
        {
            if (_shell is { HasExited: false })
            {
                _shell.StandardInput.WriteLine("exit");
                _shell.StandardInput.Close();
                _shell.WaitForExit(2000);
            }
        }
        catch { }
    }
}
