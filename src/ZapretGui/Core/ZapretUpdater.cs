using System.IO.Compression;
using System.Text.Json;

namespace ZapretGui.Core;

public static class ZapretUpdater
{
    private const string Repo = "Flowseal/zapret-discord-youtube";
    private const string VersionUrl = $"https://raw.githubusercontent.com/{Repo}/main/.service/version.txt";
    private const string IpsetUrl = $"https://raw.githubusercontent.com/{Repo}/refs/heads/main/.service/ipset-service.txt";
    private const string MainZipUrl = $"https://github.com/{Repo}/archive/refs/heads/main.zip";
    public const string ReleasesUrl = $"https://github.com/{Repo}/releases/latest";

    private static readonly string[] Preserve =
    [
        "lists/list-general-user.txt", "lists/list-exclude-user.txt", "lists/ipset-exclude-user.txt",
        "lists/ipset-all-user.txt", "lists/ipset-all.txt", "lists/ipset-all.txt.backup",
        "utils/game_filter.enabled", "utils/targets.txt", "bin/ACTIVE_DISCORD_UDP.bin", "bin/ACTIVE_GAME_UDP.bin",
    ];

    public static async Task<string?> GetRemoteVersionAsync()
    {
        try
        {
            using var http = Http.CreateClient(TimeSpan.FromSeconds(10));
            return (await http.GetStringAsync(VersionUrl)).Trim();
        }
        catch (Exception ex)
        {
            Log.Warning($"Не удалось проверить версию zapret: {ex.Message}");
            return null;
        }
    }

    public static async Task DownloadAsync(ZapretInstall target, IProgress<string>? progress = null)
    {
        using var http = Http.CreateClient(TimeSpan.FromMinutes(5));
        var (url, name) = await FindReleaseZipAsync(http);
        progress?.Report($"Загрузка {name}…");
        Log.Info($"Загрузка {url}");

        var tmpDir = Path.Combine(Path.GetTempPath(), "zapret-gui-" + Guid.NewGuid().ToString("N"));
        var zipPath = tmpDir + ".zip";
        try
        {
            await using (var fs = File.Create(zipPath))
            await using (var stream = await http.GetStreamAsync(url))
                await stream.CopyToAsync(fs);

            progress?.Report("Распаковка…");
            ZipFile.ExtractToDirectory(zipPath, tmpDir);

            var source = Directory.EnumerateDirectories(tmpDir, "bin", SearchOption.AllDirectories)
                             .Select(Path.GetDirectoryName)
                             .FirstOrDefault(d => d != null && Directory.Exists(Path.Combine(d, "lists")))
                         ?? throw new InvalidOperationException("В архиве не найдены папки bin и lists");

            progress?.Report("Копирование файлов…");
            Directory.CreateDirectory(target.Root);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file).Replace('\\', '/');
                if (rel.StartsWith(".github/", StringComparison.Ordinal)) continue;
                var dest = Path.Combine(target.Root, rel);
                if (Preserve.Contains(rel, StringComparer.OrdinalIgnoreCase) && File.Exists(dest)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }
            ListStore.EnsureUserLists(target.ListsDir);
            Log.Success($"Файлы zapret установлены в {target.Root} (версия {target.LocalVersion ?? "?"})");
        }
        finally
        {
            try { File.Delete(zipPath); } catch { }
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    private static async Task<(string Url, string Name)> FindReleaseZipAsync(HttpClient http)
    {
        try
        {
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return (a.GetProperty("browser_download_url").GetString()!, name);
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"GitHub API недоступен ({ex.Message}), используется архив ветки main");
        }
        return (MainZipUrl, "main.zip");
    }

    public static async Task UpdateIpsetAsync(ZapretInstall z)
    {
        using var http = Http.CreateClient(TimeSpan.FromSeconds(30));
        Log.Info("Загрузка списка IPSet…");
        var content = await http.GetStringAsync(IpsetUrl);
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Получен пустой список");
        var dest = z.GetIpsetMode() == IpsetMode.Loaded ? z.IpsetAllFile : z.IpsetBackupFile;
        await File.WriteAllTextAsync(dest, content.Replace("\r\n", "\n"));
        Log.Success($"Список IPSet обновлён ({content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} записей)");
    }
}
