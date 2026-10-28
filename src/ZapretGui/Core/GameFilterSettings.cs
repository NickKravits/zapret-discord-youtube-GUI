namespace ZapretGui.Core;

public sealed class GameFilterSettings
{
    public const string DefaultRange = "1024-65535";
    private const string DisabledPort = "12";

    public string Mode { get; set; } = "disabled";
    public string TcpRange { get; set; } = DefaultRange;
    public string UdpRange { get; set; } = DefaultRange;

    public bool TcpEnabled => Mode is "all" or "tcp";
    public bool UdpEnabled => Mode is "all" or "udp";

    public string TcpPorts => TcpEnabled ? TcpRange : DisabledPort;
    public string UdpPorts => UdpEnabled ? UdpRange : DisabledPort;
    public string Combined => Mode switch
    {
        "all" or "tcp" => TcpRange,
        "udp" => UdpRange,
        _ => DisabledPort,
    };

    public static GameFilterSettings Load(string file)
    {
        var s = new GameFilterSettings();
        if (!File.Exists(file)) return s;

        string? tcp = null, udp = null;
        foreach (var raw in File.ReadAllLines(file))
        {
            var parts = raw.Split('=', 2);
            var key = parts[0].Trim().ToLowerInvariant();
            var value = parts.Length > 1 ? parts[1].Trim() : "";
            switch (key)
            {
                case "mode": s.Mode = value.ToLowerInvariant(); break;
                case "all": s.Mode = "all"; break;
                case "tcp": if (value.Length == 0) s.Mode = "tcp"; else tcp = value; break;
                case "udp": if (value.Length == 0) s.Mode = "udp"; else udp = value; break;
            }
        }
        if (tcp != null && IsValidRange(tcp)) s.TcpRange = tcp;
        if (udp != null && IsValidRange(udp)) s.UdpRange = udp;
        if (s.Mode is not ("all" or "tcp" or "udp")) s.Mode = "disabled";
        return s;
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, $"mode={Mode}\r\ntcp={TcpRange}\r\nudp={UdpRange}\r\n");
    }

    public static bool IsValidRange(string value)
    {
        value = value.Replace(" ", "");
        if (value.Length == 0) return false;
        foreach (var item in value.Split(','))
        {
            var bounds = item.Split('-');
            if (bounds.Length > 2) return false;
            if (!int.TryParse(bounds[0], out var start) || start < 1 || start > 65535) return false;
            var end = start;
            if (bounds.Length == 2 && (!int.TryParse(bounds[1], out end) || end < 1 || end > 65535)) return false;
            if (start > end) return false;
        }
        return true;
    }
}
