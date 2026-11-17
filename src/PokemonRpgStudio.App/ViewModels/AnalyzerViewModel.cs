using CommunityToolkit.Mvvm.ComponentModel;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.App.ViewModels;

public sealed record TypeBarRow(string Type, int Count, double Width);

public sealed partial class AnalyzerViewModel : PageViewModel
{
    private const double MaxBarWidth = 520;
    private readonly AppServices _services;
    public override string Title => "Analyzer";
    public override string Glyph => "📊";

    public IReadOnlyList<string> PokemonNames { get; private set; }

    [ObservableProperty] private string? _selectedName;
    [ObservableProperty] private PokemonSpecies? _species;
    [ObservableProperty] private IReadOnlyList<TypeBarRow> _rows = Array.Empty<TypeBarRow>();
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _usedFallbackEggMoves;

    public AnalyzerViewModel(AppServices services)
    {
        _services = services;
        PokemonNames = services.Data.Pokemon.Select(p => p.Name).ToList();
        services.GameDataChanged += () =>
        {
            PokemonNames = _services.Data.Pokemon.Select(p => p.Name).ToList();
            OnPropertyChanged(nameof(PokemonNames));
            OnSelectedNameChanged(SelectedName);
        };
    }

    partial void OnSelectedNameChanged(string? value)
    {
        var sp = _services.Data.FindPokemon(value);
        Species = sp;
        if (sp is null)
        {
            Rows = Array.Empty<TypeBarRow>();
            Summary = "";
            return;
        }
        var analysis = PokedexHelpers.AnalyzeMoveTypes(sp, _services.Data);
        int max = analysis.Counts.Count > 0 ? analysis.Counts[0].Count : 1;
        Rows = analysis.Counts.Select(c => new TypeBarRow(c.Type, c.Count, Math.Max(4, MaxBarWidth * c.Count / max))).ToList();
        Summary = analysis.TotalMoves == 0
            ? "No moves found for this Pokémon."
            : $"{analysis.TotalMoves} move{(analysis.TotalMoves > 1 ? "s" : "")} (level-up, egg, TM and tutor)";
        UsedFallbackEggMoves = analysis.UsedFallbackEggMoves;
    }
}
