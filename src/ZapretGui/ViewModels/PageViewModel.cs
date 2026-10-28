using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using ZapretGui.Core;

namespace ZapretGui.ViewModels;

public abstract partial class PageViewModel(MainViewModel main) : ObservableObject
{
    protected MainViewModel Main { get; } = main;
    protected AppSettings Settings => Main.Settings;
    protected ZapretController Controller => Main.Controller;

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => !OperatingSystem.IsWindows();

    [ObservableProperty]
    private bool _isBusy;

    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;

    protected async Task<bool> GuardAsync(Func<Task> action, string? successMessage = null)
    {
        IsBusy = true;
        try
        {
            await action();
            if (successMessage != null) Main.Notify(successMessage, null, InfoBarSeverity.Success);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            Main.Notify("Ошибка", ex.Message, InfoBarSeverity.Error);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
