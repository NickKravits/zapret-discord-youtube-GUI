using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ZapretGui.Core;

public static class LinuxBackend
{
    public const string InstallDir = "/opt/zapret-gui";
    public const string PidFile = "/run/zapret-gui.pid";
    public const string StrategyFile = "/run/zapret-gui.strategy";
    public const string SessionLog = "/run/zapret-gui.log";
    private static long _logPosition;
    public const string ServiceName = "zapret-gui";
    public const string UnitPath = "/etc/systemd/system/zapret-gui.service";
    private const int QueueNum = 200;

    public static string? FindNfqws(AppSettings settings)
    {
        var candidates = new List<string?>
        {
            settings.NfqwsPath,
            Path.Combine(AppPaths.DataDir, "nfqws"),
            Path.Combine(AppPaths.AppDir, "nfqws"),
            "/opt/zapret/nfq/nfqws",
            "/opt/zapret/binaries/linux-x86_64/nfqws",
            "/usr/bin/nfqws",
            "/usr/local/bin/nfqws",
            "/usr/sbin/nfqws",
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
    }

    public static List<string> ToNfqwsArgs(ZapretInstall z, IEnumerable<string> winwsArgs)
    {
        var root = z.Root.TrimEnd('/') + "/";
        var result = new List<string> { $"--qnum={QueueNum}" };
        foreach (var a in winwsArgs)
        {
            if (a.StartsWith("--wf-", StringComparison.Ordinal)) continue;
            result.Add(a.Replace(root, InstallDir + "/", StringComparison.Ordinal));
        }
        return result;
    }

    private static string NftRules(List<string> tcp, List<string> udp)
    {
        if (tcp.Count == 0) tcp = ["80", "443"];
        if (udp.Count == 0) udp = ["443"];
        var sb = new StringBuilder();
        sb.AppendLine("nft delete table inet zapretgui 2>/dev/null");
        sb.AppendLine("nft -f - <<'NFT'");
        sb.AppendLine("table inet zapretgui {");
        sb.AppendLine("  chain post {");
        sb.AppendLine("    type filter hook postrouting priority mangle; policy accept;");
        sb.AppendLine($"    meta mark and 0x40000000 == 0 tcp dport {{ {string.Join(", ", tcp)} }} ct original packets 1-6 queue num {QueueNum} bypass");
        sb.AppendLine($"    meta mark and 0x40000000 == 0 udp dport {{ {string.Join(", ", udp)} }} ct original packets 1-6 queue num {QueueNum} bypass");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("NFT");
        return sb.ToString();
    }

    private static string CopyDataScript(ZapretInstall z, string nfqws) => $$"""
        set -e
        command -v nft >/dev/null 2>&1 || { echo "Не найден nft (пакет nftables)"; exit 2; }
        modprobe nfnetlink_queue 2>/dev/null || true
        mkdir -p {{InstallDir}}
        rm -rf {{InstallDir}}/bin {{InstallDir}}/lists
        cp -r {{Shell.QuoteSh(z.BinDir)}} {{InstallDir}}/bin
        cp -r {{Shell.QuoteSh(z.ListsDir)}} {{InstallDir}}/lists
        cp -f {{Shell.QuoteSh(nfqws)}} {{InstallDir}}/nfqws.new && mv -f {{InstallDir}}/nfqws.new {{InstallDir}}/nfqws
        chmod 755 {{InstallDir}} {{InstallDir}}/nfqws
        chmod -R a+rX {{InstallDir}}/bin {{InstallDir}}/lists
        set +e
        """;

    private static string StopScript => $"""
        if [ -f {PidFile} ]; then
          PID=$(cat {PidFile} 2>/dev/null)
          [ -n "$PID" ] && kill "$PID" 2>/dev/null
          sleep 0.5
        fi
        nft delete table inet zapretgui 2>/dev/null
        rm -f {PidFile} {StrategyFile}
        true
        """;

    public static async Task StartSessionAsync(ZapretInstall z, Strategy s, List<string> winwsArgs, string nfqws)
    {
        var (tcp, udp) = StrategyParser.ExtractWindowsFilterPorts(winwsArgs);
        var args = string.Join(" ", ToNfqwsArgs(z, winwsArgs).Select(Shell.QuoteSh));
        var session = $"""
            #!/bin/sh
            {NftRules(tcp, udp)}
            {InstallDir}/nfqws {args} &
            PID=$!
            echo $PID > {PidFile}
            wait $PID
            CODE=$?
            nft delete table inet zapretgui 2>/dev/null
            rm -f {PidFile} {StrategyFile}
            echo "nfqws завершился (код $CODE)"
            """;

        var script = new StringBuilder();
        script.AppendLine(StopScript);
        script.AppendLine(CopyDataScript(z, nfqws));
        script.AppendLine($"cat > {InstallDir}/session.sh <<'ZAPRET_EOF'");
        script.AppendLine(session.Replace("\r", ""));
        script.AppendLine("ZAPRET_EOF");
        script.AppendLine($"printf '%s\\n' {Shell.QuoteSh(s.Name)} > {StrategyFile}");
        script.AppendLine($": > {SessionLog}; chmod 644 {SessionLog}");
        script.AppendLine($"nohup /bin/sh {InstallDir}/session.sh >>{SessionLog} 2>&1 &");
        script.AppendLine("sleep 1");
        script.AppendLine($"[ -f {PidFile} ] && kill -0 $(cat {PidFile}) 2>/dev/null && echo \"nfqws запущен (PID $(cat {PidFile}))\"");

        var r = await RootShell.RunAsync(script.ToString());
        if (!r.Ok) throw new InvalidOperationException("nfqws не запустился. Подробности в консоли.");
    }

    public static Task<ShellResult> StopSessionAsync() => RootShell.RunAsync(StopScript, log: false);

    public static void PumpSessionLog()
    {
        try
        {
            if (!File.Exists(SessionLog)) return;
            using var fs = new FileStream(SessionLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < _logPosition) _logPosition = 0;
            if (fs.Length == _logPosition) return;
            fs.Seek(_logPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var text = reader.ReadToEnd();
            _logPosition = fs.Length;
            foreach (var line in text.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
                Log.Output(line.TrimEnd('\r'));
        }
        catch { }
    }

    public static bool IsSessionRunning(out string? strategy)
    {
        strategy = null;
        try
        {
            if (!File.Exists(PidFile)) return false;
            var pid = File.ReadAllText(PidFile).Trim();
            if (pid.Length == 0 || !Directory.Exists($"/proc/{pid}")) return false;
            if (File.Exists(StrategyFile)) strategy = File.ReadAllText(StrategyFile).Trim();
            return true;
        }
        catch { return false; }
    }

    public static async Task InstallServiceAsync(ZapretInstall z, Strategy s, List<string> winwsArgs, string nfqws)
    {
        var (tcp, udp) = StrategyParser.ExtractWindowsFilterPorts(winwsArgs);
        var args = string.Join(" ", ToNfqwsArgs(z, winwsArgs).Select(Shell.QuoteSh));
        var service = $"""
            #!/bin/sh
            {NftRules(tcp, udp)}
            exec {InstallDir}/nfqws {args}
            """;
        var unit = $"""
            [Unit]
            Description=Zapret DPI bypass (zapret-gui)
            After=network-online.target
            Wants=network-online.target

            [Service]
            Type=simple
            ExecStart=/bin/sh {InstallDir}/service.sh
            ExecStopPost=/bin/sh -c 'nft delete table inet zapretgui 2>/dev/null; true'
            Restart=on-failure
            RestartSec=5

            [Install]
            WantedBy=multi-user.target
            """;

        var script = new StringBuilder();
        script.AppendLine(StopScript);
        script.AppendLine(CopyDataScript(z, nfqws));
        script.AppendLine($"cat > {InstallDir}/service.sh <<'ZAPRET_EOF'");
        script.AppendLine(service.Replace("\r", ""));
        script.AppendLine("ZAPRET_EOF");
        script.AppendLine($"printf '%s\\n' {Shell.QuoteSh(s.Name)} > {InstallDir}/strategy.txt");
        script.AppendLine($"cat > {UnitPath} <<'ZAPRET_EOF'");
        script.AppendLine(unit.Replace("\r", ""));
        script.AppendLine("ZAPRET_EOF");
        script.AppendLine("systemctl daemon-reload");
        script.AppendLine($"systemctl enable {ServiceName}.service");
        script.AppendLine($"systemctl restart {ServiceName}.service");
        script.AppendLine($"systemctl --no-pager status {ServiceName}.service | head -n 5");

        var r = await RootShell.RunAsync(script.ToString());
        if (!r.Ok) throw new InvalidOperationException("Не удалось установить службу systemd. Подробности в консоли.");
    }

    public static async Task RemoveServiceAsync()
    {
        await RootShell.RunAsync($"""
            systemctl disable --now {ServiceName}.service 2>/dev/null
            rm -f {UnitPath} {InstallDir}/strategy.txt
            systemctl daemon-reload
            nft delete table inet zapretgui 2>/dev/null
            true
            """);
    }

    public static Task<ShellResult> ServiceControlAsync(string verb) =>
        RootShell.RunAsync($"systemctl {verb} {ServiceName}.service");

    public static bool IsServiceInstalled => File.Exists(UnitPath);

    public static string? ServiceStrategy
    {
        get
        {
            var f = Path.Combine(InstallDir, "strategy.txt");
            try { return File.Exists(f) ? File.ReadAllText(f).Trim() : null; } catch { return null; }
        }
    }

    public static async Task<bool> IsServiceActiveAsync()
    {
        if (!IsServiceInstalled) return false;
        var r = await Shell.RunAsync("systemctl", ["is-active", $"{ServiceName}.service"], log: false);
        return r.Output.Trim() == "active";
    }

    private static string DesktopFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart", "zapret-gui.desktop");

    public static bool IsAppAutostartEnabled => File.Exists(DesktopFile);

    public static void SetAppAutostart(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(DesktopFile)) File.Delete(DesktopFile);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopFile)!);
        File.WriteAllText(DesktopFile, $"""
            [Desktop Entry]
            Type=Application
            Name=Zapret GUI
            Exec="{AppPaths.ExecutablePath}" --tray
            X-GNOME-Autostart-enabled=true
            Terminal=false
            """);
    }

    public static async Task<string> DownloadNfqwsAsync(IProgress<string>? progress = null)
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "linux-x86_64",
            Architecture.X86 => "linux-x86",
            Architecture.Arm64 => "linux-arm64",
            Architecture.Arm => "linux-arm",
            _ => throw new PlatformNotSupportedException("Неподдерживаемая архитектура"),
        };

        using var http = Http.CreateClient();
        progress?.Report("Поиск последнего релиза bol-van/zapret…");
        var json = await http.GetStringAsync("https://api.github.com/repos/bol-van/zapret/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
            .FirstOrDefault(a => a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) && !a.Name.Contains("openwrt", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(asset.Url)) throw new InvalidOperationException("В релизе не найден архив .tar.gz");

        progress?.Report($"Загрузка {asset.Name}…");
        Log.Info($"Загрузка {asset.Url}");
        await using var stream = await http.GetStreamAsync(asset.Url);
        await using var gz = new GZipStream(stream, CompressionMode.Decompress);
        using var tar = new TarReader(gz);
        var target = Path.Combine(AppPaths.DataDir, "nfqws");
        while (await tar.GetNextEntryAsync() is { } entry)
        {
            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile
                && entry.Name.EndsWith($"binaries/{arch}/nfqws", StringComparison.Ordinal))
            {
                await entry.ExtractToFileAsync(target, overwrite: true);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                             UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                             UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                Log.Success($"nfqws сохранён: {target}");
                return target;
            }
        }
        throw new InvalidOperationException($"В архиве нет binaries/{arch}/nfqws");
    }
}
