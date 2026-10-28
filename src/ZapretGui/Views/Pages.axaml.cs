using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ZapretGui.ViewModels;

namespace ZapretGui.Views;

public partial class HomePage : UserControl
{
    public HomePage() => InitializeComponent();
}

public partial class TestPage : UserControl
{
    public TestPage() => InitializeComponent();
}

public partial class ListsPage : UserControl
{
    public ListsPage() => InitializeComponent();
}

public partial class DnsPage : UserControl
{
    public DnsPage() => InitializeComponent();
}

public partial class ConsolePage : UserControl
{
    private ConsoleViewModel? _vm;

    public ConsolePage()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ScrollToEnd();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.ScrollToEndRequested -= ScrollToEnd;
        _vm = DataContext as ConsoleViewModel;
        if (_vm != null) _vm.ScrollToEndRequested += ScrollToEnd;
    }

    private void ScrollToEnd()
    {
        if (_vm == null || _vm.Entries.Count == 0 || !IsVisible) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_vm.Entries.Count > 0) LogList.ScrollIntoView(_vm.Entries.Count - 1);
        }, DispatcherPriority.Background);
    }
}

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        BrowseButton.Click += OnBrowseClick;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Папка zapret-discord-youtube (с bin, lists и .bat-файлами)",
            AllowMultiple = false,
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path != null) vm.ChangeZapretDir(path);
    }
}
