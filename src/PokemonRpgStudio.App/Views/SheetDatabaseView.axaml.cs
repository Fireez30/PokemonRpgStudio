using Avalonia.Controls;
using Avalonia.Input;
using PokemonRpgStudio.App.ViewModels;

namespace PokemonRpgStudio.App.Views;

public partial class SheetDatabaseView : UserControl
{
    public SheetDatabaseView()
    {
        InitializeComponent();
        Table.DoubleTapped += OnRowDoubleTapped;
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SheetDatabaseViewModel vm) vm.OpenSelectedCommand.Execute(null);
    }
}
