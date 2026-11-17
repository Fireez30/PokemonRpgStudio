using CommunityToolkit.Mvvm.ComponentModel;

namespace PokemonRpgStudio.App.ViewModels;

/// <summary>
/// Teams page: the Team Manager (teams of saved sheets) is the main view; the Team Card
/// (a card for any team, nothing saved) is opened from a button and closed with a back button.
/// </summary>
public sealed partial class TeamsViewModel : PageViewModel
{
    private bool _active;

    public override string Title => "Teams";
    public override string Glyph => "👥";

    public TeamManagerViewModel Manager { get; }
    public TeamCardViewModel Card { get; }

    [ObservableProperty] private PageViewModel _currentView;

    public TeamsViewModel(TeamManagerViewModel manager, TeamCardViewModel card)
    {
        Manager = manager;
        Card = card;
        _currentView = manager;
    }

    public void ShowManager() => CurrentView = Manager;
    public void ShowCard() => CurrentView = Card;

    partial void OnCurrentViewChanging(PageViewModel value)
    {
        if (_active && value != CurrentView) CurrentView.OnDeactivated();
    }

    partial void OnCurrentViewChanged(PageViewModel value)
    {
        if (_active) value.OnActivated();
    }

    public override void OnActivated()
    {
        _active = true;
        CurrentView.OnActivated();
    }

    public override void OnDeactivated()
    {
        _active = false;
        CurrentView.OnDeactivated();
        // Coming back from the sidebar shows the main view; the card keeps its content.
        CurrentView = Manager;
    }
}
