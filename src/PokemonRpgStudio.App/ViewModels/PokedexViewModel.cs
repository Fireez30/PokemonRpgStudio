using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.App.ViewModels;

/// <summary>Move details for tooltips and tables.</summary>
public sealed record MoveInfo(string Name, bool Found, IReadOnlyList<string> Types, string Frequency, string AC, string Damage, string Class, string Range, string Effect)
{
    public bool HasEffect => !string.IsNullOrWhiteSpace(Effect);
    public bool HasDamage => Damage != "—";

    public static MoveInfo From(string name, GameData data, IReadOnlyList<string>? fallbackTypes = null)
    {
        var m = data.FindMove(name);
        if (m is null) return new MoveInfo(name.Trim(), false, fallbackTypes ?? Array.Empty<string>(), "", "", "—", "", "", "");
        var ac = m.AC is "0" or "/" or "" or null ? "--" : m.AC;
        var dmg = m.DamageBase > 0 ? $"DB {m.DamageBase} · {m.Roll}" : "—";
        var effect = m.Effect is "--" ? "" : m.Effect ?? "";
        return new MoveInfo(m.Name, true, m.Types.Select(t => t.Trim()).ToList(), m.Frequency ?? "", ac, dmg, m.MoveClass ?? "", m.Range ?? "", effect);
    }
}

public sealed record AbilityInfo(string Name, string Effect)
{
    public string Tip => string.IsNullOrWhiteSpace(Effect) ? $"No data for \"{Name}\"" : Effect;
}

public sealed record AbilityGroup(string Tier, IReadOnlyList<AbilityInfo> Abilities);

public sealed record KeyValueRow(string Key, string Value);

public sealed record StatBar(string Label, int Value, double Width);

public sealed record LevelMoveRow(string Level, MoveInfo Info, string Roll, ICommand Copy);

public sealed record MovePill(MoveInfo Info, ICommand Copy);

public sealed record MovePillGroup(string Title, IReadOnlyList<MovePill> Moves)
{
    public bool IsEmpty => Moves.Count == 0;
}

public sealed record EvolutionStepVm(string Name, string Condition, bool IsCurrent, bool IsLink, ICommand Open)
{
    public bool HasCondition => Condition.Length > 0;
}

/// <summary>A Pokémon card in the type-filtered browse list.</summary>
public sealed record PokemonListItem(string Name, IReadOnlyList<string> Types, ICommand Open);

public sealed record EvolutionGroupVm(IReadOnlyList<EvolutionStepVm> Steps, bool ShowArrow);

public sealed record MegaStatVm(string Label, string Value, bool Positive, bool Negative);

public sealed partial class PokedexViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly Action<PokemonSpecies> _createSheet;

    public override string Title => "Pokédex";
    public override string Glyph => "📖";

    [ObservableProperty] private IReadOnlyList<string> _pokemonNames = Array.Empty<string>();
    [ObservableProperty] private string? _selectedName;
    [ObservableProperty] private string _selectedTypeFilter = AllTypesOption;

    public const string AllTypesOption = "All types";
    public IReadOnlyList<string> TypeFilterOptions { get; } = new[] { AllTypesOption }.Concat(SheetRules.AllTypes).ToList();
    private bool _refreshingNames;
    [ObservableProperty] private IReadOnlyList<PokemonListItem> _filteredPokemon = Array.Empty<PokemonListItem>();

    public bool IsTypeFiltered => SelectedTypeFilter != AllTypesOption && !string.IsNullOrEmpty(SelectedTypeFilter);
    /// <summary>The browse grid shows when a type is chosen and no Pokémon is open.</summary>
    public bool ShowTypeList => IsTypeFiltered && Species == null && Editor == null;
    public bool ShowPlaceholder => !IsTypeFiltered && Species == null;
    public string TypeListTitle => $"{SelectedTypeFilter} Pokémon ({FilteredPokemon.Count})";
    [ObservableProperty] private PokemonSpecies? _species;
    /// <summary>Weaknesses / resistances / immunities of the species' types (×1 left out).</summary>
    [ObservableProperty] private TypeMatchups _matchups = TypeMatchups.Empty;
    [ObservableProperty] private SpeciesEditorViewModel? _editor;

    [ObservableProperty] private IReadOnlyList<StatBar> _stats = Array.Empty<StatBar>();
    [ObservableProperty] private IReadOnlyList<KeyValueRow> _info = Array.Empty<KeyValueRow>();
    [ObservableProperty] private IReadOnlyList<EvolutionGroupVm> _evolutions = Array.Empty<EvolutionGroupVm>();
    [ObservableProperty] private IReadOnlyList<AbilityGroup> _abilityGroups = Array.Empty<AbilityGroup>();
    [ObservableProperty] private IReadOnlyList<KeyValueRow> _capabilities = Array.Empty<KeyValueRow>();
    [ObservableProperty] private IReadOnlyList<KeyValueRow> _skills = Array.Empty<KeyValueRow>();
    [ObservableProperty] private IReadOnlyList<LevelMoveRow> _levelMoves = Array.Empty<LevelMoveRow>();
    [ObservableProperty] private IReadOnlyList<MovePillGroup> _otherMoves = Array.Empty<MovePillGroup>();
    [ObservableProperty] private bool _hasMega;
    [ObservableProperty] private string _megaTitle = "";
    [ObservableProperty] private string? _megaImage;
    [ObservableProperty] private IReadOnlyList<string> _megaTypes = Array.Empty<string>();
    [ObservableProperty] private AbilityInfo? _megaAbility;
    [ObservableProperty] private IReadOnlyList<MegaStatVm> _megaStats = Array.Empty<MegaStatVm>();

    public bool IsViewing => Species != null && Editor == null;
    public bool HasEvolutions => Evolutions.Count > 0;

    partial void OnEvolutionsChanged(IReadOnlyList<EvolutionGroupVm> value) => OnPropertyChanged(nameof(HasEvolutions));
    public bool IsEditing => Editor != null;

    public PokedexViewModel(AppServices services, Action<PokemonSpecies> createSheet)
    {
        _services = services;
        _createSheet = createSheet;
        RefreshNames();
        services.GameDataChanged += () =>
        {
            RefreshNames();
            Show(SelectedName);
        };
    }

    partial void OnSelectedTypeFilterChanged(string value)
    {
        RefreshNames();
        // Choosing a type closes the open sheet so its Pokémon list is shown right away.
        if (IsTypeFiltered) SelectedName = null;
        OnPropertyChanged(nameof(IsTypeFiltered));
        OnPropertyChanged(nameof(ShowTypeList));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }

    partial void OnFilteredPokemonChanged(IReadOnlyList<PokemonListItem> value) => OnPropertyChanged(nameof(TypeListTitle));

    /// <summary>Rebuilds the searchable list, keeping only Pokémon of the selected type.</summary>
    private void RefreshNames()
    {
        var filter = SelectedTypeFilter;
        var all = string.IsNullOrEmpty(filter) || filter == AllTypesOption;
        var selected = SelectedName;
        _refreshingNames = true;
        try
        {
            var matches = _services.Data.Pokemon
                .Where(p => all || p.PokemonTypes.Any(t => t.Trim().Equals(filter, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            PokemonNames = matches.Select(p => p.Name).ToList();
            FilteredPokemon = all ? Array.Empty<PokemonListItem>() : matches.Select(p =>
            {
                var name = p.Name;
                return new PokemonListItem(name, p.PokemonTypes.Select(t => t.Trim()).ToList(), new RelayCommand(() => SelectedName = name));
            }).ToList();
        }
        finally { _refreshingNames = false; }
        // Changing the list can make the search box drop its selection; keep the current sheet visible.
        if (SelectedName != selected) SelectedName = selected;
    }

    partial void OnSelectedNameChanged(string? value)
    {
        if (_refreshingNames) return;
        Editor = null;
        Show(value);
    }

    partial void OnSpeciesChanged(PokemonSpecies? value)
    {
        OnPropertyChanged(nameof(IsViewing));
        OnPropertyChanged(nameof(ShowTypeList));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }

    partial void OnEditorChanged(SpeciesEditorViewModel? value)
    {
        OnPropertyChanged(nameof(IsViewing));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ShowTypeList));
    }

    private void Show(string? name)
    {
        var data = _services.Data;
        var sp = data.FindPokemon(name);
        Species = sp;
        Matchups = sp is null ? TypeMatchups.Empty : TypeMatchups.For(data.TypeChart, sp.PokemonTypes);
        if (sp is null) return;

        Stats = SheetRules.AllStats.Select(s => new StatBar(StatLabel(s), sp.BaseStat(s), Math.Min(100, sp.BaseStat(s) / 20.0 * 100) * 1.6)).ToList();
        Info = new List<KeyValueRow>
        {
            new("Height", sp.Height ?? ""),
            new("Weight", sp.Weight ?? ""),
            new("Gender", sp.IsGenderless ? "Genderless" : $"♂ {sp.GenderRatioM?.ToString() ?? "?"}%   ♀ {sp.GenderRatioF?.ToString() ?? "?"}%"),
            new("Egg group", sp.EggGroup ?? ""),
            new("Hatch rate", sp.AverageHatchRate ?? ""),
            new("Diet", sp.Diet ?? ""),
            new("Habitat", sp.Habitat ?? ""),
        };

        var groups = PokedexHelpers.BuildEvolutionGroups(sp.Evolutions, data);
        Evolutions = groups.Select((g, gi) => new EvolutionGroupVm(g.Select(step =>
        {
            bool isCurrent = PokedexHelpers.NormLI(sp.Name) == PokedexHelpers.NormLI(step.Name);
            var target = step.Match?.Name;
            return new EvolutionStepVm(step.Name, step.Condition, isCurrent, step.Match != null && !isCurrent,
                new RelayCommand(() => { if (target != null && !isCurrent) SelectedName = target; }));
        }).ToList(), gi > 0)).ToList();

        AbilityInfo Ability(string n) => new(n.Trim(), data.FindAbility(n)?.Effect ?? "");
        var abilityGroups = new List<AbilityGroup>();
        if (sp.BaseAbilities.Count > 0) abilityGroups.Add(new("Base", sp.BaseAbilities.Select(a => Ability(a.Name)).ToList()));
        if (sp.AdvancedAbilities.Count > 0) abilityGroups.Add(new("Advanced", sp.AdvancedAbilities.Select(a => Ability(a.Name)).ToList()));
        if (sp.HighAbilities.Count > 0) abilityGroups.Add(new("High", sp.HighAbilities.Select(a => Ability(a.Name)).ToList()));
        foreach (var (section, ability) in sp.CustomAbilities ?? new())
            abilityGroups.Add(new(section, new[] { Ability(ability.Name) }));
        AbilityGroups = abilityGroups;

        Capabilities = sp.Capabilities.Select(c => new KeyValueRow(c.Name, c.Value ?? "")).ToList();
        Skills = sp.Skills.Select(c => new KeyValueRow(c.Name, c.Roll ?? "")).ToList();

        LevelMoves = sp.Moves.OrderBy(m => m.Level).Select(m =>
        {
            var info = MoveInfo.From(m.Name, data, m.AllTypes);
            var move = data.FindMove(m.Name);
            var roll = move is { DamageBase: > 0 } ? move.Roll ?? "—" : "—";
            return new LevelMoveRow(m.Level == 0 ? "Evo" : m.Level.ToString(), info, roll, new AsyncRelayCommand(() => CopyMove(m.Name)));
        }).ToList();

        MovePillGroup Pills(string title, IEnumerable<LearnsetMove> list) => new(title,
            list.Select(m => m.Name.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => new MovePill(MoveInfo.From(n, data), new AsyncRelayCommand(() => CopyMove(n)))).ToList());
        OtherMoves = new[] { Pills("TM Moves", sp.TmMoves), Pills("Tutor Moves", sp.TutorMoves), Pills("Egg Moves", sp.EggMoves) };

        var mega = PokedexHelpers.ParseMega(sp);
        HasMega = mega != null;
        if (mega != null)
        {
            MegaTitle = "Mega " + sp.DisplayName;
            MegaImage = _services.Images.ResolveMega(sp);
            MegaTypes = mega.Types.Where(t => !t.Equals("Unchanged", StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty("Unchanged").ToList();
            MegaAbility = string.IsNullOrWhiteSpace(mega.Ability) ? null : Ability(mega.Ability);
            MegaStats = mega.Stats.Select(s =>
            {
                int.TryParse(s.Value.Replace("+", ""), out var n);
                return new MegaStatVm(s.Label, s.Value, n > 0, n < 0);
            }).ToList();
        }
    }

    private static string StatLabel(Stat s) => s switch
    {
        Stat.SPATK => "SP.ATK",
        Stat.SPDEF => "SP.DEF",
        _ => s.ToString(),
    };

    private Task CopyMove(string name) => CopyMoveRow(_services, name);

    /// <summary>Copies a move as one table row (HTML for Docs/Sheets, TSV as plain text).</summary>
    internal static async Task CopyMoveRow(AppServices services, string name)
    {
        var m = services.Data.FindMove(name);
        if (m is null)
        {
            services.Dialogs.Notify($"No data for \"{name}\".", isError: true);
            return;
        }
        var cells = new[]
        {
            m.Name, m.Frequency ?? "", (m.DamageBase > 0 ? m.DamageBase : 0).ToString(), string.Join("/", m.Types.Select(t => t.Trim())),
            m.DamageBase > 0 ? m.Roll ?? "--" : "--", m.MoveClass ?? "", m.Range ?? "", m.Effect is "--" ? "" : m.Effect ?? "",
        };
        var tsv = string.Join("\t", cells);
        var html = "<table><tbody><tr>" + string.Concat(cells.Select(c => $"<td>{WebUtility.HtmlEncode(c)}</td>")) + "</tr></tbody></table>";
        await services.Dialogs.CopyHtmlAsync(html, tsv);
        services.Dialogs.Notify($"Copied: {m.Name}");
    }

    [RelayCommand]
    private void CreateSheet()
    {
        if (Species != null) _createSheet(Species);
    }

    [RelayCommand]
    private void Edit()
    {
        if (Species != null) Editor = new SpeciesEditorViewModel(Species, _services.Data);
    }

    [RelayCommand]
    private void CancelEdit() => Editor = null;

    [RelayCommand]
    private void SaveEdit()
    {
        if (Editor is null || Species is null) return;
        try
        {
            var updated = Editor.Build();
            if (string.IsNullOrWhiteSpace(updated.Name)) throw new InvalidOperationException("The name can't be empty.");
            if (!updated.Name.Equals(Species.Name, StringComparison.OrdinalIgnoreCase) && _services.Data.FindPokemon(updated.Name) != null)
                throw new InvalidOperationException($"Another Pokémon is already named '{updated.Name}'.");
            _services.Data.SaveSpecies(Species.Name, updated);
            Editor = null;
            _services.NotifyGameDataChanged();
            SelectedName = updated.Name;
            Show(updated.Name);
            _services.Dialogs.Notify($"{updated.DisplayName} saved.");
        }
        catch (Exception ex)
        {
            _services.Dialogs.Notify("Could not save: " + ex.Message, isError: true);
        }
    }
}

// ─── Species editor ──────────────────────────────────────────────────────────

public sealed partial class EditableText : ObservableObject
{
    [ObservableProperty] private string _value = "";
    public EditableText() { }
    public EditableText(string value) => _value = value;
}

public sealed partial class EditablePair : ObservableObject
{
    [ObservableProperty] private string _first = "";
    [ObservableProperty] private string _second = "";
}

public sealed partial class EditableLevelMove : ObservableObject
{
    [ObservableProperty] private int _level = 1;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _type = "Normal";
}

/// <summary>An editable list with add / remove commands.</summary>
public class EditableList<T> where T : new()
{
    public string Title { get; }
    public ObservableCollection<T> Items { get; }
    public IRelayCommand AddCommand { get; }
    public IRelayCommand<T> RemoveCommand { get; }

    public EditableList(string title, IEnumerable<T> items)
    {
        Title = title;
        Items = new ObservableCollection<T>(items);
        AddCommand = new RelayCommand(() => Items.Add(new T()));
        RemoveCommand = new RelayCommand<T>(item => { if (item != null) Items.Remove(item); });
    }
}

public sealed class TextList(string title, IEnumerable<EditableText> items) : EditableList<EditableText>(title, items);
public sealed class PairList(string title, IEnumerable<EditablePair> items) : EditableList<EditablePair>(title, items);
public sealed class LevelMoveList(string title, IEnumerable<EditableLevelMove> items) : EditableList<EditableLevelMove>(title, items);

public sealed partial class SpeciesEditorViewModel : ObservableObject
{
    private readonly PokemonSpecies _original;
    private readonly GameData _data;

    public IReadOnlyList<string> TypeOptions => SheetRules.AllTypes;

    [ObservableProperty] private string _name;
    [ObservableProperty] private int _hp;
    [ObservableProperty] private int _atk;
    [ObservableProperty] private int _def;
    [ObservableProperty] private int _spAtk;
    [ObservableProperty] private int _spDef;
    [ObservableProperty] private int _spd;
    [ObservableProperty] private string _height;
    [ObservableProperty] private string _weight;
    [ObservableProperty] private string _genderM;
    [ObservableProperty] private string _genderF;
    [ObservableProperty] private string _eggGroup;
    [ObservableProperty] private string _hatchRate;
    [ObservableProperty] private string _diet;
    [ObservableProperty] private string _habitat;

    public TextList Types { get; }
    public TextList Evolutions { get; }
    public TextList BaseAbilities { get; }
    public TextList AdvancedAbilities { get; }
    public TextList HighAbilities { get; }
    public PairList CustomAbilities { get; }
    public PairList Capabilities { get; }
    public PairList Skills { get; }
    public LevelMoveList LevelMoves { get; }
    public TextList TmMoves { get; }
    public TextList TutorMoves { get; }
    public TextList EggMoves { get; }

    public IReadOnlyList<TextList> SimpleAbilityLists => new[] { BaseAbilities, AdvancedAbilities, HighAbilities };
    public IReadOnlyList<TextList> OtherMoveLists => new[] { TmMoves, TutorMoves, EggMoves };

    public SpeciesEditorViewModel(PokemonSpecies species, GameData data)
    {
        _original = species;
        _data = data;
        _name = species.Name;
        _hp = species.StatHp; _atk = species.StatAtk; _def = species.StatDef;
        _spAtk = species.StatSpAtk; _spDef = species.StatSpDef; _spd = species.StatSpd;
        _height = species.Height ?? ""; _weight = species.Weight ?? "";
        _genderM = species.GenderRatioM?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        _genderF = species.GenderRatioF?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        _eggGroup = species.EggGroup ?? ""; _hatchRate = species.AverageHatchRate ?? "";
        _diet = species.Diet ?? ""; _habitat = species.Habitat ?? "";

        static IEnumerable<EditableText> Texts(IEnumerable<string> s) => s.Select(v => new EditableText(v));
        Types = new("Types", Texts(species.PokemonTypes));
        Evolutions = new("Evolutions", Texts(species.Evolutions));
        BaseAbilities = new("Base", Texts(species.BaseAbilities.Select(a => a.Name)));
        AdvancedAbilities = new("Advanced", Texts(species.AdvancedAbilities.Select(a => a.Name)));
        HighAbilities = new("High", Texts(species.HighAbilities.Select(a => a.Name)));
        CustomAbilities = new("Custom (section → ability)", (species.CustomAbilities ?? new()).Select(kv => new EditablePair { First = kv.Key, Second = kv.Value.Name }));
        Capabilities = new("Capabilities", species.Capabilities.Select(c => new EditablePair { First = c.Name, Second = c.Value ?? "" }));
        Skills = new("Skills", species.Skills.Select(c => new EditablePair { First = c.Name, Second = c.Roll ?? "" }));
        LevelMoves = new("Level-up moves", species.Moves.OrderBy(m => m.Level).Select(m => new EditableLevelMove
        {
            Level = m.Level, Name = m.Name, Type = m.AllTypes.FirstOrDefault()?.Trim() ?? "Normal",
        }));
        TmMoves = new("TM Moves", Texts(species.TmMoves.Select(m => m.Name)));
        TutorMoves = new("Tutor Moves", Texts(species.TutorMoves.Select(m => m.Name)));
        EggMoves = new("Egg Moves", Texts(species.EggMoves.Select(m => m.Name)));
    }

    private static double? ParseDouble(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    private List<LearnsetMove> Learnset(TextList list, List<LearnsetMove> previous) =>
        list.Items.Select(t => t.Value.Trim()).Where(n => n.Length > 0).Select(n =>
        {
            var prev = previous.FirstOrDefault(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            var types = prev?.AllTypes.ToList() ?? _data.FindMove(n)?.Types.ToList() ?? new List<string>();
            return new LearnsetMove { Name = n, Level = -1, Types = types };
        }).ToList();

    /// <summary>Builds the updated species; fields not exposed in the editor are preserved.</summary>
    public PokemonSpecies Build()
    {
        var s = _original.DeepClone();
        s.Name = Name.Trim();
        s.StatHp = Hp; s.StatAtk = Atk; s.StatDef = Def; s.StatSpAtk = SpAtk; s.StatSpDef = SpDef; s.StatSpd = Spd;
        s.Height = Height; s.Weight = Weight; s.EggGroup = EggGroup; s.AverageHatchRate = HatchRate; s.Diet = Diet; s.Habitat = Habitat;
        s.GenderRatioM = ParseDouble(GenderM);
        s.GenderRatioF = ParseDouble(GenderF);
        s.PokemonTypes = Types.Items.Select(t => t.Value.Trim()).Where(t => t.Length > 0).ToList();
        s.Evolutions = Evolutions.Items.Select(t => t.Value.Trim()).Where(t => t.Length > 0).ToList();
        static List<AbilityRef> Abilities(TextList l) =>
            l.Items.Select(t => t.Value.Trim()).Where(t => t.Length > 0).Select(n => new AbilityRef(n)).ToList();
        s.BaseAbilities = Abilities(BaseAbilities);
        s.AdvancedAbilities = Abilities(AdvancedAbilities);
        s.HighAbilities = Abilities(HighAbilities);
        var custom = CustomAbilities.Items.Where(p => p.First.Trim().Length > 0 && p.Second.Trim().Length > 0)
            .GroupBy(p => p.First.Trim()).ToDictionary(g => g.Key, g => new AbilityRef(g.Last().Second.Trim()));
        s.CustomAbilities = custom.Count > 0 || _original.CustomAbilities != null ? custom : null;
        s.Capabilities = Capabilities.Items.Where(p => p.First.Trim().Length > 0).Select(p => new NameValue { Name = p.First.Trim(), Value = p.Second }).ToList();
        s.Skills = Skills.Items.Where(p => p.First.Trim().Length > 0).Select(p => new SkillEntry { Name = p.First.Trim(), Roll = p.Second }).ToList();
        s.Moves = LevelMoves.Items.Where(m => m.Name.Trim().Length > 0).OrderBy(m => m.Level)
            .Select(m => new LearnsetMove { Name = m.Name.Trim(), Level = m.Level, Types = new List<string> { m.Type } }).ToList();
        s.TmMoves = Learnset(TmMoves, _original.TmMoves);
        s.TutorMoves = Learnset(TutorMoves, _original.TutorMoves);
        s.EggMoves = Learnset(EggMoves, _original.EggMoves);
        return s;
    }
}
