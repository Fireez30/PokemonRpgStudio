using Avalonia.Controls;
using Avalonia.Input;
using PokemonRpgStudio.App.ViewModels;

namespace PokemonRpgStudio.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as MainWindowViewModel)?.Shutdown();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (e.KeyModifiers != KeyModifiers.Control) return;
        if (e.Key == Key.S && vm.CurrentPage == vm.Editor)
        {
            vm.Editor.SaveCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.N && (vm.CurrentPage == vm.SheetsSection || vm.CurrentPage == vm.Editor))
        {
            vm.Sheets.NewSheetCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.E && vm.IsSheetListViewShown)
        {
            vm.Sheets.EditCommand.Execute(null);
            e.Handled = true;
        }
    }
}
