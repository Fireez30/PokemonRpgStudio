using Avalonia.Controls;

namespace PokemonRpgStudio.App.Views;

public partial class AbilitiesView : UserControl
{
    public AbilitiesView()
    {
        InitializeComponent();
        SplitLayout.Attach(Split, DetailsPane, minWidth: 1000, detailsWidth: 400);
    }
}
