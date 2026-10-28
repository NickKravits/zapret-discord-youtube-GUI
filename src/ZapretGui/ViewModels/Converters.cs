using Avalonia.Data.Converters;
using FluentAvalonia.UI.Controls;

namespace ZapretGui.ViewModels;

public static class Converters
{
    public static readonly IValueConverter RunningSymbol =
        new FuncValueConverter<bool, Symbol>(running => running ? Symbol.Checkmark : Symbol.Dismiss);
}
