using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ZapretGui.Core;

public sealed record AdapterDns(int Index, string Name, string Description, IReadOnlyList<string> Servers)
{
    public string ServersText => Servers.Count == 0 ? "автоматически" : string.Join(", ", Servers);
}

public sealed class DnsManager(AppSettings settings)
{
    private const string ResolvedDropIn = "/etc/systemd/resolved.conf.d/zapret-gui-comss.conf";

    public static IReadOnlyList<AdapterDns> GetAdapters()
    {
        var result = new List<AdapterDns>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try
            {
                var props = ni.GetIPProperties();
                if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork)) continue;
                var index = props.GetIPv4Properties()?.Index ?? -1;
                var dns = props.DnsAddresses
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.ToString()).ToList();
                result.Add(new AdapterDns(index, ni.Name, ni.Description, dns));
            }
            catch { }
        }
        return result;
    }

    private IEnumerable<string> ComssAddresses => [settings.DnsPrimary, settings.DnsSecondary, settings.DohServer];

    public bool IsComssActive()
    {
        if (!OperatingSystem.IsWindows() && File.Exists(ResolvedDropIn)) return true;
        var adapters = GetAdapters();
        return adapters.Count > 0 && adapters.All(a => a.Servers.Count > 0 && a.Servers.All(s => ComssAddresses.Contains(s)));
    }

    public async Task EnableAsync(bool useDoh)
    {
        settings.DnsUseDoh = useDoh;
        if (OperatingSystem.IsWindows()) await EnableWindowsAsync(useDoh);
        else await EnableLinuxAsync();
        settings.Save();
    }

    public async Task DisableAsync()
    {
        if (OperatingSystem.IsWindows()) await DisableWindowsAsync();
        else await DisableLinuxAsync();
        settings.Save();
    }

    private async Task EnableWindowsAsync(bool useDoh)
    {
        var adapters = GetAdapters();
        if (adapters.Count == 0) throw new InvalidOperationException("Не найдено активных сетевых подключений");

        var servers = useDoh
            ? $"'{settings.DohServer}'"
            : $"'{settings.DnsPrimary}','{settings.DnsSecondary}'";

        if (useDoh)
        {
            var doh = $$"""
                $ip='{{settings.DohServer}}'; $t='{{settings.DohTemplate}}';
                if (-not (Get-Command Add-DnsClientDohServerAddress -ErrorAction SilentlyContinue)) { throw 'DNS-over-HTTPS поддерживается только в Windows 11' }
                if (Get-DnsClientDohServerAddress -ServerAddress $ip -ErrorAction SilentlyContinue) {
                  Set-DnsClientDohServerAddress -ServerAddress $ip -DohTemplate $t -AllowFallbackToUdp $true -AutoUpgrade $true | Out-Null
                } else {
                  Add-DnsClientDohServerAddress -ServerAddress $ip -DohTemplate $t -AllowFallbackToUdp $true -AutoUpgrade $true | Out-Null
                }
                'DoH-шаблон зарегистрирован: ' + $t
                """;
            var r = await Shell.PowerShellAsync(doh.Replace("\r\n", " ").Replace("\n", " "));
            if (!r.Ok) throw new InvalidOperationException("Не удалось настроить DNS-over-HTTPS");
        }

        foreach (var a in adapters)
        {
            var r = await Shell.PowerShellAsync($"Set-DnsClientServerAddress -InterfaceIndex {a.Index} -ServerAddresses @({servers}); 'DNS изменён: {a.Name.Replace("'", "''")}'");
            if (r.Ok && !settings.DnsChangedInterfaces.Contains(a.Index)) settings.DnsChangedInterfaces.Add(a.Index);
        }
        await Shell.RunAsync("ipconfig", ["/flushdns"]);
        Log.Success(useDoh ? "Comss DNS (DoH) подключён" : "Comss DNS подключён");
    }

    private async Task DisableWindowsAsync()
    {
        var indexes = settings.DnsChangedInterfaces
            .Union(GetAdapters().Where(a => a.Servers.Any(s => ComssAddresses.Contains(s))).Select(a => a.Index))
            .Distinct().ToList();
        foreach (var index in indexes)
            await Shell.PowerShellAsync($"Set-DnsClientServerAddress -InterfaceIndex {index} -ResetServerAddresses -ErrorAction SilentlyContinue; 'DNS сброшен на автоматический (интерфейс {index})'");
        settings.DnsChangedInterfaces.Clear();
        await Shell.RunAsync("ipconfig", ["/flushdns"]);
        Log.Success("DNS возвращён на автоматический (DHCP)");
    }

    private async Task EnableLinuxAsync()
    {
        var r = await RootShell.RunAsync($"""
            if ! command -v resolvectl >/dev/null 2>&1; then
              echo "systemd-resolved не найден. Укажите DNS {settings.DnsPrimary} и {settings.DnsSecondary} в настройках сети вручную."
              exit 3
            fi
            mkdir -p /etc/systemd/resolved.conf.d
            cat > {ResolvedDropIn} <<'EOF'
            [Resolve]
            DNS={settings.DnsPrimary} {settings.DnsSecondary}
            Domains=~.
            EOF
            systemctl restart systemd-resolved
            resolvectl flush-caches 2>/dev/null
            echo "Comss DNS подключён через systemd-resolved"
            """);
        if (!r.Ok) throw new InvalidOperationException("Не удалось подключить Comss DNS. Подробности в консоли.");
    }

    private static async Task DisableLinuxAsync()
    {
        await RootShell.RunAsync($"""
            rm -f {ResolvedDropIn}
            systemctl restart systemd-resolved 2>/dev/null
            resolvectl flush-caches 2>/dev/null
            echo "Comss DNS отключён"
            """);
    }
}
