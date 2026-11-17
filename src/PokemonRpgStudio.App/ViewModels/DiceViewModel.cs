using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.App.ViewModels;

/// <summary>A single die result shown as a badge. Kind: "", "hit", "shiny", "platinum".</summary>
public sealed record RollBadge(int Value, string Kind)
{
    public bool IsHit => Kind == "hit";
    public bool IsShiny => Kind == "shiny";
    public bool IsPlatinum => Kind == "platinum";
}

public sealed record RollTierGroup(string Title, IReadOnlyList<RollBadge> Rolls);

public sealed partial class DiceViewModel : PageViewModel
{
    private readonly AppServices _services;
    public override string Title => "Dice Rolls";
    public override string Glyph => "🎲";

    // Encounter / capture
    [ObservableProperty] private string _encounterBracket = "—";
    [ObservableProperty] private string _encounterCount = "—";
    [ObservableProperty] private string _captureAccuracy = "—";
    [ObservableProperty] private string _captureRate = "—";

    // Expression
    [ObservableProperty] private string _expression = "2d6+1d8+4";
    [ObservableProperty] private string _expressionResult = "";
    [ObservableProperty] private string _expressionDetails = "";

    // Shiny
    [ObservableProperty] private int _shinyNumber;
    [ObservableProperty] private int _shinyDexnav;
    [ObservableProperty] private int _shinyCount = 1;
    public ObservableCollection<RollBadge> ShinyResults { get; } = new();

    // Cards
    [ObservableProperty] private int _cardNormal1;
    [ObservableProperty] private int _cardNormal2;
    [ObservableProperty] private int _cardNormal3;
    [ObservableProperty] private int _cardShiny1;
    [ObservableProperty] private int _cardShiny2;
    [ObservableProperty] private int _cardPlatine1;
    [ObservableProperty] private int _cardDexnav;
    [ObservableProperty] private int _cardCount = 1;
    [ObservableProperty] private bool _caughtIsShiny;
    [ObservableProperty] private bool _caughtIsPlatine;
    public ObservableCollection<RollTierGroup> CardResults { get; } = new();

    private bool _loading;

    public DiceViewModel(AppServices services)
    {
        _services = services;
        _loading = true;
        var s = services.Settings;
        ShinyNumber = s.ShinyNumber;
        ShinyDexnav = s.ShinyDexnav;
        CardNormal1 = s.Cards.Normal1;
        CardNormal2 = s.Cards.Normal2;
        CardNormal3 = s.Cards.Normal3;
        CardShiny1 = s.Cards.Shiny1;
        CardShiny2 = s.Cards.Shiny2;
        CardPlatine1 = s.Cards.Platine1;
        CardDexnav = s.Cards.Dexnav;
        _loading = false;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading) return;
        // Persist the personal numbers (the website kept them in cookies).
        switch (e.PropertyName)
        {
            case nameof(ShinyNumber): case nameof(ShinyDexnav): case nameof(CardNormal1): case nameof(CardNormal2):
            case nameof(CardNormal3): case nameof(CardShiny1): case nameof(CardShiny2): case nameof(CardPlatine1):
            case nameof(CardDexnav):
                var s = _services.Settings;
                s.ShinyNumber = ShinyNumber;
                s.ShinyDexnav = ShinyDexnav;
                s.Cards = CurrentCardSettings();
                _services.SaveSettings();
                break;
        }
    }

    private CardRollSettings CurrentCardSettings() => new()
    {
        Normal1 = CardNormal1, Normal2 = CardNormal2, Normal3 = CardNormal3,
        Shiny1 = CardShiny1, Shiny2 = CardShiny2, Platine1 = CardPlatine1, Dexnav = CardDexnav,
    };

    [RelayCommand]
    private void RollEncounter()
    {
        int bracket = Dice.Roll(100), count = Dice.Roll(20);
        EncounterBracket = $"{bracket} / 100";
        EncounterCount = $"{count} / 20";
    }

    [RelayCommand]
    private void RollCapture()
    {
        CaptureAccuracy = $"{Dice.Roll(20)} / 20";
        CaptureRate = $"{Dice.Roll(100)} / 100";
    }

    [RelayCommand]
    private void RollExpression()
    {
        try
        {
            var r = Dice.RollExpression(Expression);
            ExpressionResult = $"Result = {r.Final}";
            ExpressionDetails = string.Join("\n", r.Rolls.Select(x => $"{x.Dice} : ( {string.Join(", ", x.Results)} ) = {x.Total}"));
        }
        catch (FormatException ex)
        {
            ExpressionResult = "Invalid expression";
            ExpressionDetails = ex.Message;
        }
    }

    [RelayCommand]
    private void RollShiny()
    {
        ShinyResults.Clear();
        var rolls = Dice.RollShinyBatch(Math.Clamp(ShinyCount, 1, 500), ShinyNumber, ShinyDexnav);
        bool hasTarget = rolls.Contains(ShinyNumber);
        foreach (var r in rolls)
            ShinyResults.Add(new RollBadge(r, r == ShinyNumber ? "hit" : r == 100 && hasTarget ? "platinum" : ""));
    }

    [RelayCommand]
    private void RollCards()
    {
        CardResults.Clear();
        var settings = CurrentCardSettings();
        var batches = Dice.RollCards(Math.Clamp(CardCount, 1, 500), settings, CaughtIsShiny, CaughtIsPlatine);

        RollTierGroup Group(string title, IReadOnlyList<int> rolls, bool canShiny, bool canPlatine) =>
            new(title, rolls.Select(r => new RollBadge(r, Dice.CardTier(r, settings, canShiny, canPlatine) switch
            {
                CardRollTier.Hit => "hit",
                CardRollTier.Shiny => "shiny",
                CardRollTier.Platinum => "platinum",
                _ => "",
            })).ToList());

        if (batches.PlatineTier.Count > 0) CardResults.Add(Group("Platine / shiny / normal batch", batches.PlatineTier, true, true));
        if (batches.ShinyTier.Count > 0) CardResults.Add(Group("Shiny / normal batch", batches.ShinyTier, true, false));
        CardResults.Add(Group("Normal batch", batches.BaseTier, false, false));
    }
}
