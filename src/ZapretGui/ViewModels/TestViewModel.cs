using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using ZapretGui.Core;

namespace ZapretGui.ViewModels;

public sealed partial class SelectableStrategy(Strategy strategy, bool selected) : ObservableObject
{
    public Strategy Strategy { get; } = strategy;
    public string Name => Strategy.Name;
    [ObservableProperty] private bool _isSelected = selected;
}

public sealed partial class TestResultItem(StrategyTestResult result) : ObservableObject
{
    public StrategyTestResult Result { get; } = result;
    public string Name => Result.Strategy;
    public bool HasError => Result.Error != null;
    public string ScoreText => HasError ? "Ошибка запуска" : $"{Result.Score} из {Result.MaxScore}";
    public double Percent => Result.MaxScore == 0 ? 0 : 100.0 * Result.Score / Result.MaxScore;
    public string PingText => Result.AvgPing is { } p ? $"~{p:0} мс" : "—";
    public string Details => Result.Error ?? $"Средний ping {PingText}";
    public IReadOnlyList<TargetResult> Targets => Result.Targets;
    [ObservableProperty] private bool _isBest;
}

public sealed partial class TestViewModel(MainViewModel main) : PageViewModel(main)
{
    private CancellationTokenSource? _cts;

    public ObservableCollection<SelectableStrategy> Items { get; } = new();
    public ObservableCollection<TestResultItem> Results { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isTesting;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private double _progressMax = 1;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private decimal _timeoutSeconds = 5;
    [ObservableProperty] private string _targetsText = "";
    [ObservableProperty] private TestResultItem? _best;

    public bool HasResults => Results.Count > 0;

    public void ReloadStrategies(IEnumerable<Strategy> strategies)
    {
        TimeoutSeconds = Math.Clamp(Settings.TestTimeoutSeconds, 2, 30);
        var selection = Settings.TestSelection;
        Items.Clear();
        foreach (var s in strategies)
            Items.Add(new SelectableStrategy(s, selection.Count == 0 || selection.Contains(s.Name)));
        UpdateTargetsText();
    }

    private void UpdateTargetsText()
    {
        var targets = StrategyTester.LoadTargets(Controller.Install);
        TargetsText = $"Проверяется {targets.Count} адресов: " + string.Join(", ", targets.Select(t => t.Name));
    }

    [RelayCommand]
    private void SelectAll() { foreach (var i in Items) i.IsSelected = true; }

    [RelayCommand]
    private void SelectNone() { foreach (var i in Items) i.IsSelected = false; }

    [RelayCommand]
    private async Task StartAsync()
    {
        var selected = Items.Where(i => i.IsSelected).Select(i => i.Strategy).ToList();
        if (selected.Count == 0)
        {
            Main.Notify("Не выбраны стратегии", "Отметьте хотя бы одну стратегию для теста", InfoBarSeverity.Warning);
            return;
        }

        Settings.TestSelection = selected.Count == Items.Count ? new List<string>() : selected.Select(s => s.Name).ToList();
        Settings.TestTimeoutSeconds = (int)TimeoutSeconds;
        Settings.Save();

        Results.Clear();
        Best = null;
        OnPropertyChanged(nameof(HasResults));
        IsTesting = true;
        ProgressMax = selected.Count;
        ProgressValue = 0;
        _cts = new CancellationTokenSource();
        var tester = new StrategyTester(Controller);

        try
        {
            await tester.RunAsync(selected, TimeSpan.FromSeconds((double)TimeoutSeconds),
                (i, s) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ProgressValue = i;
                    ProgressText = $"Стратегия {i + 1} из {selected.Count}: {s.Name}";
                }),
                r => Avalonia.Threading.Dispatcher.UIThread.Post(() => AddResult(r)),
                _cts.Token);

            ProgressValue = ProgressMax;
            ProgressText = "Тест завершён";
            if (Best != null)
                Main.Notify("Тест завершён", $"Лучшая стратегия: «{Best.Name}» ({Best.ScoreText})", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Тест остановлен";
            Log.Warning("Тест стратегий остановлен пользователем");
        }
        catch (Exception ex)
        {
            ProgressText = "Ошибка теста";
            Log.Error(ex.Message);
            Main.Notify("Ошибка теста", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsTesting = false;
            _cts.Dispose();
            _cts = null;
            await Main.RefreshStateAsync();
        }
    }

    private void AddResult(StrategyTestResult r)
    {
        var item = new TestResultItem(r);
        var index = 0;
        while (index < Results.Count && Compare(Results[index].Result, r) <= 0) index++;
        Results.Insert(index, item);

        var best = StrategyTester.Best(Results.Select(x => x.Result));
        foreach (var x in Results) x.IsBest = best != null && ReferenceEquals(x.Result, best);
        Best = Results.FirstOrDefault(x => x.IsBest);
        OnPropertyChanged(nameof(HasResults));
    }

    private static int Compare(StrategyTestResult a, StrategyTestResult b)
    {
        if ((a.Error == null) != (b.Error == null)) return a.Error == null ? -1 : 1;
        var byScore = b.Score.CompareTo(a.Score);
        if (byScore != 0) return byScore;
        return (a.AvgPing ?? double.MaxValue).CompareTo(b.AvgPing ?? double.MaxValue);
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private bool CanApply(TestResultItem? item) => !IsTesting;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync(TestResultItem? item)
    {
        item ??= Best;
        if (item == null) return;
        var strategy = Controller.Install.FindStrategy(item.Name);
        if (strategy == null) return;
        Main.Home.SelectedStrategy = Main.Home.Strategies.FirstOrDefault(s => s.Name == strategy.Name);
        await GuardAsync(() => Controller.StartAsync(strategy), $"Включена стратегия «{strategy.Name}»");
    }

    [RelayCommand]
    private void OpenTargets()
    {
        var file = Controller.Install.TargetsFile;
        if (File.Exists(file)) Shell.OpenInShell(file);
        else Main.Notify("Файл не найден", file, InfoBarSeverity.Warning);
    }

    public override Task OnNavigatedToAsync()
    {
        UpdateTargetsText();
        return Task.CompletedTask;
    }
}
