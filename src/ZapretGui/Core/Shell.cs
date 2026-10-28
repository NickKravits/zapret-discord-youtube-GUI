using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ZapretGui.Core;

public sealed record ShellResult(int ExitCode, string Output)
{
    public bool Ok => ExitCode == 0;
}

public static class Shell
{
    static Shell()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding ConsoleEncoding
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return Encoding.UTF8;
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.UTF8; }
        }
    }

    public static async Task<ShellResult> RunAsync(
        string file,
        IEnumerable<string> args,
        bool log = true,
        Encoding? encoding = null,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var argList = args.ToList();
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding ?? ConsoleEncoding,
            StandardErrorEncoding = encoding ?? ConsoleEncoding,
        };
        if (workingDirectory != null) psi.WorkingDirectory = workingDirectory;
        foreach (var a in argList) psi.ArgumentList.Add(a);

        if (log) Log.Command($"> {file} {string.Join(' ', argList.Select(QuoteForDisplay))}");

        using var p = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        var sync = new object();
        void OnData(string? data, bool isError)
        {
            if (data == null) return;
            lock (sync) sb.AppendLine(data);
            if (log && !string.IsNullOrWhiteSpace(data))
                Log.Write(isError ? LogLevel.Warning : LogLevel.Output, data);
        }
        p.OutputDataReceived += (_, e) => OnData(e.Data, false);
        p.ErrorDataReceived += (_, e) => OnData(e.Data, true);

        try
        {
            p.Start();
        }
        catch (Exception ex)
        {
            if (log) Log.Error($"Не удалось запустить {file}: {ex.Message}");
            return new ShellResult(-1, ex.Message);
        }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            throw;
        }
        p.WaitForExit();
        lock (sync) return new ShellResult(p.ExitCode, sb.ToString());
    }

    public static Task<ShellResult> PowerShellAsync(string script, bool log = true, CancellationToken ct = default)
    {
        var full = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; " + script;
        if (log) Log.Command($"PS> {script}");
        return RunAsync("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", full],
            log: false, encoding: Encoding.UTF8, ct: ct)
            .ContinueWith(t =>
            {
                var r = t.Result;
                if (log)
                {
                    foreach (var line in r.Output.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0))
                        Log.Write(r.Ok ? LogLevel.Output : LogLevel.Warning, line);
                }
                return r;
            }, ct);
    }

    public static void OpenInShell(string pathOrUrl)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open", [pathOrUrl]) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Error($"Не удалось открыть {pathOrUrl}: {ex.Message}");
        }
    }

    public static string QuoteWindowsArg(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') sb.Append('\\', backslashes * 2 + 1).Append('"');
            else sb.Append('\\', backslashes).Append(c);
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    public static string QuoteSh(string arg) => "'" + arg.Replace("'", "'\\''") + "'";

    private static string QuoteForDisplay(string a) => a.Contains(' ') ? $"\"{a}\"" : a;
}
