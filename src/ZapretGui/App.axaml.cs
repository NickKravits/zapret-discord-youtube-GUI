using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ZapretGui.Core;
using ZapretGui.ViewModels;
using ZapretGui.Views;

namespace ZapretGui;

public partial class App : Application
{
    private MainViewModel? _vm;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private NativeMenuItem? _toggleItem;
    private bool _exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var startHidden = args.Contains("--tray");
            var screenshotIndex = Array.IndexOf(args, "--screenshot");

            var settings = AppSettings.Load();
            _vm = new MainViewModel(settings);
            _vm.SettingsPage.LoadFromSettings();
            _window = new MainWindow { DataContext = _vm };
            CreateTray();

            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.State)) UpdateTray();
            };

            if (!startHidden) _window.Show();
            _ = _vm.InitializeAsync(startHidden);

            if (screenshotIndex >= 0 && screenshotIndex + 1 < args.Length)
                _ = TakeScreenshotsAsync(args[screenshotIndex + 1]);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTray()
    {
        var icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ZapretGUI/Assets/app.png")));

        var open = new NativeMenuItem("Открыть Zapret GUI");
        open.Click += (_, _) => ShowWindow();
        _toggleItem = new NativeMenuItem("Включить обход");
        _toggleItem.Click += (_, _) => _vm?.ToggleBypassCommand.Execute(null);
        var test = new NativeMenuItem("Тест стратегий");
        test.Click += (_, _) => { ShowWindow(); _window?.Navigate("test"); };
        var exit = new NativeMenuItem("Выход");
        exit.Click += (_, _) => _ = ExitAsync();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(_toggleItem);
        menu.Items.Add(test);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);

        _tray = new TrayIcon { Icon = icon, ToolTipText = "Zapret GUI — обход выключен", Menu = menu };
        _tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [_tray]);
    }

    private void UpdateTray()
    {
        if (_vm == null || _tray == null || _toggleItem == null) return;
        var st = _vm.State;
        _toggleItem.Header = st.IsRunning ? "Выключить обход" : "Включить обход";
        _tray.ToolTipText = st.IsRunning ? $"Zapret GUI — включён ({st.Strategy})" : "Zapret GUI — обход выключен";
    }

    private void ShowWindow()
    {
        if (_window == null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        if (_vm != null)
        {
            var state = await _vm.Controller.GetStateAsync();
            if (state.Mode == BypassMode.Process) Log.Info("Выход: обход выключается");
            await _vm.Controller.ShutdownAsync();
        }
        if (_window != null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        _tray?.Dispose();
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private async Task TakeScreenshotsAsync(string dir)
    {
        if (_window == null || _vm == null) return;
        Directory.CreateDirectory(dir);
        await Task.Delay(1500);

        if (_window.TryFindResource("SolidBackgroundFillColorBaseBrush", _window.ActualThemeVariant, out var bg) && bg is Avalonia.Media.IBrush brush)
            _window.Background = brush;

        var z = _vm.Controller.Install;
        var lines = z.GetStrategies().Select(s =>
        {
            try { return $"## {s.Name}\n{string.Join("\n", z.BuildArgs(s))}\n"; }
            catch (Exception ex) { return $"## {s.Name}\nERROR {ex.Message}\n"; }
        });
        await File.WriteAllTextAsync(Path.Combine(dir, "args.txt"), string.Join("\n", lines));
        foreach (var page in new[] { "home", "test", "lists", "dns", "console", "settings" })
        {
            _window.Navigate(page);
            await Task.Delay(900);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var size = new PixelSize((int)_window.Bounds.Width, (int)_window.Bounds.Height);
                using var bmp = new RenderTargetBitmap(size);
                bmp.Render(_window);
                bmp.Save(Path.Combine(dir, page + ".png"));
            });
        }
        await ExitAsync();
    }
}
