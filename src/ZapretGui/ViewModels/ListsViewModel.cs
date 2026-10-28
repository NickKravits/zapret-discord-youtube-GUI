using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using ZapretGui.Core;

namespace ZapretGui.ViewModels;

public sealed partial class ListsViewModel(MainViewModel main) : PageViewModel(main)
{
    private List<string> _all = new();

    public IReadOnlyList<ListInfo> Lists { get; } = ListCatalog.All;
    public IReadOnlyList<string> QuickTargets { get; } = ["Обходить блокировку", "Исключить из обхода"];

    [ObservableProperty] private ListInfo? _selectedList;
    [ObservableProperty] private IReadOnlyList<string> _entries = [];
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _quickInput = "";
    [ObservableProperty] private int _quickTargetIndex;
    [ObservableProperty] private string _addInput = "";
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private bool _needsRestart;

    public override Task OnNavigatedToAsync()
    {
        SelectedList ??= Lists[0];
        Load();
        return Task.CompletedTask;
    }

    partial void OnSelectedListChanged(ListInfo? value) => Load();
    partial void OnFilterChanged(string value) => ApplyFilter();

    private string PathOf(ListInfo info) => Controller.Install.ListPath(info.FileName);

    private void Load()
    {
        if (SelectedList is not { } info) return;
        try
        {
            if (info.IsUser) ListStore.EnsureUserLists(Controller.Install.ListsDir);
            _all = ListStore.ReadEntries(PathOf(info), info);
        }
        catch (Exception ex)
        {
            _all = new List<string>();
            Log.Warning($"Не удалось прочитать {info.FileName}: {ex.Message}");
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var f = Filter.Trim();
        Entries = f.Length == 0 ? _all.ToList() : _all.Where(e => e.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        CountText = f.Length == 0 ? $"Записей: {_all.Count}" : $"Найдено: {Entries.Count} из {_all.Count}";
    }

    private void Save(ListInfo info, List<string> entries)
    {
        ListStore.WriteEntries(PathOf(info), info, entries);
        if (ReferenceEquals(info, SelectedList)) { _all = entries; ApplyFilter(); }
        NeedsRestart = Main.State.IsRunning;
    }

    [RelayCommand]
    private void QuickAdd()
    {
        var exclude = QuickTargetIndex == 1;
        var domains = new List<string>();
        var ips = new List<string>();
        var bad = new List<string>();
        foreach (var raw in EntryParser.Split(QuickInput))
        {
            if (EntryParser.LooksLikeIp(raw) && EntryParser.TryNormalizeIp(raw, out var ip)) ips.Add(ip);
            else if (EntryParser.TryNormalizeDomain(raw, out var d)) domains.Add(d);
            else bad.Add(raw);
        }

        var added = 0;
        if (domains.Count > 0) added += AddTo(ListCatalog.Get(exclude ? ListCatalog.ExcludeUser : ListCatalog.GeneralUser), domains);
        if (ips.Count > 0) added += AddTo(ListCatalog.Get(exclude ? ListCatalog.IpsetExcludeUser : ListCatalog.IpsetUser), ips);

        Report(added, domains.Count + ips.Count, bad);
        if (bad.Count == 0) QuickInput = "";
    }

    [RelayCommand]
    private void AddToSelected()
    {
        if (SelectedList is not { } info) return;
        var good = new List<string>();
        var bad = new List<string>();
        foreach (var raw in EntryParser.Split(AddInput))
        {
            if (EntryParser.TryNormalize(raw, info.Kind, out var v)) good.Add(v);
            else bad.Add(raw);
        }
        var added = AddTo(info, good);
        Report(added, good.Count, bad);
        if (bad.Count == 0) AddInput = "";
    }

    private int AddTo(ListInfo info, List<string> values)
    {
        var path = PathOf(info);
        var current = ListStore.ReadEntries(path, info);
        var set = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        var fresh = values.Where(set.Add).ToList();
        if (fresh.Count == 0) return 0;
        current.AddRange(fresh);
        try
        {
            Save(info, current);
            Log.Success($"Добавлено в «{info.Title}»: {string.Join(", ", fresh)}");
        }
        catch (Exception ex)
        {
            Main.Notify("Не удалось сохранить список", ex.Message, InfoBarSeverity.Error);
            return 0;
        }
        return fresh.Count;
    }

    private void Report(int added, int valid, List<string> bad)
    {
        if (bad.Count > 0)
            Main.Notify("Некорректные записи", "Не распознано: " + string.Join(", ", bad.Take(5)) + (bad.Count > 5 ? "…" : ""), InfoBarSeverity.Warning);
        else if (added > 0)
            Main.Notify($"Добавлено записей: {added}", NeedsRestart ? "Перезапустите обход, чтобы применить изменения" : null, InfoBarSeverity.Success);
        else if (valid > 0)
            Main.Notify("Уже есть в списке", null, InfoBarSeverity.Informational);
    }

    [RelayCommand]
    private void RemoveSelected(IList? selected)
    {
        if (SelectedList is not { } info || selected == null || selected.Count == 0) return;
        var remove = new HashSet<string>(selected.OfType<string>(), StringComparer.OrdinalIgnoreCase);
        var entries = _all.Where(e => !remove.Contains(e)).ToList();
        try
        {
            Save(info, entries);
            Log.Info($"Удалено из «{info.Title}»: {string.Join(", ", remove.Take(20))}{(remove.Count > 20 ? "…" : "")}");
        }
        catch (Exception ex)
        {
            Main.Notify("Не удалось сохранить список", ex.Message, InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private void OpenFile()
    {
        if (SelectedList is { } info) Shell.OpenInShell(PathOf(info));
    }

    [RelayCommand]
    private void Reload() => Load();

    [RelayCommand]
    private async Task RestartAsync()
    {
        if (await GuardAsync(Controller.RestartAsync, "Обход перезапущен")) NeedsRestart = false;
    }
}
