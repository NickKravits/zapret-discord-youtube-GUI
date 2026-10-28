using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using ZapretGui.ViewModels;

namespace ZapretGui.Views;

public partial class MainWindow : AppWindow
{
    private readonly Dictionary<string, Control> _pages = new();
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsWindows())
        {
            TitleBar.ExtendsContentIntoTitleBar = true;
            TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None];
        }
        else
        {
            TitleBarHost.IsVisible = false;
        }

        Nav.SelectionChanged += OnNavSelectionChanged;
    }

    public bool AllowClose { get; set; }

    public string CurrentPage { get; private set; } = "";

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateBackground();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActualTransparencyLevelProperty) UpdateBackground();
    }

    private void UpdateBackground()
    {
        if (ActualTransparencyLevel == WindowTransparencyLevel.Mica || ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur)
            Background = Brushes.Transparent;
        else
            ClearValue(BackgroundProperty);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.NavigateRequested -= Navigate;
        _vm = DataContext as MainViewModel;
        if (_vm == null) return;
        _vm.NavigateRequested += Navigate;
        _vm.SetClipboard = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
        Navigate("home");
    }

    public void Navigate(string tag)
    {
        if (tag == "settings")
        {
            Nav.SelectedItem = Nav.SettingsItem;
            return;
        }
        var item = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag);
        if (item != null) Nav.SelectedItem = item;
    }

    private void OnNavSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        var tag = e.IsSettingsSelected ? "settings" : (e.SelectedItem as NavigationViewItem)?.Tag as string;
        if (tag == null || _vm == null) return;
        ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        if (_vm == null) return;
        if (!_pages.TryGetValue(tag, out var page))
        {
            page = tag switch
            {
                "home" => new HomePage { DataContext = _vm.Home },
                "test" => new TestPage { DataContext = _vm.Test },
                "lists" => new ListsPage { DataContext = _vm.Lists },
                "dns" => new DnsPage { DataContext = _vm.DnsPage },
                "console" => new ConsolePage { DataContext = _vm.Console },
                _ => new SettingsPage { DataContext = _vm.SettingsPage },
            };
            _pages[tag] = page;
        }
        Nav.Content = page;
        CurrentPage = tag;
        if (page.DataContext is PageViewModel pvm) _ = pvm.OnNavigatedToAsync();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose && _vm?.Settings.MinimizeToTray == true)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
        if (!AllowClose && Application.Current is App app)
        {
            e.Cancel = true;
            _ = app.ExitAsync();
        }
    }
}
