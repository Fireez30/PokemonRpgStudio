using CommunityToolkit.Mvvm.ComponentModel;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.App.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Glyph { get; }

    /// <summary>Called each time the page is shown.</summary>
    public virtual void OnActivated() { }

    /// <summary>Called each time the page is hidden (save pending work).</summary>
    public virtual void OnDeactivated() { }
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    public AppServices Services { get; }
    public SheetsViewModel Sheets { get; }
    public SheetDatabaseViewModel SheetList { get; }
    public SheetEditorViewModel Editor { get; }
    public PokedexViewModel Pokedex { get; }
    public MovesViewModel Moves { get; }
    public AbilitiesViewModel Abilities { get; }
    public TeamManagerViewModel Teams { get; }
    public TeamCardViewModel TeamCard { get; }
    /// <summary>Teams section: Team Manager, with the Team Card opened from a button.</summary>
    public TeamsViewModel TeamsPage { get; }
    public EncounterViewModel Encounters { get; }
    public DiceViewModel Dice { get; }
    public AnalyzerViewModel Analyzer { get; }
    public TypeChartViewModel TypeChart { get; }
    public WhiteboardViewModel Whiteboard { get; }
    /// <summary>The shops, shared by the two views of the Shops section.</summary>
    public ShopsViewModel Shops { get; }
    public ShopViewerViewModel ShopViewer { get; }
    public ShopEditorViewModel ShopEditor { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>Sheets section: list view (sheet in use) or database view (table of every sheet).</summary>
    public SectionViewModel SheetsSection { get; }
    /// <summary>Tools section: dice rolls, type chart, analyzer and whiteboard.</summary>
    public SectionViewModel Tools { get; }
    /// <summary>Shops section: shop view (read-only) or edit view.</summary>
    public SectionViewModel ShopsSection { get; }

    public IReadOnlyList<PageViewModel> Pages { get; }

    [ObservableProperty] private PageViewModel _currentPage;

    public string Version => "v" + (typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    public MainWindowViewModel(AppServices services)
    {
        Services = services;
        Editor = new SheetEditorViewModel(services, CloseEditor);
        Sheets = new SheetsViewModel(services, OpenEditor, () => OpenNewSheet(null, Sheets.CurrentFolder));
        SheetList = new SheetDatabaseViewModel(services, OpenSheet);
        Pokedex = new PokedexViewModel(services, species => OpenNewSheet(species));
        Moves = new MovesViewModel(services, OpenPokemon);
        Abilities = new AbilitiesViewModel(services, OpenPokemon);
        Teams = new TeamManagerViewModel(services, OpenSheet, () => TeamsPage.ShowCard());
        TeamCard = new TeamCardViewModel(services, () => TeamsPage.ShowManager());
        TeamsPage = new TeamsViewModel(Teams, TeamCard);
        Encounters = new EncounterViewModel(services);
        Dice = new DiceViewModel(services);
        Analyzer = new AnalyzerViewModel(services);
        TypeChart = new TypeChartViewModel(services);
        Whiteboard = new WhiteboardViewModel(services);
        Shops = new ShopsViewModel(services);
        ShopEditor = new ShopEditorViewModel(Shops);
        ShopViewer = new ShopViewerViewModel(Shops, () => ShopsSection.Show(ShopEditor));
        Settings = new SettingsViewModel(services);
        SheetsSection = new SectionViewModel(services, "sheets", "Sheets", "📝",
            new SectionTab("list", "List view", "📝", Sheets),
            new SectionTab("database", "Database view", "🗂", SheetList));
        Tools = new SectionViewModel(services, "tools", "Tools", "🧰",
            new SectionTab("dice", "Dice Rolls", Dice.Glyph, Dice),
            new SectionTab("types", "Type Chart", TypeChart.Glyph, TypeChart),
            new SectionTab("analyzer", "Analyzer", Analyzer.Glyph, Analyzer),
            new SectionTab("whiteboard", "Whiteboard", Whiteboard.Glyph, Whiteboard));
        ShopsSection = new SectionViewModel(services, "shops", "Shops", "🛒",
            new SectionTab("view", "Shop view", "🛒", ShopViewer),
            new SectionTab("edit", "Edit", "✏", ShopEditor));
        Pages = new PageViewModel[] { SheetsSection, Editor, Pokedex, Moves, Abilities, TeamsPage, Encounters, ShopsSection, Tools, Settings };
        _currentPage = SheetsSection;
        SheetsSection.OnActivated();
    }

    /// <summary>True when the list view of the Sheets section is shown.</summary>
    public bool IsSheetListViewShown => CurrentPage == SheetsSection && SheetsSection.IsShowing(Sheets);

    partial void OnCurrentPageChanging(PageViewModel value)
    {
        // Leaving a page: write pending changes (editor auto-save, session notes) so the other pages see them.
        if (CurrentPage != value) CurrentPage?.OnDeactivated();
    }

    partial void OnCurrentPageChanged(PageViewModel value) => value?.OnActivated();

    private void OpenNewSheet(PokemonSpecies? species, string? folder = null)
    {
        Editor.StartNewSheet(species, folder);
        CurrentPage = Editor;
    }

    /// <summary>Shows a Pokémon in the Pokédex (links of the Moves and Abilities pages).</summary>
    private void OpenPokemon(string name)
    {
        Pokedex.SelectedName = name;
        CurrentPage = Pokedex;
    }

    private void OpenEditor(SheetRecord record)
    {
        Editor.Open(record);
        CurrentPage = Editor;
    }

    /// <summary>Shows a sheet in the list view of the Sheets section (from the database view or the Team Manager).</summary>
    private void OpenSheet(string sheetId)
    {
        ShowSheetsListView();
        Sheets.Select(sheetId);
    }

    private void CloseEditor(string? sheetId)
    {
        ShowSheetsListView();
        Sheets.Select(sheetId);
    }

    private void ShowSheetsListView()
    {
        SheetsSection.Show(Sheets);
        CurrentPage = SheetsSection;
    }

    /// <summary>Saves pending work before the window closes.</summary>
    public void Shutdown()
    {
        Editor.FlushAutoSave();
        Sheets.FlushNotes();
        Teams.OnDeactivated();
        Whiteboard.OnDeactivated();
        Shops.Flush();
    }
}
