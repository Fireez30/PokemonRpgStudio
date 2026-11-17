using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.App.ViewModels;

/// <summary>One cell of the chart: <see cref="Attacking"/> used on <see cref="Defending"/>.</summary>
public sealed record TypeChartCell(string Attacking, string Defending, double Multiplier)
{
    public string Text => TypeChart.Format(Multiplier);
    public string ToolTip => $"{Attacking} → {Defending}: ×{Text} ({TypeChart.Describe(Multiplier)})";
}

public sealed record TypeChartRow(string Defending, IReadOnlyList<TypeChartCell> Cells);

/// <summary>Attacking types grouped by the multiplier they deal to the chosen defender (×4, ×2…).</summary>
public sealed record TypeMultiplierGroup(double Multiplier, IReadOnlyList<string> Types)
{
    public string Label => $"×{TypeChart.Format(Multiplier)}";
    public string Description => TypeChart.Describe(Multiplier);
}

/// <summary>
/// Damage taken by a Pokémon of the given types, without the neutral (×1) multipliers: what a sheet shows
/// under its types (weaknesses, resistances, immunities).
/// </summary>
public sealed record TypeMatchups(IReadOnlyList<TypeMultiplierGroup> Groups)
{
    public static TypeMatchups Empty { get; } = new(Array.Empty<TypeMultiplierGroup>());

    public bool HasGroups => Groups.Count > 0;

    public static TypeMatchups For(TypeChart chart, IEnumerable<string?> types)
    {
        var defenders = types.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList();
        if (defenders.Count == 0) return Empty;
        return new TypeMatchups(chart.DamageTaken(defenders)
            .Where(x => x.Multiplier != 1)
            .GroupBy(x => x.Multiplier)
            .OrderByDescending(g => g.Key)
            .Select(g => new TypeMultiplierGroup(g.Key, g.Select(x => x.Attacking).ToList()))
            .ToList());
    }
}

public sealed partial class TypeChartViewModel : PageViewModel
{
    /// <summary>Choice of the second type meaning "single type".</summary>
    public const string NoType = "—";

    private readonly AppServices _services;
    public override string Title => "Type Chart";
    public override string Glyph => "⚔";

    [ObservableProperty] private IReadOnlyList<string> _types = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> _secondTypeOptions = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<TypeChartRow> _rows = Array.Empty<TypeChartRow>();

    [ObservableProperty] private string? _firstType;
    [ObservableProperty] private string? _secondType = NoType;
    [ObservableProperty] private IReadOnlyList<TypeMultiplierGroup> _groups = Array.Empty<TypeMultiplierGroup>();
    [ObservableProperty] private IReadOnlyList<string> _defenderTypes = Array.Empty<string>();

    public string ChartFile => _services.Data.FilePath(TypeChart.FileName);

    public TypeChartViewModel(AppServices services)
    {
        _services = services;
        Rebuild();
        services.GameDataChanged += Rebuild;
    }

    private TypeChart Chart => _services.Data.TypeChart;

    private void Rebuild()
    {
        var chart = Chart;
        Types = chart.Types;
        SecondTypeOptions = new[] { NoType }.Concat(chart.Types).ToList();
        Rows = chart.Types
            .Select(d => new TypeChartRow(d, chart.Types.Select(a => new TypeChartCell(a, d, chart.Multiplier(a, d))).ToList()))
            .ToList();
        if (FirstType is null || !chart.Types.Contains(FirstType)) FirstType = chart.Types.FirstOrDefault();
        if (SecondType is null || (SecondType != NoType && !chart.Types.Contains(SecondType))) SecondType = NoType;
        OnPropertyChanged(nameof(ChartFile));
        UpdateDefender();
    }

    partial void OnFirstTypeChanged(string? value) => UpdateDefender();
    partial void OnSecondTypeChanged(string? value) => UpdateDefender();

    /// <summary>Clicking a row header picks it as the defender.</summary>
    [RelayCommand]
    private void PickDefender(string type)
    {
        FirstType = type;
        SecondType = NoType;
    }

    private void UpdateDefender()
    {
        var defenders = new[] { FirstType, SecondType }
            .Where(t => !string.IsNullOrWhiteSpace(t) && t != NoType)
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        DefenderTypes = defenders;
        if (defenders.Count == 0)
        {
            Groups = Array.Empty<TypeMultiplierGroup>();
            return;
        }
        Groups = Chart.DamageTaken(defenders)
            .GroupBy(x => x.Multiplier)
            .OrderByDescending(g => g.Key)
            .Select(g => new TypeMultiplierGroup(g.Key, g.Select(x => x.Attacking).ToList()))
            .ToList();
    }

    [RelayCommand] private void OpenDataFolder() => _services.Dialogs.OpenInFileManager(_services.DataDir);
}
