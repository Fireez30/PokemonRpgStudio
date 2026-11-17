using Avalonia.Controls;

namespace PokemonRpgStudio.App.Views;

public partial class MovesView : UserControl
{
    public MovesView()
    {
        InitializeComponent();
        SplitLayout.Attach(Split, DetailsPane, minWidth: 1050, detailsWidth: 360);
    }
}
