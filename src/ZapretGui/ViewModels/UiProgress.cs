using Avalonia.Threading;

namespace ZapretGui.ViewModels;

public sealed class UiProgress(Action<string?> report) : IProgress<string>
{
    private volatile bool _completed;

    public void Report(string value)
    {
        if (_completed) return;
        Dispatcher.UIThread.Post(() => { if (!_completed) report(value); });
    }

    public void Complete(string? finalValue = null)
    {
        _completed = true;
        report(finalValue);
    }
}
