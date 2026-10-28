namespace ZapretGui.Core;

public enum LogLevel { Info, Success, Warning, Error, Command, Output }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Text)
{
    public string TimeText => Time.ToString("HH:mm:ss");
    public bool IsSuccess => Level == LogLevel.Success;
    public bool IsWarning => Level == LogLevel.Warning;
    public bool IsError => Level == LogLevel.Error;
    public bool IsCommand => Level == LogLevel.Command;
    public bool IsOutput => Level == LogLevel.Output;

    public override string ToString() => $"[{TimeText}] {Text}";
}

public static class Log
{
    private const int MaxEntries = 5000;
    private static readonly object Sync = new();
    private static readonly List<LogEntry> Buffer = new();

    public static event Action<LogEntry>? Added;

    public static IReadOnlyList<LogEntry> Snapshot()
    {
        lock (Sync) return Buffer.ToList();
    }

    public static void Clear()
    {
        lock (Sync) Buffer.Clear();
    }

    public static void Write(LogLevel level, string text)
    {
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            var entry = new LogEntry(DateTime.Now, level, line);
            lock (Sync)
            {
                Buffer.Add(entry);
                if (Buffer.Count > MaxEntries) Buffer.RemoveRange(0, Buffer.Count - MaxEntries);
            }
            Added?.Invoke(entry);
        }
    }

    public static void Info(string text) => Write(LogLevel.Info, text);
    public static void Success(string text) => Write(LogLevel.Success, text);
    public static void Warning(string text) => Write(LogLevel.Warning, text);
    public static void Error(string text) => Write(LogLevel.Error, text);
    public static void Command(string text) => Write(LogLevel.Command, text);
    public static void Output(string text) => Write(LogLevel.Output, text);
}
