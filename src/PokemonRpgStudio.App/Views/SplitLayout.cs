using Avalonia;
using Avalonia.Controls;

namespace PokemonRpgStudio.App.Views;

/// <summary>
/// List + details layout: the details pane sits beside the list, and moves below it when the
/// page is too narrow for both (so the table columns never get squeezed).
/// </summary>
internal static class SplitLayout
{
    public static void Attach(Grid grid, Control details, double minWidth, double detailsWidth, double stackedHeight = 320)
    {
        void Update(double width)
        {
            bool stacked = width > 0 && width < minWidth;
            grid.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 16);
            grid.ColumnDefinitions[2].Width = new GridLength(stacked ? 0 : detailsWidth);
            Grid.SetColumn(details, stacked ? 0 : 2);
            Grid.SetRow(details, stacked ? 1 : 0);
            details.MaxHeight = stacked ? stackedHeight : double.PositiveInfinity;
            details.Margin = stacked ? new Thickness(0, 4, 0, 12) : new Thickness(0, 0, 0, 12);
        }
        grid.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty) Update(((Rect)e.NewValue!).Width);
        };
        Update(grid.Bounds.Width);
    }
}
