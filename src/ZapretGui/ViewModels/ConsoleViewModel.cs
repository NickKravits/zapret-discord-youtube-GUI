using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretGui.Core;

namespace ZapretGui.ViewModels;

public sealed partial class ConsoleViewModel : PageViewModel
{
    private const int MaxLines = 5000;
    private readonly ConcurrentQueue<LogEntry> _pending = new();

    public ConsoleViewModel(MainViewModel main) : base(main)
    {
        foreach (var e in Log.Snapshot()) Entries.Add(e);
        Log.Added += e => _pending.Enqueue(e);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => Flush();
        timer.Start();
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();

    [ObservableProperty] private bool _autoScroll = true;

    public event Action? ScrollToEndRequested;

    private void Flush()
    {
        if (_pending.IsEmpty) return;
        while (_pending.TryDequeue(out var e)) Entries.Add(e);
        while (Entries.Count > MaxLines) Entries.RemoveAt(0);
        if (AutoScroll) ScrollToEndRequested?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        Log.Clear();
        Entries.Clear();
    }

    [RelayCommand]
    private async Task CopyAllAsync()
    {
        if (Main.SetClipboard == null) return;
        await Main.SetClipboard(string.Join(Environment.NewLine, Entries.Select(e => e.ToString())));
        Main.Notify("Скопировано", $"Строк: {Entries.Count}", FluentAvalonia.UI.Controls.InfoBarSeverity.Success);
    }
}
