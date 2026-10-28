using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Authentication;
using System.Text.RegularExpressions;

namespace ZapretGui.Core;

public sealed record TestTarget(string Name, string? Url, string? PingHost);

public sealed record TargetResult(string Name, string Http, string Tls12, string Tls13, string Ping)
{
    public int OkCount => new[] { Http, Tls12, Tls13 }.Count(r => r == "OK");
    public int TestCount => new[] { Http, Tls12, Tls13 }.Count(r => r != "—" && r != "UNSUP");
    public bool AllOk => OkCount == TestCount;
    public bool AnyFail => OkCount < TestCount;
    public string Summary => Http == "—" ? $"ping {Ping}" : $"HTTP {Http} · TLS1.2 {Tls12} · TLS1.3 {Tls13} · ping {Ping}";
}

public sealed class StrategyTestResult
{
    public required string Strategy { get; init; }
    public List<TargetResult> Targets { get; init; } = new();
    public string? Error { get; init; }
    public int Score => Targets.Sum(t => t.OkCount);
    public int MaxScore => Targets.Sum(t => t.TestCount);
    public double? AvgPing
    {
        get
        {
            var values = Targets.Select(t => t.Ping.EndsWith(" ms") && int.TryParse(t.Ping[..^3], out var v) ? v : (int?)null)
                                .Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return values.Count == 0 ? null : values.Average();
        }
    }
}

public sealed partial class StrategyTester(ZapretController controller)
{
    public static List<TestTarget> LoadTargets(ZapretInstall z)
    {
        var targets = new List<TestTarget>();
        if (File.Exists(z.TargetsFile))
        {
            foreach (var line in File.ReadLines(z.TargetsFile))
            {
                var m = TargetLine().Match(line);
                if (!m.Success) continue;
                var name = m.Groups[1].Value;
                var value = m.Groups[2].Value.Trim();
                if (value.StartsWith("PING:", StringComparison.OrdinalIgnoreCase))
                    targets.Add(new TestTarget(name, null, value[5..].Trim()));
                else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
                    targets.Add(new TestTarget(name, value, uri.Host));
            }
        }
        if (targets.Count == 0)
        {
            targets.AddRange([
                new("DiscordMain", "https://discord.com", "discord.com"),
                new("DiscordGateway", "https://gateway.discord.gg", "gateway.discord.gg"),
                new("DiscordCDN", "https://cdn.discordapp.com", "cdn.discordapp.com"),
                new("YouTubeWeb", "https://www.youtube.com", "www.youtube.com"),
                new("YouTubeImage", "https://i.ytimg.com", "i.ytimg.com"),
                new("YouTubeVideoRedirect", "https://redirector.googlevideo.com", "redirector.googlevideo.com"),
                new("GoogleMain", "https://www.google.com", "www.google.com"),
                new("CloudflareWeb", "https://www.cloudflare.com", "www.cloudflare.com"),
                new("CloudflareDNS1111", null, "1.1.1.1"),
            ]);
        }
        return targets;
    }

    public async Task<List<StrategyTestResult>> RunAsync(
        IReadOnlyList<Strategy> strategies,
        TimeSpan timeout,
        Action<int, Strategy> onStrategyStarted,
        Action<StrategyTestResult> onResult,
        CancellationToken ct)
    {
        var results = new List<StrategyTestResult>();
        var targets = LoadTargets(controller.Install);
        var before = await controller.GetStateAsync();

        Log.Info($"Тест стратегий: {strategies.Count} шт., целей: {targets.Count}");
        if (before.Mode == BypassMode.Service)
        {
            Log.Info("Служба временно остановлена на время теста");
            if (OperatingSystem.IsWindows()) await WindowsBackend.StopServiceAsync();
            else await LinuxBackend.ServiceControlAsync("stop");
        }

        try
        {
            for (var i = 0; i < strategies.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var s = strategies[i];
                onStrategyStarted(i, s);
                StrategyTestResult result;
                try
                {
                    await controller.StartProcessCoreAsync(s);
                    await Task.Delay(TimeSpan.FromSeconds(OperatingSystem.IsWindows() ? 2 : 1), ct);
                    var targetResults = await CheckTargetsAsync(targets, timeout, ct);
                    result = new StrategyTestResult { Strategy = s.Name, Targets = targetResults };
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    result = new StrategyTestResult { Strategy = s.Name, Error = ex.Message };
                }
                finally
                {
                    await controller.StopProcessCoreAsync();
                }

                if (result.Error != null) Log.Error($"[{s.Name}] ошибка: {result.Error}");
                else Log.Write(result.Score == result.MaxScore ? LogLevel.Success : LogLevel.Info,
                    $"[{s.Name}] успешно {result.Score}/{result.MaxScore}" + (result.AvgPing is { } p ? $", ping ~{p:0} ms" : ""));
                results.Add(result);
                onResult(result);
            }
        }
        finally
        {
            try
            {
                if (before.Mode == BypassMode.Service)
                {
                    if (OperatingSystem.IsWindows()) await WindowsBackend.StartServiceAsync();
                    else await LinuxBackend.ServiceControlAsync("start");
                }
                else if (before.Mode == BypassMode.Process && controller.Install.FindStrategy(before.Strategy) is { } prev)
                {
                    await controller.StartProcessCoreAsync(prev);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Не удалось восстановить обход после теста: {ex.Message}");
            }
        }

        var best = Best(results);
        if (best != null) Log.Success($"Лучшая стратегия: «{best.Strategy}» ({best.Score}/{best.MaxScore})");
        return results;
    }

    public static StrategyTestResult? Best(IEnumerable<StrategyTestResult> results) =>
        results.Where(r => r.Error == null && r.MaxScore > 0)
               .OrderByDescending(r => r.Score)
               .ThenBy(r => r.AvgPing ?? double.MaxValue)
               .FirstOrDefault();

    private static async Task<List<TargetResult>> CheckTargetsAsync(List<TestTarget> targets, TimeSpan timeout, CancellationToken ct)
    {
        using var throttle = new SemaphoreSlim(8);
        var tasks = targets.Select(async t =>
        {
            await throttle.WaitAsync(ct);
            try { return await CheckTargetAsync(t, timeout, ct); }
            finally { throttle.Release(); }
        });
        return (await Task.WhenAll(tasks)).ToList();
    }

    private static async Task<TargetResult> CheckTargetAsync(TestTarget t, TimeSpan timeout, CancellationToken ct)
    {
        string http = "—", tls12 = "—", tls13 = "—";
        if (t.Url != null)
        {
            var checks = await Task.WhenAll(
                CheckHttpAsync(t.Url, null, timeout, ct),
                CheckHttpAsync(t.Url, SslProtocols.Tls12, timeout, ct),
                CheckHttpAsync(t.Url, SslProtocols.Tls13, timeout, ct));
            (http, tls12, tls13) = (checks[0], checks[1], checks[2]);
        }
        var ping = t.PingHost != null ? await PingAsync(t.PingHost) : "—";
        return new TargetResult(t.Name, http, tls12, tls13, ping);
    }

    public static async Task<string> CheckHttpAsync(string url, SslProtocols? protocols, TimeSpan timeout, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(Math.Min(3, timeout.TotalSeconds)),
            PooledConnectionLifetime = TimeSpan.Zero,
        };
        if (protocols != null) handler.SslOptions.EnabledSslProtocols = protocols.Value;
        using var client = new HttpClient(handler) { Timeout = timeout };
        using var req = new HttpRequestMessage(HttpMethod.Head, url)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        try
        {
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return "OK";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TaskCanceledException) { return "TIMEOUT"; }
        catch (HttpRequestException ex)
        {
            if (ex.HttpRequestError == HttpRequestError.NameResolutionError) return "DNS";
            if (ex.InnerException is AuthenticationException ae)
            {
                var msg = ae.Message + " " + ae.InnerException?.Message;
                if (msg.Contains("certificate", StringComparison.OrdinalIgnoreCase) || msg.Contains("сертификат", StringComparison.OrdinalIgnoreCase))
                    return "SSL";
                if (protocols == SslProtocols.Tls13 && (ae.InnerException is PlatformNotSupportedException || msg.Contains("not supported", StringComparison.OrdinalIgnoreCase)))
                    return "UNSUP";
                return "ERROR";
            }
            return "ERROR";
        }
        catch (PlatformNotSupportedException) { return "UNSUP"; }
        catch (Exception) { return "ERROR"; }
    }

    private static async Task<string> PingAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1000);
            return reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} ms" : "Timeout";
        }
        catch { return "Timeout"; }
    }

    [GeneratedRegex(@"^\s*([A-Za-z0-9_]+)\s*=\s*""([^""]+)""")]
    private static partial Regex TargetLine();
}
