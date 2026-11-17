using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.ViewModels;

public enum SheetColumn { Nickname, Folder, Pokemon, Type, Level, Rarity, Card, Status, Nature, Hp, StatHp, Atk, Def, SpAtk, SpDef, Spd, Total, Updated }

/// <summary>One sheet as a database row: identity, final stats and current HP.</summary>
public sealed class SheetDbRow
{
    public required SheetRecord Record { get; init; }
    public required IReadOnlyList<string> Types { get; init; }
    public required string SpeciesName { get; init; }
    public required string Nature { get; init; }
    public required SheetIssues Issues { get; init; }
    public required bool IsKnownSpecies { get; init; }
    /// <summary>Final stats by key (HP, ATK…), empty when the species isn't in the Pokédex.</summary>
    public required IReadOnlyDictionary<string, int> Stats { get; init; }
    public required int MaxHp { get; init; }
    public required int CurrentHp { get; init; }
    public required int Injuries { get; init; }
    public required ICommand Open { get; init; }
    public required string SearchIndex { get; init; }

    public string Id => Record.Id;
    public string Nickname => string.IsNullOrWhiteSpace(Record.Nickname) ? "(no nickname)" : Record.Nickname;
    public double LevelValue => Record.State.Level;
    public string Level => Record.State.Level > 0 ? SheetRules.FormatLevel(Record.State.Level) : "—";
    public string Rarity => string.IsNullOrEmpty(Record.State.Rarity) ? "Normal" : Record.State.Rarity;
    public string Card => string.IsNullOrEmpty(Record.State.Card) ? "None" : Record.State.Card;
    public string Status => Record.Status;
    public string Folder => SheetFolders.Normalize(Record.Folder);
    public string HpText => IsKnownSpecies ? $"{CurrentHp}/{MaxHp}" : "—";
    public string HpTip => Injuries > 0 ? $"{Injuries} {(Injuries > 1 ? "injuries" : "injury")}" : "No injury";
    public bool IsHurt => IsKnownSpecies && CurrentHp < MaxHp;
    public bool IsFainted => IsKnownSpecies && CurrentHp <= 0;
    public int Total => Stats.Values.Sum();
    public string Updated => Record.UpdatedAt.ToLocalTime().ToString("g");
    public string Warning => Issues.HasIssues ? string.Join("\n", Issues.All.Select(i => "• " + i)) : "";
    public bool HasWarning => Issues.HasIssues;
    public bool HasErrors => Issues.IsIncorrect;

    public string Stat(string key) => Stats.TryGetValue(key, out var v) ? v.ToString() : "—";
    public string StatHp => Stat("HP");
    public string Atk => Stat("ATK");
    public string Def => Stat("DEF");
    public string SpAtk => Stat("SPATK");
    public string SpDef => Stat("SPDEF");
    public string Spd => Stat("SPD");
    public string TotalText => Stats.Count == 0 ? "—" : Total.ToString();
}

/// <summary>
/// "Sheet list" page: every saved sheet in one table (database view) with filters and sortable
/// columns. Opening a row shows the sheet on the Sheets page.
/// </summary>
public sealed partial class SheetDatabaseViewModel : PageViewModel
{
    public const string AnyOption = "All";
    private readonly AppServices _services;
    private readonly Action<string> _openSheet;
    private List<SheetDbRow> _all = new();
    private bool _dirty = true;

    public override string Title => "Sheet list";
    public override string Glyph => "🗂";

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private IReadOnlyList<string> _statusOptions;
    [ObservableProperty] private string _selectedStatus = AnyOption;
    [ObservableProperty] private IReadOnlyList<string> _typeOptions;
    [ObservableProperty] private string _selectedType = AnyOption;
    [ObservableProperty] private IReadOnlyList<string> _rarityOptions;
    [ObservableProperty] private string _selectedRarity = AnyOption;
    [ObservableProperty] private bool _onlyHurt;
    [ObservableProperty] private bool _onlyIssues;
    [ObservableProperty] private IReadOnlyList<FolderOption> _folderOptions = Array.Empty<FolderOption>();
    [ObservableProperty] private FolderOption? _selectedFolder;
    [ObservableProperty] private IReadOnlyList<SheetDbRow> _rows = Array.Empty<SheetDbRow>();
    [ObservableProperty] private SheetDbRow? _selectedRow;
    [ObservableProperty] private string _countText = "";

    private SheetColumn _sortKey = SheetColumn.Nickname;
    private bool _sortDescending;
    private readonly SortHeader[] _headers;
    public IReadOnlyDictionary<SheetColumn, SortHeader> Headers { get; }

    public SortHeader NicknameHeader => Headers[SheetColumn.Nickname];
    public SortHeader FolderHeader => Headers[SheetColumn.Folder];
    public SortHeader PokemonHeader => Headers[SheetColumn.Pokemon];
    public SortHeader TypeHeader => Headers[SheetColumn.Type];
    public SortHeader LevelHeader => Headers[SheetColumn.Level];
    public SortHeader RarityHeader => Headers[SheetColumn.Rarity];
    public SortHeader CardHeader => Headers[SheetColumn.Card];
    public SortHeader StatusHeader => Headers[SheetColumn.Status];
    public SortHeader NatureHeader => Headers[SheetColumn.Nature];
    public SortHeader HpHeader => Headers[SheetColumn.Hp];
    public SortHeader StatHpHeader => Headers[SheetColumn.StatHp];
    public SortHeader AtkHeader => Headers[SheetColumn.Atk];
    public SortHeader DefHeader => Headers[SheetColumn.Def];
    public SortHeader SpAtkHeader => Headers[SheetColumn.SpAtk];
    public SortHeader SpDefHeader => Headers[SheetColumn.SpDef];
    public SortHeader SpdHeader => Headers[SheetColumn.Spd];
    public SortHeader TotalHeader => Headers[SheetColumn.Total];
    public SortHeader UpdatedHeader => Headers[SheetColumn.Updated];

    public bool HasNoSheets => _all.Count == 0;
    public bool HasNoMatch => _all.Count > 0 && Rows.Count == 0;

    public SheetDatabaseViewModel(AppServices services, Action<string> openSheet)
    {
        _services = services;
        _openSheet = openSheet;
        _statusOptions = new[] { AnyOption }.Concat(SheetRules.Statuses.Reverse()).ToList();
        _typeOptions = new[] { AnyOption }.Concat(SheetRules.AllTypes).ToList();
        _rarityOptions = new[] { AnyOption }.Concat(SheetRules.Rarities).ToList();
        var headers = new (SheetColumn Key, string Label)[]
        {
            (SheetColumn.Nickname, "Nickname"), (SheetColumn.Folder, "Folder"), (SheetColumn.Pokemon, "Pokémon"), (SheetColumn.Type, "Type"),
            (SheetColumn.Level, "Lv"), (SheetColumn.Rarity, "Rarity"), (SheetColumn.Card, "Card"),
            (SheetColumn.Status, "Status"), (SheetColumn.Nature, "Nature"), (SheetColumn.Hp, "HP now"),
            (SheetColumn.StatHp, "HP"), (SheetColumn.Atk, "ATK"), (SheetColumn.Def, "DEF"),
            (SheetColumn.SpAtk, "SP.ATK"), (SheetColumn.SpDef, "SP.DEF"), (SheetColumn.Spd, "SPD"),
            (SheetColumn.Total, "Total"), (SheetColumn.Updated, "Updated"),
        };
        Headers = headers.ToDictionary(h => h.Key, h => new SortHeader(h.Label, h.Key, SortBy));
        _headers = Headers.Values.ToArray();
        UpdateHeaders();
        services.LibraryChanged += () => { _dirty = true; Reload(); };
        services.GameDataChanged += () => { _dirty = true; Reload(); };
    }

    public override void OnActivated()
    {
        // Play tracking (HP) and edits don't raise LibraryChanged: re-read when the files changed.
        if (!_diskWatch.HasChanged) return;
        _dirty = true;
        Reload();
        _diskWatch.MarkSeen();
    }

    private DiskChangeWatch _diskWatch => _diskWatchField ??= new(() => _services.Library.Sheets.Directory);
    private DiskChangeWatch? _diskWatchField;

    private void Reload()
    {
        if (!_dirty) return;
        _dirty = false;
        var records = _services.Library.Sheets.List();
        _all = records.Select(BuildRow).ToList();
        var folder = SelectedFolder?.Path;
        FolderOptions = FolderOption.ForFilter(records, _services.Settings);
        SelectedFolder = FolderOptions.FirstOrDefault(o => string.Equals(o.Path, folder, StringComparison.OrdinalIgnoreCase)) ?? FolderOptions[0];
        OnPropertyChanged(nameof(HasNoSheets));
        ApplyFilters();
    }

    private SheetDbRow BuildRow(SheetRecord r)
    {
        var state = r.State;
        var name = string.IsNullOrEmpty(state.ChosenPokemon) ? r.PokemonName : state.ChosenPokemon;
        var species = _services.Data.FindPokemon(name);
        IReadOnlyDictionary<string, int> stats = new Dictionary<string, int>();
        int maxHp = 0, currentHp = 0, injuries = 0;
        if (species != null)
        {
            var final = SheetRules.ComputeFinalStats(species, state.Card, state.Rarity, state.BuffedStat, state.LoweredStat, state.PointsByStat);
            stats = final;
            var hp = (r.Play ?? new SheetPlayState()).HpStatus(SheetRules.HitPoints(state.Level, final["HP"]));
            (maxHp, currentHp, injuries) = (hp.MaxHp, hp.CurrentHp, hp.Injuries);
        }
        var id = r.Id;
        return new SheetDbRow
        {
            Record = r,
            Types = species?.PokemonTypes ?? (IReadOnlyList<string>)Array.Empty<string>(),
            SpeciesName = Formatting.TitleCase(species?.Name ?? name),
            Nature = SheetRules.NatureName(state.BuffedStat, state.LoweredStat),
            Issues = SheetValidation.Check(state.ChosenPokemon == "" ? WithName(state, name) : state, _services.Data),
            IsKnownSpecies = species != null,
            Stats = stats,
            MaxHp = maxHp,
            CurrentHp = currentHp,
            Injuries = injuries,
            Open = new RelayCommand(() => _openSheet(id)),
            SearchIndex = SheetSearch.BuildIndex(r, species),
        };
    }

    private static SheetState WithName(SheetState state, string name)
    {
        var copy = state.Clone();
        copy.ChosenPokemon = name;
        return copy;
    }

    partial void OnFilterTextChanged(string value) => ApplyFilters();
    partial void OnOnlyIssuesChanged(bool value) => ApplyFilters();
    partial void OnSelectedStatusChanged(string value) => ApplyFilters();
    partial void OnSelectedTypeChanged(string value) => ApplyFilters();
    partial void OnSelectedRarityChanged(string value) => ApplyFilters();
    partial void OnOnlyHurtChanged(bool value) => ApplyFilters();
    partial void OnSelectedFolderChanged(FolderOption? value) => ApplyFilters();
    partial void OnRowsChanged(IReadOnlyList<SheetDbRow> value) => OnPropertyChanged(nameof(HasNoMatch));

    private static string? Selected(string? option) => option is null or AnyOption ? null : option;

    private void ApplyFilters()
    {
        var text = FilterText;
        var status = Selected(SelectedStatus);
        var type = Selected(SelectedType);
        var rarity = Selected(SelectedRarity);
        IEnumerable<SheetDbRow> rows = _all;
        if (SheetSearch.Terms(text).Count > 0) rows = rows.Where(r => SheetSearch.Matches(r.SearchIndex, text));
        if (status != null) rows = rows.Where(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));
        if (type != null) rows = rows.Where(r => r.Types.Any(t => string.Equals(t.Trim(), type, StringComparison.OrdinalIgnoreCase)));
        if (rarity != null) rows = rows.Where(r => string.Equals(r.Rarity, rarity, StringComparison.OrdinalIgnoreCase));
        if (SelectedFolder?.Path is { } folder) rows = rows.Where(r => SheetFolders.IsInside(r.Folder, folder));
        if (OnlyIssues) rows = rows.Where(r => r.Issues.HasIssues);
        if (OnlyHurt) rows = rows.Where(r => r.IsHurt || r.Injuries > 0);

        var selected = SelectedRow?.Id;
        Rows = Sort(rows).ToList();
        SelectedRow = Rows.FirstOrDefault(r => r.Id == selected);
        CountText = Rows.Count == _all.Count ? $"{Rows.Count} sheets" : $"{Rows.Count} of {_all.Count} sheets";
    }

    private static int RarityRank(string rarity) => Array.FindIndex(SheetRules.Rarities, x => x.Equals(rarity, StringComparison.OrdinalIgnoreCase));
    private static int CardRank(string card) => Array.FindIndex(SheetRules.Cards, x => x.Equals(card, StringComparison.OrdinalIgnoreCase));
    private static int StatusRank(string status) => Array.FindIndex(SheetRules.Statuses, x => x.Equals(status, StringComparison.OrdinalIgnoreCase));
    /// <summary>Missing stats sort as the lowest value.</summary>
    private static int StatValue(SheetDbRow r, string key) => r.Stats.TryGetValue(key, out var v) ? v : int.MinValue;

    private IEnumerable<SheetDbRow> Sort(IEnumerable<SheetDbRow> rows)
    {
        var cmp = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<SheetDbRow> ordered = _sortKey switch
        {
            SheetColumn.Folder => Order(rows, r => r.Folder, cmp),
            SheetColumn.Pokemon => Order(rows, r => r.SpeciesName, cmp),
            SheetColumn.Type => Order(rows, r => r.Types.FirstOrDefault() ?? "", cmp),
            SheetColumn.Level => Order(rows, r => r.LevelValue),
            SheetColumn.Rarity => Order(rows, r => RarityRank(r.Rarity)),
            SheetColumn.Card => Order(rows, r => CardRank(r.Card)),
            SheetColumn.Status => Order(rows, r => StatusRank(r.Status)),
            SheetColumn.Nature => Order(rows, r => r.Nature, cmp),
            SheetColumn.Hp => Order(rows, r => r.MaxHp <= 0 ? -1 : (double)r.CurrentHp / r.MaxHp),
            SheetColumn.StatHp => Order(rows, r => StatValue(r, "HP")),
            SheetColumn.Atk => Order(rows, r => StatValue(r, "ATK")),
            SheetColumn.Def => Order(rows, r => StatValue(r, "DEF")),
            SheetColumn.SpAtk => Order(rows, r => StatValue(r, "SPATK")),
            SheetColumn.SpDef => Order(rows, r => StatValue(r, "SPDEF")),
            SheetColumn.Spd => Order(rows, r => StatValue(r, "SPD")),
            SheetColumn.Total => Order(rows, r => r.Stats.Count == 0 ? int.MinValue : r.Total),
            SheetColumn.Updated => Order(rows, r => r.Record.UpdatedAt),
            _ => Order(rows, r => r.Nickname, cmp),
        };
        return ordered.ThenBy(r => r.Nickname, cmp).ThenBy(r => r.SpeciesName, cmp);
    }

    private IOrderedEnumerable<SheetDbRow> Order<T>(IEnumerable<SheetDbRow> rows, Func<SheetDbRow, T> key, IComparer<T>? comparer = null) =>
        _sortDescending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);

    private void SortBy(object key)
    {
        var k = (SheetColumn)key;
        // Text A → Z; numbers, ranks and dates biggest first.
        if (k == _sortKey) _sortDescending = !_sortDescending;
        else
        {
            _sortKey = k;
            _sortDescending = k is not (SheetColumn.Nickname or SheetColumn.Folder or SheetColumn.Pokemon or SheetColumn.Type or SheetColumn.Nature or SheetColumn.Hp);
        }
        UpdateHeaders();
        ApplyFilters();
    }

    private void UpdateHeaders()
    {
        foreach (var h in _headers) h.Update(_sortKey, _sortDescending);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FilterText = "";
        SelectedStatus = AnyOption;
        SelectedType = AnyOption;
        SelectedRarity = AnyOption;
        OnlyHurt = false;
        OnlyIssues = false;
        SelectedFolder = FolderOptions.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedRow != null) _openSheet(SelectedRow.Id);
    }
}
