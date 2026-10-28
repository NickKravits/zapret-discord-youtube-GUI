using System.Text;
using System.Text.RegularExpressions;

namespace ZapretGui.Core;

public sealed record Strategy(string Name, string FilePath)
{
    public override string ToString() => Name;
}

public static partial class StrategyParser
{
    public static List<string> Parse(string batPath, IReadOnlyDictionary<string, string> vars)
    {
        var lines = File.ReadAllLines(batPath);
        var start = Array.FindIndex(lines, l => l.Contains("winws.exe", StringComparison.OrdinalIgnoreCase));
        if (start < 0) throw new FormatException($"В «{Path.GetFileName(batPath)}» не найден запуск winws.exe");

        var sb = new StringBuilder();
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var continues = line.EndsWith('^');
            if (continues) line = line[..^1];
            sb.Append(line).Append(' ');
            if (!continues) break;
        }

        var cmd = sb.ToString();
        var idx = cmd.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase) + "winws.exe".Length;
        if (idx < cmd.Length && cmd[idx] == '"') idx++;
        cmd = Expand(cmd[idx..], vars);

        var args = Tokenize(cmd);
        var unknown = args.SelectMany(a => VarRegex().Matches(a)).Select(m => m.Value).Distinct().ToList();
        if (unknown.Count > 0)
            Log.Warning($"Неизвестные переменные в стратегии: {string.Join(", ", unknown)}");
        return args;
    }

    private static string Expand(string s, IReadOnlyDictionary<string, string> vars)
    {
        if (vars.TryGetValue("~dp0", out var dp0)) s = s.Replace("%~dp0", dp0, StringComparison.OrdinalIgnoreCase);
        s = VarRegex().Replace(s, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        return s.Replace("%%", "%");
    }

    public static List<string> Tokenize(string s)
    {
        var result = new List<string>();
        var cur = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"') { inQuotes = !inQuotes; hasToken = true; continue; }
            if (c == '^' && !inQuotes && i + 1 < s.Length) { cur.Append(s[++i]); hasToken = true; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken) { result.Add(cur.ToString()); cur.Clear(); hasToken = false; }
                continue;
            }
            cur.Append(c);
            hasToken = true;
        }
        if (hasToken) result.Add(cur.ToString());
        return result;
    }

    public static List<string> InjectAfter(List<string> args, Func<string, bool> match, string extra)
    {
        var result = new List<string>(args.Count + 4);
        foreach (var a in args)
        {
            result.Add(a);
            if (match(a)) result.Add(extra);
        }
        return result;
    }

    public static (List<string> Tcp, List<string> Udp) ExtractWindowsFilterPorts(IEnumerable<string> args)
    {
        var tcp = new List<string>();
        var udp = new List<string>();
        foreach (var a in args)
        {
            if (a.StartsWith("--wf-tcp=", StringComparison.Ordinal)) tcp.AddRange(SplitPorts(a["--wf-tcp=".Length..]));
            else if (a.StartsWith("--wf-udp=", StringComparison.Ordinal)) udp.AddRange(SplitPorts(a["--wf-udp=".Length..]));
        }
        return (tcp.Distinct().ToList(), udp.Distinct().ToList());
    }

    private static IEnumerable<string> SplitPorts(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Where(p => PortRegex().IsMatch(p));

    [GeneratedRegex(@"%([A-Za-z_][A-Za-z0-9_]*)%")]
    private static partial Regex VarRegex();

    [GeneratedRegex(@"^\d{1,5}(-\d{1,5})?$")]
    private static partial Regex PortRegex();
}
