using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.App.ViewModels;

/// <summary>A clickable column header: clicking sorts by it, clicking again reverses the order.</summary>
public sealed partial class SortHeader : ObservableObject
{
    public string Label { get; }
    public object Key { get; }
    public IRelayCommand Command { get; }
    [ObservableProperty] private string _text;
    [ObservableProperty] private bool _isActive;

    public SortHeader(string label, object key, Action<object> sortBy)
    {
        Label = label;
        Key = key;
        _text = label;
        Command = new RelayCommand(() => sortBy(key));
    }

    public void Update(object activeKey, bool descending)
    {
        IsActive = Equals(Key, activeKey);
        Text = IsActive ? $"{Label} {(descending ? "▼" : "▲")}" : Label;
    }
}

/// <summary>A Pokémon button in a "learned by" / "has it" list.</summary>
public sealed record PokemonLink(string Name, string Detail, ICommand Open)
{
    public string Text => Detail.Length > 0 ? $"{Formatting.TitleCase(Name)} · {Detail}" : Formatting.TitleCase(Name);
}

public sealed record PokemonLinkGroup(string Title, IReadOnlyList<PokemonLink> Pokemon)
{
    public string Header => $"{Title} ({Pokemon.Count})";
}

/// <summary>Shared plumbing of the Moves and Abilities pages: catalog rebuilt when the game data changes.</summary>
public abstract partial class ReferencePageViewModel : PageViewModel
{
    protected readonly AppServices Services;
    private readonly Action<string> _openPokemon;
    private GameCatalog? _catalog;

    public const string AnyOption = "All";

    [ObservableProperty] private IReadOnlyList<string> _pokemonNames = Array.Empty<string>();
    [ObservableProperty] private string _countText = "";

    protected ReferencePageViewModel(AppServices services, Action<string> openPokemon)
    {
        Services = services;
        _openPokemon = openPokemon;
        services.GameDataChanged += () =>
        {
            _catalog = null;
            Refresh();
        };
    }

    protected GameCatalog Catalog => _catalog ??= new GameCatalog(Services.Data);

    protected bool Ready { get; set; }

    public override void OnActivated()
    {
        if (!Ready)
        {
            Ready = true;
            Refresh();
        }
    }

    /// <summary>Rebuilds the option lists and the visible rows.</summary>
    protected void Refresh()
    {
        if (!Ready) return;
        PokemonNames = Services.Data.Pokemon.Select(p => p.Name).ToList();
        RebuildOptions();
        ApplyFilters();
    }

    protected abstract void RebuildOptions();
    protected abstract void ApplyFilters();

    protected PokemonLink Link(string pokemon, string detail) => new(pokemon, detail, new RelayCommand(() => _openPokemon(pokemon)));

    protected static string? Selected(string? option) => option is null or AnyOption ? null : option;

    /// <summary>Keeps the current choice when it still exists in the new option list.</summary>
    protected static string KeepOption(IReadOnlyList<string> options, string? current) =>
        current != null && options.Contains(current) ? current : AnyOption;
}

// ─── Moves ───────────────────────────────────────────────────────────────────

public sealed record MoveRow(MoveData Move, IReadOnlyList<string> Types, string Frequency, string AC, string DamageBase, string Roll, int Learners)
{
    public string Name => Move.Name;
    public string Class => Move.MoveClass ?? "";
    public string Range => Formatting.CollapseWhitespace(Move.Range);
}

public sealed partial class MovesViewModel : ReferencePageViewModel
{
    public override string Title => "Moves";
    public override string Glyph => "⚔";

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _searchEffects;
    [ObservableProperty] private IReadOnlyList<string> _typeOptions = new[] { AnyOption };
    [ObservableProperty] private string _selectedType = AnyOption;
    [ObservableProperty] private IReadOnlyList<string> _classOptions = new[] { AnyOption };
    [ObservableProperty] private string _selectedClass = AnyOption;
    [ObservableProperty] private IReadOnlyList<string> _frequencyOptions = new[] { AnyOption };
    [ObservableProperty] private string _selectedFrequency = AnyOption;
    [ObservableProperty] private string? _learnableBy;

    [ObservableProperty] private IReadOnlyList<MoveRow> _rows = Array.Empty<MoveRow>();
    [ObservableProperty] private MoveRow? _selectedRow;
    [ObservableProperty] private MoveInfo? _details;
    [ObservableProperty] private IReadOnlyList<PokemonLinkGroup> _learnerGroups = Array.Empty<PokemonLinkGroup>();
    [ObservableProperty] private string _learnersTitle = "";

    private MoveSortKey _sortKey;
    private bool _sortDescending;

    public SortHeader NameHeader { get; }
    public SortHeader TypeHeader { get; }
    public SortHeader ClassHeader { get; }
    public SortHeader FrequencyHeader { get; }
    public SortHeader AcHeader { get; }
    public SortHeader DamageHeader { get; }
    public SortHeader RangeHeader { get; }
    public SortHeader LearnersHeader { get; }
    private readonly SortHeader[] _headers;

    public bool HasSelection => SelectedRow != null;
    public bool HasNoLearners => SelectedRow != null && LearnerGroups.Count == 0;

    public MovesViewModel(AppServices services, Action<string> openPokemon) : base(services, openPokemon)
    {
        _sortKey = services.Settings.MoveSort;
        _sortDescending = services.Settings.MoveSortDescending;
        _headers = new[]
        {
            NameHeader = new SortHeader("Name", MoveSortKey.Name, SortBy),
            TypeHeader = new SortHeader("Type", MoveSortKey.Type, SortBy),
            ClassHeader = new SortHeader("Class", MoveSortKey.Class, SortBy),
            FrequencyHeader = new SortHeader("Frequency", MoveSortKey.Frequency, SortBy),
            AcHeader = new SortHeader("AC", MoveSortKey.AC, SortBy),
            DamageHeader = new SortHeader("DB", MoveSortKey.DamageBase, SortBy),
            RangeHeader = new SortHeader("Range", MoveSortKey.Range, SortBy),
            LearnersHeader = new SortHeader("Pokémon", MoveSortKey.Learners, SortBy),
        };
        UpdateHeaders();
    }

    private void SortBy(object key)
    {
        var k = (MoveSortKey)key;
        // Numbers read best biggest first; text A → Z.
        if (k == _sortKey) _sortDescending = !_sortDescending;
        else { _sortKey = k; _sortDescending = k is MoveSortKey.DamageBase or MoveSortKey.Learners; }
        Services.Settings.MoveSort = _sortKey;
        Services.Settings.MoveSortDescending = _sortDescending;
        Services.SaveSettings();
        UpdateHeaders();
        ApplyFilters();
    }

    private void UpdateHeaders()
    {
        foreach (var h in _headers) h.Update(_sortKey, _sortDescending);
    }

    partial void OnFilterTextChanged(string value) => ApplyFilters();
    partial void OnSearchEffectsChanged(bool value) => ApplyFilters();
    partial void OnSelectedTypeChanged(string value) => ApplyFilters();
    partial void OnSelectedClassChanged(string value) => ApplyFilters();
    partial void OnSelectedFrequencyChanged(string value) => ApplyFilters();
    partial void OnLearnableByChanged(string? value) => ApplyFilters();
    partial void OnSelectedRowChanged(MoveRow? value) => ShowDetails(value);
    partial void OnLearnerGroupsChanged(IReadOnlyList<PokemonLinkGroup> value) => OnPropertyChanged(nameof(HasNoLearners));

    protected override void RebuildOptions()
    {
        var moves = Services.Data.Moves;
        var types = moves.SelectMany(m => m.Types).Select(t => t.Trim()).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase);
        TypeOptions = new[] { AnyOption }.Concat(types).ToList();
        var classes = moves.Select(m => (m.MoveClass ?? "").Trim()).Where(c => c.Length > 0)
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key);
        ClassOptions = new[] { AnyOption }.Concat(classes).ToList();
        FrequencyOptions = new[] { AnyOption }.Concat(GameCatalog.SortFrequencies(moves.Select(m => GameCatalog.NormalizeFrequency(m.Frequency)))).ToList();
        SelectedType = KeepOption(TypeOptions, SelectedType);
        SelectedClass = KeepOption(ClassOptions, SelectedClass);
        SelectedFrequency = KeepOption(FrequencyOptions, SelectedFrequency);
    }

    protected override void ApplyFilters()
    {
        if (!Ready) return;
        var filter = new MoveFilter
        {
            Text = FilterText,
            SearchEffects = SearchEffects,
            Type = Selected(SelectedType),
            MoveClass = Selected(SelectedClass),
            Frequency = Selected(SelectedFrequency),
            LearnableBy = LearnableBy,
        };
        var catalog = Catalog;
        var selected = SelectedRow?.Name;
        Rows = catalog.SortMoves(catalog.FilterMoves(filter), _sortKey, _sortDescending).Select(m => new MoveRow(
            m,
            m.Types.Select(t => t.Trim()).Where(t => t.Length > 0).ToList(),
            m.Frequency ?? "",
            GameCatalog.ParseAC(m.AC)?.ToString() ?? "—",
            m.DamageBase > 0 ? m.DamageBase.ToString() : "—",
            m.DamageBase > 0 ? m.Roll ?? "" : "",
            catalog.LearnerCount(m.Name))).ToList();
        CountText = Rows.Count == Services.Data.Moves.Count ? $"{Rows.Count} moves" : $"{Rows.Count} of {Services.Data.Moves.Count} moves";
        SelectedRow = Rows.FirstOrDefault(r => r.Name == selected);
        if (SelectedRow is null) ShowDetails(null);
    }

    private void ShowDetails(MoveRow? row)
    {
        OnPropertyChanged(nameof(HasSelection));
        if (row is null)
        {
            Details = null;
            LearnerGroups = Array.Empty<PokemonLinkGroup>();
            LearnersTitle = "";
            return;
        }
        Details = MoveInfo.From(row.Name, Services.Data);
        var learners = Catalog.LearnersOf(row.Name);
        string[] order = { "Level-up", "Evolution", "TM", "Tutor", "Egg" };
        LearnerGroups = learners.GroupBy(l => l.Method)
            .OrderBy(g => Array.IndexOf(order, g.Key) is var i && i < 0 ? 99 : i)
            .Select(g => new PokemonLinkGroup(g.Key, g
                .OrderBy(l => l.Pokemon, StringComparer.OrdinalIgnoreCase)
                .Select(l => Link(l.Pokemon, l.Level is { } lv ? $"Lv {lv}" : "")).ToList()))
            .ToList();
        var count = learners.Select(l => l.Pokemon).Distinct().Count();
        LearnersTitle = $"Learned by {count} Pokémon";
    }

    [RelayCommand]
    private Task CopyMove() => SelectedRow is null ? Task.CompletedTask : PokedexViewModel.CopyMoveRow(Services, SelectedRow.Name);

    [RelayCommand]
    private void ClearFilters()
    {
        Ready = false; // one refresh instead of one per property
        FilterText = "";
        SearchEffects = false;
        SelectedType = AnyOption;
        SelectedClass = AnyOption;
        SelectedFrequency = AnyOption;
        LearnableBy = null;
        Ready = true;
        ApplyFilters();
    }
}

// ─── Abilities ───────────────────────────────────────────────────────────────

public sealed record AbilityRow(AbilityData Ability, string Frequency, string Summary, int Holders)
{
    public string Name => Ability.Name;
}

public sealed partial class AbilitiesViewModel : ReferencePageViewModel
{
    public override string Title => "Abilities";
    public override string Glyph => "✨";

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _searchEffects;
    [ObservableProperty] private IReadOnlyList<string> _frequencyOptions = new[] { AnyOption };
    [ObservableProperty] private string _selectedFrequency = AnyOption;
    [ObservableProperty] private string? _pokemon;
    [ObservableProperty] private bool _onlyUsed;

    [ObservableProperty] private IReadOnlyList<AbilityRow> _rows = Array.Empty<AbilityRow>();
    [ObservableProperty] private AbilityRow? _selectedRow;
    [ObservableProperty] private string _detailsEffect = "";
    [ObservableProperty] private IReadOnlyList<PokemonLinkGroup> _holderGroups = Array.Empty<PokemonLinkGroup>();
    [ObservableProperty] private string _holdersTitle = "";

    private AbilitySortKey _sortKey;
    private bool _sortDescending;

    public SortHeader NameHeader { get; }
    public SortHeader FrequencyHeader { get; }
    public SortHeader HoldersHeader { get; }
    private readonly SortHeader[] _headers;

    public bool HasSelection => SelectedRow != null;
    public bool HasNoHolders => SelectedRow != null && HolderGroups.Count == 0;

    public AbilitiesViewModel(AppServices services, Action<string> openPokemon) : base(services, openPokemon)
    {
        _sortKey = services.Settings.AbilitySort;
        _sortDescending = services.Settings.AbilitySortDescending;
        _headers = new[]
        {
            NameHeader = new SortHeader("Name", AbilitySortKey.Name, SortBy),
            FrequencyHeader = new SortHeader("Frequency", AbilitySortKey.Frequency, SortBy),
            HoldersHeader = new SortHeader("Pokémon", AbilitySortKey.Holders, SortBy),
        };
        UpdateHeaders();
    }

    private void SortBy(object key)
    {
        var k = (AbilitySortKey)key;
        if (k == _sortKey) _sortDescending = !_sortDescending;
        else { _sortKey = k; _sortDescending = k == AbilitySortKey.Holders; }
        Services.Settings.AbilitySort = _sortKey;
        Services.Settings.AbilitySortDescending = _sortDescending;
        Services.SaveSettings();
        UpdateHeaders();
        ApplyFilters();
    }

    private void UpdateHeaders()
    {
        foreach (var h in _headers) h.Update(_sortKey, _sortDescending);
    }

    partial void OnFilterTextChanged(string value) => ApplyFilters();
    partial void OnSearchEffectsChanged(bool value) => ApplyFilters();
    partial void OnSelectedFrequencyChanged(string value) => ApplyFilters();
    partial void OnPokemonChanged(string? value) => ApplyFilters();
    partial void OnOnlyUsedChanged(bool value) => ApplyFilters();
    partial void OnSelectedRowChanged(AbilityRow? value) => ShowDetails(value);
    partial void OnHolderGroupsChanged(IReadOnlyList<PokemonLinkGroup> value) => OnPropertyChanged(nameof(HasNoHolders));

    protected override void RebuildOptions()
    {
        FrequencyOptions = new[] { AnyOption }
            .Concat(GameCatalog.SortFrequencies(Services.Data.Abilities.Select(GameCatalog.AbilityFrequency))).ToList();
        SelectedFrequency = KeepOption(FrequencyOptions, SelectedFrequency);
    }

    /// <summary>The effect text without its frequency line, on one line.</summary>
    private static string Summary(AbilityData a)
    {
        var lines = (a.Effect ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count > 1 && GameCatalog.NormalizeFrequency(lines[0]) != "Other") lines.RemoveAt(0);
        lines = lines.Select(l => l.StartsWith("Effect:", StringComparison.OrdinalIgnoreCase) ? l[7..].Trim() : l).ToList();
        var s = Formatting.CollapseWhitespace(string.Join(" ", lines.Where(l => !l.StartsWith("Keywords", StringComparison.OrdinalIgnoreCase))));
        return s.Length > 220 ? s[..220].TrimEnd() + "…" : s;
    }

    protected override void ApplyFilters()
    {
        if (!Ready) return;
        var filter = new AbilityFilter
        {
            Text = FilterText,
            SearchEffects = SearchEffects,
            Frequency = Selected(SelectedFrequency),
            Pokemon = Pokemon,
            OnlyUsed = OnlyUsed,
        };
        var catalog = Catalog;
        var selected = SelectedRow?.Name;
        Rows = catalog.SortAbilities(catalog.FilterAbilities(filter), _sortKey, _sortDescending)
            .Select(a => new AbilityRow(a, GameCatalog.AbilityFrequency(a), Summary(a), catalog.HolderCount(a.Name))).ToList();
        var total = Services.Data.Abilities.Count;
        CountText = Rows.Count == total ? $"{Rows.Count} abilities" : $"{Rows.Count} of {total} abilities";
        SelectedRow = Rows.FirstOrDefault(r => r.Name == selected);
        if (SelectedRow is null) ShowDetails(null);
    }

    private void ShowDetails(AbilityRow? row)
    {
        OnPropertyChanged(nameof(HasSelection));
        if (row is null)
        {
            DetailsEffect = "";
            HolderGroups = Array.Empty<PokemonLinkGroup>();
            HoldersTitle = "";
            return;
        }
        DetailsEffect = string.IsNullOrWhiteSpace(row.Ability.Effect) ? "No description." : row.Ability.Effect.Trim();
        var holders = Catalog.HoldersOf(row.Name);
        string[] order = { "Base", "Advanced", "High", "Mega" };
        HolderGroups = holders.GroupBy(h => h.Tier)
            .OrderBy(g => Array.IndexOf(order, g.Key) is var i && i < 0 ? 99 : i).ThenBy(g => g.Key)
            .Select(g => new PokemonLinkGroup(g.Key, g.OrderBy(h => h.Pokemon, StringComparer.OrdinalIgnoreCase)
                .Select(h => Link(h.Pokemon, "")).ToList()))
            .ToList();
        HoldersTitle = $"Pokémon with this ability: {holders.Select(h => h.Pokemon).Distinct().Count()}";
    }

    [RelayCommand]
    private async Task CopyAbility()
    {
        if (SelectedRow is null) return;
        var text = $"{SelectedRow.Name}\n{SelectedRow.Ability.Effect?.Trim()}";
        await Services.Dialogs.CopyTextAsync(text);
        Services.Dialogs.Notify($"Copied: {SelectedRow.Name}");
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Ready = false;
        FilterText = "";
        SearchEffects = false;
        SelectedFrequency = AnyOption;
        Pokemon = null;
        OnlyUsed = false;
        Ready = true;
        ApplyFilters();
    }
}
