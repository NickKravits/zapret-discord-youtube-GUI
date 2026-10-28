using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretGui.Core;

public enum ListKind { Domain, Ip }

public sealed record ListInfo(string FileName, string Title, string Description, ListKind Kind, bool IsUser)
{
    public string Placeholder => Kind == ListKind.Domain ? ListCatalog.DomainPlaceholder : ListCatalog.IpPlaceholder;
    public string KindText => Kind == ListKind.Domain ? "Домены" : "IP-адреса";
    public override string ToString() => Title;
}

public static class ListCatalog
{
    public const string GeneralUser = "list-general-user.txt";
    public const string ExcludeUser = "list-exclude-user.txt";
    public const string IpsetUser = "ipset-all-user.txt";
    public const string IpsetExcludeUser = "ipset-exclude-user.txt";
    public const string IpsetAll = "ipset-all.txt";

    public const string DomainPlaceholder = "domain.example.abc";
    public const string IpPlaceholder = "203.0.113.113/32";

    public static readonly IReadOnlyList<ListInfo> All =
    [
        new(GeneralUser, "Мои домены", "Домены, для которых включается обход блокировок", ListKind.Domain, true),
        new(IpsetUser, "Мои IP-адреса", "IP-адреса и подсети для обхода", ListKind.Ip, true),
        new(ExcludeUser, "Мои исключения — домены", "Домены, которые обход не трогает", ListKind.Domain, true),
        new(IpsetExcludeUser, "Мои исключения — IP", "IP-адреса и подсети, которые обход не трогает", ListKind.Ip, true),
        new("list-general.txt", "Общий список доменов", "Встроенный список zapret (Discord, YouTube и др.)", ListKind.Domain, false),
        new("list-google.txt", "Домены Google", "Встроенный список для YouTube/Google", ListKind.Domain, false),
        new("list-exclude.txt", "Исключения — домены", "Встроенный список исключений", ListKind.Domain, false),
        new(IpsetAll, "IPSet — все IP", "Встроенный список IP (большой, меняется режимом IPSet)", ListKind.Ip, false),
        new("ipset-exclude.txt", "Исключения — IP", "Встроенный список IP-исключений", ListKind.Ip, false),
    ];

    public static ListInfo Get(string fileName) => All.First(l => l.FileName == fileName);
}

public static class ListStore
{
    public static List<string> ReadEntries(string path, ListInfo info)
    {
        if (!File.Exists(path)) return new List<string>();
        return File.ReadLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Where(l => !info.IsUser || !string.Equals(l, info.Placeholder, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static void WriteEntries(string path, ListInfo info, IEnumerable<string> entries)
    {
        var comments = File.Exists(path)
            ? File.ReadLines(path).Where(l => l.TrimStart().StartsWith('#')).ToList()
            : new List<string>();
        var list = entries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0 && info.IsUser) list.Add(info.Placeholder);

        var sb = new StringBuilder();
        foreach (var c in comments) sb.Append(c).Append('\n');
        foreach (var e in list) sb.Append(e).Append('\n');
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    public static void EnsureUserLists(string listsDir)
    {
        Directory.CreateDirectory(listsDir);
        foreach (var info in ListCatalog.All.Where(l => l.IsUser))
        {
            var path = Path.Combine(listsDir, info.FileName);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                File.WriteAllText(path, info.Placeholder + "\n");
        }
    }
}

public static partial class EntryParser
{
    private static readonly IdnMapping Idn = new();

    public static IEnumerable<string> Split(string input) =>
        input.Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries);

    public static bool TryNormalizeDomain(string input, out string domain)
    {
        domain = "";
        var s = input.Trim().ToLowerInvariant();
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        var cut = s.IndexOfAny(['/', '?', '#']);
        if (cut >= 0) s = s[..cut];
        var at = s.LastIndexOf('@');
        if (at >= 0) s = s[(at + 1)..];
        s = PortSuffix().Replace(s, "");
        s = s.TrimStart('*', '.').TrimEnd('.');
        if (s.Length == 0) return false;
        try { s = Idn.GetAscii(s); } catch { return false; }
        if (!DomainRegex().IsMatch(s)) return false;
        domain = s;
        return true;
    }

    public static bool TryNormalizeIp(string input, out string ip)
    {
        ip = "";
        var parts = input.Trim().Split('/');
        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var addr)) return false;
        var max = addr.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > max) return false;
            ip = $"{addr}/{prefix}";
        }
        else ip = addr.ToString();
        return true;
    }

    public static bool TryNormalize(string input, ListKind kind, out string value) =>
        kind == ListKind.Domain ? TryNormalizeDomain(input, out value) : TryNormalizeIp(input, out value);

    public static bool LooksLikeIp(string input)
    {
        var s = input.Trim().Split('/')[0];
        return IPAddress.TryParse(s, out _) && (s.Contains(':') || s.Count(c => c == '.') == 3);
    }

    [GeneratedRegex(@":\d+$")]
    private static partial Regex PortSuffix();

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9][a-z0-9-]{0,61}[a-z0-9]$")]
    private static partial Regex DomainRegex();
}
