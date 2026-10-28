using System.Text.RegularExpressions;

namespace ZapretGui.Core;

public enum IpsetMode { Loaded, None, Any }

public sealed partial class ZapretInstall(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string BinDir => Path.Combine(Root, "bin");
    public string ListsDir => Path.Combine(Root, "lists");
    public string UtilsDir => Path.Combine(Root, "utils");
    public string WinwsPath => Path.Combine(BinDir, "winws.exe");
    public string GameFilterFile => Path.Combine(UtilsDir, "game_filter.enabled");
    public string TargetsFile => Path.Combine(UtilsDir, "targets.txt");
    public string IpsetAllFile => Path.Combine(ListsDir, ListCatalog.IpsetAll);
    public string IpsetBackupFile => IpsetAllFile + ".backup";

    public bool Exists =>
        Directory.Exists(BinDir) && Directory.Exists(ListsDir) && Directory.EnumerateFiles(Root, "general*.bat").Any();

    public IReadOnlyList<Strategy> GetStrategies()
    {
        if (!Directory.Exists(Root)) return [];
        return Directory.EnumerateFiles(Root, "*.bat")
            .Where(f => !Path.GetFileName(f).StartsWith("service", StringComparison.OrdinalIgnoreCase))
            .Select(f => new Strategy(Path.GetFileNameWithoutExtension(f), f))
            .OrderBy(s => NaturalKey(s.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Strategy? FindStrategy(string? name) =>
        name == null ? null : GetStrategies().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public string? LocalVersion
    {
        get
        {
            var file = Path.Combine(Root, "service.bat");
            if (!File.Exists(file)) return null;
            var m = VersionRegex().Match(File.ReadAllText(file));
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
    }

    public GameFilterSettings LoadGameFilter() => GameFilterSettings.Load(GameFilterFile);

    public string ListPath(string fileName) => Path.Combine(ListsDir, fileName);

    public List<string> BuildArgs(Strategy strategy)
    {
        ListStore.EnsureUserLists(ListsDir);
        var gf = LoadGameFilter();
        var sep = Path.DirectorySeparatorChar.ToString();
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["~dp0"] = Root + sep,
            ["BIN"] = BinDir + sep,
            ["LISTS"] = ListsDir + sep,
            ["GameFilter"] = gf.Combined,
            ["GameFilterTCP"] = gf.TcpPorts,
            ["GameFilterUDP"] = gf.UdpPorts,
        };
        var args = StrategyParser.Parse(strategy.FilePath, vars);

        if (GetIpsetMode() != IpsetMode.Any)
        {
            args = StrategyParser.InjectAfter(args,
                a => a.StartsWith("--ipset=", StringComparison.Ordinal) && a.EndsWith(ListCatalog.IpsetAll, StringComparison.OrdinalIgnoreCase),
                "--ipset=" + ListPath(ListCatalog.IpsetUser));
        }
        return args;
    }

    public IpsetMode GetIpsetMode()
    {
        if (!File.Exists(IpsetAllFile)) return IpsetMode.Any;
        var lines = File.ReadLines(IpsetAllFile).Select(l => l.Trim()).Where(l => l.Length > 0).Take(2).ToList();
        if (lines.Count == 0) return IpsetMode.Any;
        return File.ReadLines(IpsetAllFile).Any(l => l.Trim() == ListCatalog.IpPlaceholder) ? IpsetMode.None : IpsetMode.Loaded;
    }

    public void SetIpsetMode(IpsetMode target)
    {
        var current = GetIpsetMode();
        if (current == target) return;

        if (current == IpsetMode.Loaded)
        {
            File.Copy(IpsetAllFile, IpsetBackupFile, overwrite: true);
        }

        switch (target)
        {
            case IpsetMode.None:
                File.WriteAllText(IpsetAllFile, ListCatalog.IpPlaceholder + "\n");
                break;
            case IpsetMode.Any:
                File.WriteAllText(IpsetAllFile, "");
                break;
            case IpsetMode.Loaded:
                if (!File.Exists(IpsetBackupFile))
                    throw new InvalidOperationException("Нет сохранённого списка IPSet. Сначала обновите список IPSet.");
                File.Copy(IpsetBackupFile, IpsetAllFile, overwrite: true);
                break;
        }
        Log.Info($"Режим IPSet: {IpsetModeText(target)}");
    }

    public static string IpsetModeText(IpsetMode m) => m switch
    {
        IpsetMode.Loaded => "loaded — по списку IP",
        IpsetMode.None => "none — выключен",
        _ => "any — все IP",
    };

    private static string NaturalKey(string s) => NumberRegex().Replace(s, m => m.Value.PadLeft(8, '0'));

    [GeneratedRegex(@"LOCAL_VERSION=([^""\r\n]+)")]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();
}
