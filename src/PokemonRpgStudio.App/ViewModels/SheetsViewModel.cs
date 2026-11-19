using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Export;
using PokemonRpgStudio.Core.Json;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.ViewModels;

public sealed record SortOption(SheetSortKey Key, string Label)
{
    public override string ToString() => Label;
}

public sealed record RollLogEntry(string Time, string Title, string Detail);

/// <summary>
/// One stat of the sheet during play: max (from the sheet) and current
/// (max + modifier + combat stages, 20 % of the max per stage).
/// </summary>
public sealed partial class PlayStatRow : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public string Formula { get; }
    public int Max { get; }
    public bool IsBuffed { get; }
    public bool IsLowered { get; }

    [ObservableProperty] private int _modifier;
    [ObservableProperty] private int _combatStages;

    public int StageBonus => SheetRules.CombatStageBonus(Max, CombatStages);
    public int Current => Math.Max(0, Max + Modifier + StageBonus);
    public string ModifierText => Modifier == 0 ? "" : Formatting.SignedNumber(Modifier);
    public bool IsRaised => Modifier > 0;
    public bool IsDropped => Modifier < 0;
    /// <summary>"+2 CS (+6)"</summary>
    public string StagesText => CombatStages == 0 ? "" : $"{Formatting.SignedNumber(CombatStages)} CS ({Formatting.SignedNumber(StageBonus)})";
    public bool HasStages => CombatStages != 0;
    public bool IsStageUp => CombatStages > 0;
    public bool IsStageDown => CombatStages < 0;
    public string StageTip => $"1 combat stage = 20 % of {Label} ({SheetRules.CombatStageBonus(Max, 1):+0;-0;0}) · from −{SheetRules.MaxCombatStages} to +{SheetRules.MaxCombatStages}";
    public IRelayCommand IncreaseCommand { get; }
    public IRelayCommand DecreaseCommand { get; }
    public IRelayCommand RaiseStageCommand { get; }
    public IRelayCommand LowerStageCommand { get; }

    public PlayStatRow(string key, string label, string formula, int max, int modifier, int stages, bool isBuffed, bool isLowered, Action<PlayStatRow> changed)
    {
        _combatStages = Math.Clamp(stages, -SheetRules.MaxCombatStages, SheetRules.MaxCombatStages);
        RaiseStageCommand = new RelayCommand(() => { CombatStages++; changed(this); }, () => CombatStages < SheetRules.MaxCombatStages);
        LowerStageCommand = new RelayCommand(() => { CombatStages--; changed(this); }, () => CombatStages > -SheetRules.MaxCombatStages);
        Key = key;
        Label = label;
        Formula = formula;
        Max = max;
        _modifier = modifier;
        IsBuffed = isBuffed;
        IsLowered = isLowered;
        IncreaseCommand = new RelayCommand(() => { Modifier++; changed(this); });
        DecreaseCommand = new RelayCommand(() => { if (Current > 0) { Modifier--; changed(this); } });
    }

    partial void OnModifierChanged(int value)
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(ModifierText));
        OnPropertyChanged(nameof(IsRaised));
        OnPropertyChanged(nameof(IsDropped));
    }

    partial void OnCombatStagesChanged(int value)
    {
        OnPropertyChanged(nameof(StageBonus));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(StagesText));
        OnPropertyChanged(nameof(HasStages));
        OnPropertyChanged(nameof(IsStageUp));
        OnPropertyChanged(nameof(IsStageDown));
        RaiseStageCommand.NotifyCanExecuteChanged();
        LowerStageCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>A row of the move table with its roll buttons; a move can be disabled for a while.</summary>
public sealed partial class PlayMoveRow : ObservableObject
{
    public int Index { get; }
    public MoveData Move { get; }
    public IRelayCommand Accuracy { get; }
    public IRelayCommand Damage { get; }
    public IRelayCommand ToggleDisabledCommand { get; }

    [ObservableProperty] private bool _isDisabled;
    /// <summary>The last accuracy roll was a critical hit: the next damage roll doubles the dice.</summary>
    [ObservableProperty] private bool _critReady;

    public PlayMoveRow(int index, MoveData move, bool disabled, Action<PlayMoveRow> accuracy, Action<PlayMoveRow> damage, Action<PlayMoveRow> toggled)
    {
        Index = index;
        Move = move;
        _isDisabled = disabled;
        Accuracy = new RelayCommand(() => accuracy(this), () => !IsDisabled);
        Damage = new RelayCommand(() => damage(this), () => !IsDisabled);
        ToggleDisabledCommand = new RelayCommand(() => { IsDisabled = !IsDisabled; toggled(this); });
    }

    partial void OnIsDisabledChanged(bool value)
    {
        Accuracy.NotifyCanExecuteChanged();
        Damage.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DisableLabel));
    }

    partial void OnCritReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(DamageLabel));
        OnPropertyChanged(nameof(DamageTip));
    }

    public string DamageLabel => CritReady ? "🎲 Damage ×2" : "🎲 Damage";
    public string DamageTip => CritReady
        ? "Critical hit: roll damage with twice as many dice (right-click to cancel the critical)"
        : "Roll damage (move roll + ATK or SP.ATK)";

    [RelayCommand]
    private void CancelCrit() => CritReady = false;

    [RelayCommand]
    private void ForceCrit() => CritReady = HasDamage;

    public string DisableLabel => IsDisabled ? "Enable" : "Disable";
    public string Number => (Index + 1).ToString();
    public string Name => Move.Name;
    public IReadOnlyList<string> Types => Move.Types;
    public string Frequency => Move.Frequency ?? "";
    public string AC => string.Equals(Move.AC, "none", StringComparison.OrdinalIgnoreCase) ? "/" : Move.AC ?? "";
    public string Roll => Move.Roll ?? "";
    public bool HasDamage => !string.IsNullOrWhiteSpace(Move.Roll) && Move.Roll != "--";
    public string Class => Move.MoveClass ?? "";
    public string Range => Move.Range ?? "";
    public string Effect => Move.Effect is "--" ? "" : Move.Effect ?? "";
    public bool StartsSecondTable => Index == SheetRules.FirstTableMoves;

    /// <summary>"At-Will · AC 2 · 4, 1 Target"</summary>
    public string Details => string.Join(" · ", new[]
    {
        Frequency,
        string.IsNullOrWhiteSpace(AC) || AC == "/" ? "" : "AC " + AC,
        Range,
        HasDamage && SheetRules.DamageStat(Move) is { } stat ? $"damage + {DamageRollResult.StatLabel(stat)}" : "",
    }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

/// <summary>A free note written during the session under one section of the sheet.</summary>
public sealed partial class SectionNote : ObservableObject
{
    private readonly Action<SectionNote> _changed;
    private bool _loading;
    public string Key { get; }
    public string Placeholder { get; }
    [ObservableProperty] private string _text = "";

    public SectionNote(string key, string placeholder, Action<SectionNote> changed)
    {
        Key = key;
        Placeholder = placeholder;
        _changed = changed;
    }

    public void Load(string text)
    {
        _loading = true;
        Text = text;
        _loading = false;
    }

    partial void OnTextChanged(string value)
    {
        if (!_loading) _changed(this);
    }
}

/// <summary>A stat chip of the custom roll (toggle on to add the stat to the damage).</summary>
public sealed partial class CustomStatOption : ObservableObject
{
    private readonly Action _changed;
    public string Key { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isSelected;

    public CustomStatOption(string key, string label, Action changed)
    {
        Key = key;
        Label = label;
        _changed = changed;
    }

    partial void OnIsSelectedChanged(bool value) => _changed();
}

/// <summary>A skill with its roll and a temporary bonus / malus added to the roll.</summary>
public sealed partial class PlaySkillRow : ObservableObject
{
    private readonly Action<PlaySkillRow> _changed;
    public string Name { get; }
    public string Roll { get; }
    public IRelayCommand RollCommand { get; }
    public IRelayCommand IncreaseCommand { get; }
    public IRelayCommand DecreaseCommand { get; }

    [ObservableProperty] private int _modifier;

    public PlaySkillRow(string name, string roll, int modifier, Action<PlaySkillRow> rollIt, Action<PlaySkillRow> changed)
    {
        Name = name;
        Roll = roll;
        _modifier = modifier;
        _changed = changed;
        RollCommand = new RelayCommand(() => rollIt(this));
        IncreaseCommand = new RelayCommand(() => { Modifier++; _changed(this); });
        DecreaseCommand = new RelayCommand(() => { Modifier--; _changed(this); });
    }

    partial void OnModifierChanged(int value)
    {
        OnPropertyChanged(nameof(ModifierText));
        OnPropertyChanged(nameof(IsBonus));
        OnPropertyChanged(nameof(IsMalus));
        OnPropertyChanged(nameof(EffectiveRoll));
    }

    public bool CanRoll => !string.IsNullOrWhiteSpace(Roll);
    public string ModifierText => Modifier == 0 ? "" : Formatting.SignedNumber(Modifier);
    public bool IsBonus => Modifier > 0;
    public bool IsMalus => Modifier < 0;
    /// <summary>The roll actually used: "2d6+2" with a +1 bonus → "2d6+2+1".</summary>
    public string EffectiveRoll => Dice.WithModifier(Roll, Modifier);
}

/// <summary>
/// "Sheets" page: the library of saved sheets and the sheet as used during a game session
/// (read-only character sheet, HP / injuries / current stats tracking, dice, exports).
/// Creating and changing a sheet happens in <see cref="SheetEditorViewModel"/>.
/// </summary>
/// <summary>One sheet open in a tab of the Sheets page. Each tab keeps its own roll log.</summary>
public sealed partial class SheetTab : ObservableObject
{
    public string Id { get; }
    [ObservableProperty] private string _nickname;
    [ObservableProperty] private string _pokemonName;
    [ObservableProperty] private bool _isActive;
    public List<RollLogEntry> Log { get; } = new();
    public IRelayCommand ActivateCommand { get; }
    public IRelayCommand CloseCommand { get; }

    public SheetTab(SheetRecord record, Action<SheetTab> activate, Action<SheetTab> close)
    {
        Id = record.Id;
        _nickname = record.Nickname;
        _pokemonName = record.State.ChosenPokemon is { Length: > 0 } p ? p : record.PokemonName;
        ActivateCommand = new RelayCommand(() => activate(this));
        CloseCommand = new RelayCommand(() => close(this));
    }

    public void Update(SheetRecord record)
    {
        Nickname = record.Nickname;
        PokemonName = record.State.ChosenPokemon is { Length: > 0 } p ? p : record.PokemonName;
    }
}

public sealed partial class SheetsViewModel : PageViewModel
{
    private const int MaxLogEntries = 60;
    private readonly AppServices _services;
    private readonly Action<SheetRecord> _openEditor;
    private readonly Action _newSheet;
    private SheetRecord? _record;
    private SheetSnapshot? _snapshot;
    private bool _loadingSelection;

    public override string Title => "Sheets";
    public override string Glyph => "📝";

    // ── Library ──────────────────────────────────────────────────────────────
    public ObservableCollection<SheetListItem> Sheets { get; } = new();
    [ObservableProperty] private IReadOnlyList<SheetListItem> _filteredSheets = Array.Empty<SheetListItem>();
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private SheetListItem? _selectedListItem;
    /// <summary>Only sheets that are not finished or not correct.</summary>
    [ObservableProperty] private bool _onlyIssues;
    [ObservableProperty] private int _issueCount;
    public string IssuesFilterText => $"⚠ Needs attention ({IssueCount})";
    partial void OnIssueCountChanged(int value) => OnPropertyChanged(nameof(IssuesFilterText));
    partial void OnOnlyIssuesChanged(bool value) => ApplyFilter();

    // ── Folders ──────────────────────────────────────────────────────────────
    [ObservableProperty] private IReadOnlyList<FolderOption> _folderOptions = Array.Empty<FolderOption>();
    [ObservableProperty] private FolderOption? _selectedFolder;
    /// <summary>Destinations of "Move to folder" for the displayed sheet.</summary>
    [ObservableProperty] private IReadOnlyList<FolderOption> _moveFolderOptions = Array.Empty<FolderOption>();
    [ObservableProperty] private FolderOption? _sheetFolder;
    private bool _loadingFolders;
    /// <summary>The folder open in the list (null = all sheets / no folder): new and imported sheets go there.</summary>
    public string? CurrentFolder => SelectedFolder is { IsFolder: true } f ? f.Path : null;
    public bool HasFolderSelected => SelectedFolder?.IsFolder == true;

    // ── Displayed sheet ──────────────────────────────────────────────────────
    [ObservableProperty] private bool _hasSheet;
    [ObservableProperty] private string _nickname = "";
    [ObservableProperty] private string _speciesName = "";
    [ObservableProperty] private string _imageName = "";
    [ObservableProperty] private IReadOnlyList<string> _types = Array.Empty<string>();
    /// <summary>Weaknesses / resistances / immunities of the Pokémon's types (×1 left out).</summary>
    [ObservableProperty] private TypeMatchups _matchups = TypeMatchups.Empty;
    [ObservableProperty] private string _levelText = "";
    [ObservableProperty] private string _experienceText = "";
    [ObservableProperty] private string _rarityText = "";
    [ObservableProperty] private string _cardText = "";
    [ObservableProperty] private string _genderText = "";
    [ObservableProperty] private string _natureText = "";
    [ObservableProperty] private string _sizeText = "";
    [ObservableProperty] private string _warning = "";
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private string _incompleteText = "";
    /// <summary>What is wrong on the sheet (values the editor wouldn't allow), one per line.</summary>
    [ObservableProperty] private string _errorsText = "";

    [ObservableProperty] private int _maxHp;
    [ObservableProperty] private int _tempMaxHp;
    private int _sheetMaxHp;
    private HpStatus _hp = new(0, 0, 0, 0, 0);
    [ObservableProperty] private int _currentHp;
    [ObservableProperty] private int _injuries;
    [ObservableProperty] private int _hpAmount = 10;
    [ObservableProperty] private IReadOnlyList<PlayStatRow> _stats = Array.Empty<PlayStatRow>();
    [ObservableProperty] private string _evades = "";
    [ObservableProperty] private int _tutorPoints;

    [ObservableProperty] private IReadOnlyList<AbilityInfo> _abilities = Array.Empty<AbilityInfo>();
    [ObservableProperty] private IReadOnlyList<KeyValueRow> _capabilities = Array.Empty<KeyValueRow>();
    [ObservableProperty] private IReadOnlyList<PlaySkillRow> _skills = Array.Empty<PlaySkillRow>();
    [ObservableProperty] private IReadOnlyList<PlayMoveRow> _moves = Array.Empty<PlayMoveRow>();
    [ObservableProperty] private string _eggMovesText = "";
    [ObservableProperty] private string _tmMovesText = "";
    [ObservableProperty] private string _tutorMovesText = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _tutorNotes = "";
    [ObservableProperty] private string _heldObject = "";
    public ObservableCollection<RollLogEntry> RollLog { get; } = new();

    // ── Tabs: sheets open at the same time ───────────────────────────────────
    public ObservableCollection<SheetTab> Tabs { get; } = new();
    private SheetTab? _activeTab;
    public bool HasSeveralTabs => Tabs.Count > 1;

    // ── Teams ────────────────────────────────────────────────────────────────
    [ObservableProperty] private IReadOnlyList<TeamRecord> _teams = Array.Empty<TeamRecord>();
    [ObservableProperty] private TeamRecord? _selectedTeam;
    [ObservableProperty] private IReadOnlyList<TeamSlotOption> _teamSlots = Array.Empty<TeamSlotOption>();
    [ObservableProperty] private TeamSlotOption? _selectedTeamSlot;

    public double HpRatio => MaxHp <= 0 ? 0 : Math.Clamp((double)CurrentHp / MaxHp, 0, 1);
    public string HpText => $"{CurrentHp} / {MaxHp}";
    public bool HasTempMaxHp => TempMaxHp != 0;
    /// <summary>"Max HP 68 +10 temp − 15 (2 injuries) = 63"</summary>
    public string MaxHpInfo
    {
        get
        {
            var text = $"Max HP {_sheetMaxHp}";
            if (TempMaxHp != 0) text += $" {Formatting.SignedNumber(TempMaxHp)} temp";
            if (_hp.InjuryReduction > 0) text += $" − {_hp.InjuryReduction} ({TotalInjuries} {(TotalInjuries > 1 ? "injuries" : "injury")})";
            return TempMaxHp != 0 || _hp.InjuryReduction > 0 ? text + $" = {MaxHp}" : text;
        }
    }
    public bool IsMaxHpReduced => TempMaxHp != 0 || _hp.InjuryReduction > 0;
    public int TotalInjuries => Injuries;
    public bool HasInjuries => TotalInjuries > 0;
    public string InjuryInfo => TotalInjuries == 0 ? "No injury"
        : $"{TotalInjuries} {(TotalInjuries > 1 ? "injuries" : "injury")}";
    public string NextInjuryText => HasSheet && _hp.BaseMaxHp > 0 ? $"Next injury at {_hp.NextInjuryAt} HP or less" : "";
    public bool HasStatModifiers => Stats.Any(s => s.Modifier != 0 || s.CombatStages != 0);
    public bool HasSkillModifiers => Skills.Any(s => s.Modifier != 0);
    /// <summary>Any temporary change on the stats: stat bonuses, combat stages, skill bonuses or max HP change.</summary>
    public bool HasTempChanges => HasStatModifiers || HasSkillModifiers || HasTempMaxHp;
    public bool HasDisabledMoves => Moves.Any(m => m.IsDisabled);

    // Session notes, one under each section of the sheet.
    public SectionNote NoteHp { get; }
    public SectionNote NoteStats { get; }
    public SectionNote NoteMoves { get; }
    public SectionNote NoteAbilities { get; }
    public SectionNote NoteSkills { get; }
    public SectionNote NoteCapabilities { get; }
    private IEnumerable<SectionNote> AllNotes => new[] { NoteHp, NoteStats, NoteMoves, NoteAbilities, NoteSkills, NoteCapabilities };
    private readonly Dictionary<string, string> _pendingNotes = new();
    private readonly Avalonia.Threading.DispatcherTimer _notesTimer;
    public bool IsHpLow => HpRatio <= 0.25;
    public bool IsHpMid => HpRatio > 0.25 && HpRatio <= 0.5;
    public bool IsFainted => HasSheet && CurrentHp <= 0;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasTutorNotes => !string.IsNullOrWhiteSpace(TutorNotes);
    public bool HasHeldObject => !string.IsNullOrWhiteSpace(HeldObject);
    public bool HasNoTeams => Teams.Count == 0;
    public bool HasRolls => RollLog.Count > 0;
    public bool HasNoMoves => HasSheet && Moves.Count == 0;
    public bool HasNoAbilities => Abilities.Count == 0;

    partial void OnMovesChanged(IReadOnlyList<PlayMoveRow> value)
    {
        OnPropertyChanged(nameof(HasNoMoves));
        OnPropertyChanged(nameof(HasDisabledMoves));
    }
    partial void OnAbilitiesChanged(IReadOnlyList<AbilityInfo> value) => OnPropertyChanged(nameof(HasNoAbilities));

    public SheetsViewModel(AppServices services, Action<SheetRecord> openEditor, Action newSheet)
    {
        _services = services;
        _openEditor = openEditor;
        _newSheet = newSheet;
        _loadingSort = true;
        _selectedSort = SortOptions.FirstOrDefault(o => o.Key == services.Settings.SheetSort) ?? SortOptions[0];
        _sortDescending = services.Settings.SheetSortDescending;
        _loadingSort = false;
        RollLog.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRolls));
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSeveralTabs));
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(HasStatModifiers) or nameof(HasSkillModifiers) or nameof(HasTempMaxHp))
                OnPropertyChanged(nameof(HasTempChanges));
        };
        NoteHp = new SectionNote("hp", "Notes on hit points (temporary HP, status, injuries…)", OnNoteChanged);
        NoteStats = new SectionNote("stats", "Notes on stats (combat stages, buffs, debuffs…)", OnNoteChanged);
        NoteMoves = new SectionNote("moves", "Notes on moves (disabled until…, PP, frequency used…)", OnNoteChanged);
        NoteAbilities = new SectionNote("abilities", "Notes on abilities (used this scene…)", OnNoteChanged);
        NoteSkills = new SectionNote("skills", "Notes on skills (bonus from items, terrain…)", OnNoteChanged);
        NoteCapabilities = new SectionNote("capabilities", "Notes on capabilities", OnNoteChanged);
        _notesTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(0.8) };
        _notesTimer.Tick += (_, _) => FlushNotes();
        _filterTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _filterTimer.Tick += (_, _) => ApplyFilter();
        CustomStats = new (string Key, string Label)[] { ("HP", "HP"), ("ATK", "ATK"), ("DEF", "DEF"), ("SPATK", "SP.ATK"), ("SPDEF", "SP.DEF"), ("SPD", "SPEED") }
            .Select(x => new CustomStatOption(x.Key, x.Label, UpdateCustomStatsSummary)).ToList();
        services.LibraryChanged += () => { ReloadLibrary(); ReloadTeams(); _diskWatch.MarkSeen(); };
        services.GameDataChanged += () => { if (_record != null) Show(_record); };
        _diskWatch = new DiskChangeWatch(() => services.Library.Sheets.Directory, () => services.Library.Teams.Directory);
        ReloadLibrary();
        ReloadTeams();
        _diskWatch.MarkSeen();
    }

    /// <summary>Sheets and teams as last read: they are only re-read when they changed.</summary>
    private readonly DiskChangeWatch _diskWatch;

    public override void OnDeactivated()
    {
        FlushNotes();
        _diskWatch.MarkSeen(); // this page's own saves (HP, notes…) are already shown
    }

    public override void OnActivated()
    {
        if (!_diskWatch.HasChanged) return;
        ReloadLibrary();
        ReloadTeams();
        // The editor may have changed the sheet: show the latest version.
        if (_record != null && _services.Library.Sheets.Get(_record.Id) is { } fresh) Show(fresh);
        _diskWatch.MarkSeen();
    }

    partial void OnTeamsChanged(IReadOnlyList<TeamRecord> value) => OnPropertyChanged(nameof(HasNoTeams));
    partial void OnNotesChanged(string value) => OnPropertyChanged(nameof(HasNotes));
    partial void OnTutorNotesChanged(string value) => OnPropertyChanged(nameof(HasTutorNotes));
    partial void OnHeldObjectChanged(string value) => OnPropertyChanged(nameof(HasHeldObject));

    partial void OnCurrentHpChanged(int value) => NotifyHp();
    partial void OnMaxHpChanged(int value) => NotifyHp();
    partial void OnTempMaxHpChanged(int value) => NotifyHp();
    partial void OnInjuriesChanged(int value) => NotifyHp();
    partial void OnStatsChanged(IReadOnlyList<PlayStatRow> value) => OnPropertyChanged(nameof(HasStatModifiers));
    partial void OnSkillsChanged(IReadOnlyList<PlaySkillRow> value) => OnPropertyChanged(nameof(HasSkillModifiers));

    private void NotifyHp()
    {
        OnPropertyChanged(nameof(HpRatio));
        OnPropertyChanged(nameof(HpText));
        OnPropertyChanged(nameof(HasTempMaxHp));
        OnPropertyChanged(nameof(MaxHpInfo));
        OnPropertyChanged(nameof(IsMaxHpReduced));
        OnPropertyChanged(nameof(TotalInjuries));
        OnPropertyChanged(nameof(HasInjuries));
        OnPropertyChanged(nameof(InjuryInfo));
        OnPropertyChanged(nameof(NextInjuryText));
        OnPropertyChanged(nameof(IsHpLow));
        OnPropertyChanged(nameof(IsHpMid));
        OnPropertyChanged(nameof(IsFainted));
        OnPropertyChanged(nameof(TickAmount));
        OnPropertyChanged(nameof(TickLabel));
        OnPropertyChanged(nameof(HealTickLabel));
        OnPropertyChanged(nameof(HalfHealAmount));
        OnPropertyChanged(nameof(HealHalfLabel));
    }

    // ═══ Library ════════════════════════════════════════════════════════════

    private void ReloadLibrary()
    {
        var selectedId = _record?.Id;
        Sheets.Clear();
        var records = _services.Library.Sheets.List();
        ReloadFolders(records);
        foreach (var r in records.OrderBy(r => r.Nickname, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(r.State.ChosenPokemon)) r.State.ChosenPokemon = r.PokemonName;
            Sheets.Add(new SheetListItem(r, SheetSearch.BuildIndex(r, _services.Data.FindPokemon(r.State.ChosenPokemon)),
                SheetValidation.Check(r.State, _services.Data)));
        }
        IssueCount = Sheets.Count(s => s.HasIssues);
        ApplyFilter();
        _loadingSelection = true;
        SelectedListItem = selectedId is null ? null : FilteredSheets.FirstOrDefault(s => s.Id == selectedId);
        _loadingSelection = false;
        foreach (var tab in Tabs.ToList())
        {
            if (_services.Library.Sheets.Get(tab.Id) is { } r) tab.Update(r);
            else if (tab == _activeTab) CloseTab(tab);
            else Tabs.Remove(tab);
        }
    }

    private void ReloadFolders(IReadOnlyCollection<SheetRecord> records)
    {
        var wanted = SelectedFolder is null && FolderOptions.Count == 0 ? _services.Settings.SheetFolderFilter : SelectedFolder?.Path;
        _loadingFolders = true;
        FolderOptions = FolderOption.ForFilter(records, _services.Settings);
        SelectedFolder = FolderOptions.FirstOrDefault(o => string.Equals(o.Path, wanted, StringComparison.OrdinalIgnoreCase)) ?? FolderOptions[0];
        MoveFolderOptions = FolderOption.ForMove(records, _services.Settings);
        SheetFolder = _record is null ? null : FindMoveOption(_record.Folder);
        _loadingFolders = false;
        OnPropertyChanged(nameof(CurrentFolder));
        OnPropertyChanged(nameof(HasFolderSelected));
    }

    private FolderOption? FindMoveOption(string folder)
    {
        var path = SheetFolders.Normalize(folder);
        return MoveFolderOptions.FirstOrDefault(o => string.Equals(o.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSelectedFolderChanged(FolderOption? value)
    {
        OnPropertyChanged(nameof(CurrentFolder));
        OnPropertyChanged(nameof(HasFolderSelected));
        if (_loadingFolders || value is null) return;
        _services.Settings.SheetFolderFilter = value.Path;
        _services.SaveSettings();
        ApplyFilter();
    }

    /// <summary>Moves the displayed sheet to the folder picked in the toolbar.</summary>
    partial void OnSheetFolderChanged(FolderOption? value)
    {
        if (_loadingFolders || value?.Path is null || _record is null) return;
        var record = _services.Library.Sheets.Get(_record.Id) ?? _record;
        if (string.Equals(SheetFolders.Normalize(record.Folder), value.Path, StringComparison.OrdinalIgnoreCase)) return;
        record.Folder = value.Path;
        _services.Library.Sheets.Save(record, touch: false);
        _record = record;
        _services.NotifyLibraryChanged();
        _services.Dialogs.Notify(value.Path.Length == 0 ? $"\"{record.Nickname}\" removed from its folder." : $"\"{record.Nickname}\" moved to {value.Path}.");
    }

    /// <summary>New folder, inside the folder currently open.</summary>
    [RelayCommand]
    private async Task NewFolder()
    {
        var parent = CurrentFolder ?? "";
        var name = await _services.Dialogs.PromptAsync("New folder",
            parent.Length == 0 ? "Name of the new folder (use / for subfolders, e.g. Campaign/Fire team):" : $"Name of the new folder inside \"{parent}\":",
            confirmText: "Create", watermark: "Folder name");
        if (name is null) return;
        var path = SheetFolders.Create(_services.Settings, parent, name);
        if (path.Length == 0) return;
        _services.SaveSettings();
        _services.Settings.SheetFolderFilter = path;
        _loadingFolders = true;
        SelectedFolder = null;
        FolderOptions = Array.Empty<FolderOption>();
        _loadingFolders = false;
        ReloadLibrary();
    }

    [RelayCommand]
    private async Task RenameFolder()
    {
        if (CurrentFolder is not { } folder) return;
        var name = await _services.Dialogs.PromptAsync("Rename folder", $"New name for \"{folder}\":", SheetFolders.Name(folder), "Rename");
        if (name is null) return;
        try
        {
            var target = SheetFolders.RenameFolder(_services.Library.Sheets, _services.Settings, folder, name);
            _services.Settings.SheetFolderFilter = target;
            _services.SaveSettings();
            _loadingFolders = true;
            SelectedFolder = null;
            FolderOptions = Array.Empty<FolderOption>();
            _loadingFolders = false;
            _services.NotifyLibraryChanged();
        }
        catch (InvalidOperationException ex)
        {
            _services.Dialogs.Notify(ex.Message, isError: true);
        }
    }

    /// <summary>Deletes the open folder; its sheets and subfolders go to the parent folder.</summary>
    [RelayCommand]
    private async Task DeleteFolder()
    {
        if (CurrentFolder is not { } folder) return;
        var parent = SheetFolders.Parent(folder);
        var count = Sheets.Count(s => SheetFolders.IsInside(SheetFolders.Normalize(s.Record.Folder), folder));
        var where = parent.Length == 0 ? "out of any folder" : $"to \"{parent}\"";
        if (!await _services.Dialogs.ConfirmAsync("Delete folder",
                $"Delete the folder \"{folder}\"? No sheet is deleted: its {count} sheet(s) and subfolders are moved {where}.")) return;
        SheetFolders.Delete(_services.Library.Sheets, _services.Settings, folder);
        _services.Settings.SheetFolderFilter = parent.Length == 0 ? null : parent;
        _services.SaveSettings();
        _loadingFolders = true;
        SelectedFolder = null;
        FolderOptions = Array.Empty<FolderOption>();
        _loadingFolders = false;
        _services.NotifyLibraryChanged();
        _services.Dialogs.Notify("Folder deleted.");
    }

    private void ReloadTeams()
    {
        var selected = SelectedTeam?.Id;
        Teams = _services.Library.Teams.List();
        SelectedTeam = Teams.FirstOrDefault(t => t.Id == selected);
    }

    /// <summary>Typing in the search box filters once the typing pauses, not on every key (slow on phones).</summary>
    private readonly Avalonia.Threading.DispatcherTimer _filterTimer;

    partial void OnFilterTextChanged(string value)
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void ApplyFilter()
    {
        _filterTimer.Stop();
        // Every word must match a field: nickname, Pokémon, types, moves, abilities, notes…
        var terms = SheetSearch.Terms(FilterText);
        var folder = SelectedFolder?.Path;
        var matching = Sheets.Where(s => (!OnlyIssues || s.HasIssues) && SheetSearch.Matches(s.SearchIndex, terms)
                                         && (folder is null || SheetFolders.IsInside(SheetFolders.Normalize(s.Record.Folder), folder)));
        var byId = matching.ToDictionary(i => i.Id);
        var sorted = SheetSorting.Sort(byId.Values.Select(i => i.Record), SelectedSort.Key, SortDescending).Select(r => byId[r.Id]).ToList();

        // Keep the selection while re-ordering the list.
        var selected = SelectedListItem?.Id;
        _loadingSelection = true;
        FilteredSheets = sorted;
        SelectedListItem = selected is null ? null : sorted.FirstOrDefault(i => i.Id == selected);
        _loadingSelection = false;
    }

    // ── Sorting ─────────────────────────────────────────────────────────────
    public IReadOnlyList<SortOption> SortOptions { get; } =
        SheetSorting.Options.Select(o => new SortOption(o.Key, o.Label)).ToList();

    [ObservableProperty] private SortOption _selectedSort = null!;
    [ObservableProperty] private bool _sortDescending;
    private bool _loadingSort;

    public string SortDirectionGlyph => SortDescending ? "↓" : "↑";
    public string SortDirectionTip => SortDescending ? "Descending — click for ascending" : "Ascending — click for descending";

    partial void OnSelectedSortChanged(SortOption value)
    {
        if (_loadingSort || value is null) return;
        _loadingSort = true;
        SortDescending = SheetSorting.DefaultDescending(value.Key);
        _loadingSort = false;
        SortChanged();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        OnPropertyChanged(nameof(SortDirectionGlyph));
        OnPropertyChanged(nameof(SortDirectionTip));
        if (!_loadingSort) SortChanged();
    }

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

    private void SortChanged()
    {
        _services.Settings.SheetSort = SelectedSort.Key;
        _services.Settings.SheetSortDescending = SortDescending;
        _services.SaveSettings();
        ApplyFilter();
    }

    partial void OnSelectedListItemChanged(SheetListItem? value)
    {
        if (_loadingSelection || value is null) return;
        var record = _services.Library.Sheets.Get(value.Id);
        if (record is null)
        {
            _services.Dialogs.Notify("This sheet could not be read.", isError: true);
            return;
        }
        OpenTab(record);
    }

    /// <summary>Shows a sheet by id (after the editor closes).</summary>
    public void Select(string? id)
    {
        if (_diskWatch.HasChanged)
        {
            ReloadLibrary();
            _diskWatch.MarkSeen();
        }
        if (id is null) return;
        var item = FilteredSheets.FirstOrDefault(s => s.Id == id);
        if (item is null)
        {
            // Hidden by the search or the folder: show every sheet.
            FilterText = "";
            OnlyIssues = false;
            ApplyFilter(); // now, not after the typing delay
            if (FilteredSheets.All(s => s.Id != id) && FolderOptions.Count > 0) SelectedFolder = FolderOptions[0];
            item = FilteredSheets.FirstOrDefault(s => s.Id == id);
        }
        if (item is null) return;
        _loadingSelection = true;
        SelectedListItem = item;
        _loadingSelection = false;
        if (_services.Library.Sheets.Get(id) is { } record) OpenTab(record);
    }

    /// <summary>Shows a sheet in its tab, opening a new tab if it isn't open yet.</summary>
    private void OpenTab(SheetRecord record)
    {
        var tab = Tabs.FirstOrDefault(t => t.Id == record.Id);
        if (tab is null)
        {
            tab = new SheetTab(record, ActivateTab, CloseTab);
            var at = _activeTab is null ? Tabs.Count : Tabs.IndexOf(_activeTab) + 1;
            Tabs.Insert(at, tab);
        }
        SetActiveTab(tab);
        Show(record);
    }

    private void SetActiveTab(SheetTab tab)
    {
        if (_activeTab == tab) return;
        if (_activeTab != null)
        {
            _activeTab.IsActive = false;
            _activeTab.Log.Clear();
            _activeTab.Log.AddRange(RollLog);
        }
        _activeTab = tab;
        tab.IsActive = true;
        RollLog.Clear();
        foreach (var entry in tab.Log) RollLog.Add(entry);
    }

    private void ActivateTab(SheetTab tab)
    {
        if (tab == _activeTab) return;
        var record = _services.Library.Sheets.Get(tab.Id);
        if (record is null)
        {
            CloseTab(tab);
            return;
        }
        SetActiveTab(tab);
        Show(record);
        _loadingSelection = true;
        SelectedListItem = FilteredSheets.FirstOrDefault(s => s.Id == tab.Id);
        _loadingSelection = false;
    }

    private void CloseTab(SheetTab tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        if (tab != _activeTab)
        {
            Tabs.Remove(tab);
            return;
        }
        FlushNotes();
        Tabs.RemoveAt(index);
        _activeTab = null;
        RollLog.Clear();
        if (Tabs.Count == 0)
        {
            Clear();
            return;
        }
        ActivateTab(Tabs[Math.Min(index, Tabs.Count - 1)]);
    }

    [RelayCommand]
    private void CloseCurrentTab()
    {
        if (_activeTab != null) CloseTab(_activeTab);
    }

    [RelayCommand]
    private void CloseOtherTabs()
    {
        foreach (var tab in Tabs.Where(t => t != _activeTab).ToList()) Tabs.Remove(tab);
    }

    [RelayCommand]
    private void NextTab() => CycleTab(1);

    [RelayCommand]
    private void PreviousTab() => CycleTab(-1);

    private void CycleTab(int step)
    {
        if (Tabs.Count < 2 || _activeTab is null) return;
        var index = (Tabs.IndexOf(_activeTab) + step + Tabs.Count) % Tabs.Count;
        ActivateTab(Tabs[index]);
    }

    private void Clear()
    {
        _record = null;
        _snapshot = null;
        HasSheet = false;
        RollLog.Clear();
        _loadingSelection = true;
        SelectedListItem = null;
        _loadingSelection = false;
    }

    // ═══ Display ════════════════════════════════════════════════════════════

    private void Show(SheetRecord record)
    {
        FlushNotes();
        _record = record;
        if (_activeTab?.Id == record.Id) _activeTab.Update(record);
        var state = record.State;
        if (string.IsNullOrEmpty(state.ChosenPokemon)) state.ChosenPokemon = record.PokemonName;
        var species = _services.Data.FindPokemon(state.ChosenPokemon);

        Nickname = record.Nickname;
        Notes = record.Notes;
        TutorNotes = state.TutorNotes;
        HeldObject = state.HeldObject;
        HasSheet = true;
        _loadingFolders = true;
        SheetFolder = FindMoveOption(record.Folder);
        _loadingFolders = false;
        ImageName = species?.Name ?? state.ChosenPokemon;
        SpeciesName = Formatting.TitleCase(species?.Name ?? state.ChosenPokemon);
        if (species is null)
        {
            _snapshot = null;
            Warning = $"\"{state.ChosenPokemon}\" is not in the Pokédex: edit the sheet to pick a Pokémon.";
            Types = Array.Empty<string>();
            Matchups = TypeMatchups.Empty;
            Stats = Array.Empty<PlayStatRow>();
            Moves = Array.Empty<PlayMoveRow>();
            Abilities = Array.Empty<AbilityInfo>();
            Capabilities = Array.Empty<KeyValueRow>();
            Skills = Array.Empty<PlaySkillRow>();
            IsComplete = false;
            IncompleteText = "";
            ErrorsText = "";
            MaxHp = CurrentHp = 0;
            foreach (var note in AllNotes) note.Load((record.Play ?? new SheetPlayState()).Note(note.Key));
            return;
        }
        Warning = "";

        var snap = SheetSnapshot.Create(state, species, _services.Data, record.Notes, record.Nickname);
        _snapshot = snap;
        var play = record.Play ?? new SheetPlayState();

        Types = species.PokemonTypes;
        Matchups = TypeMatchups.For(_services.Data.TypeChart, species.PokemonTypes);
        LevelText = state.Level > 0 ? $"Lv. {SheetRules.FormatLevel(state.Level)}" : "Lv. —";
        ExperienceText = $"XP {state.Experience} / {SheetRules.MaxExperience}";
        RarityText = Chip(state.Rarity, SheetRules.TierSymbol(state.Rarity));
        CardText = Chip(state.Card, SheetRules.TierSymbol(state.Card));
        GenderText = Chip(state.Gender, SheetRules.GenderSymbol(state.Gender));
        NatureText = string.IsNullOrEmpty(snap.Nature) ? "No nature" : snap.Nature;
        SizeText = $"{species.Height} · {species.Weight}";

        var missing = SheetRules.MissingRequirements(state);
        IsComplete = missing.Count == 0;
        IncompleteText = IsComplete ? "" : "This sheet is not finished: " + string.Join(" · ", missing) + ". Click Edit to complete it.";
        var errors = SheetValidation.Check(state, _services.Data).Errors;
        ErrorsText = errors.Count == 0 ? "" : "⚠ This sheet is not correct:\n• " + string.Join("\n• ", errors) + "\nClick Edit to fix it.";

        _sheetMaxHp = snap.HitPoints;
        TempMaxHp = play.MaxHpModifier;
        ShowHp(play.HpStatus(_sheetMaxHp));
        foreach (var note in AllNotes) note.Load(play.Note(note.Key));

        Stats = SheetRules.AllStats.Select(stat =>
        {
            var key = stat.ToString();
            var label = stat switch { Stat.SPATK => "SP.ATK", Stat.SPDEF => "SP.DEF", Stat.SPD => "SPEED", _ => key };
            var formula = $"{species.BaseStat(stat)} + {snap.Buffs[key]} + {state.Points(stat)}".Replace("+ -", "- ");
            bool natureActive = state.BuffedStat != state.LoweredStat;
            return new PlayStatRow(key, label, formula, snap.FinalStats[key], play.Modifier(key), play.CombatStage(key),
                natureActive && state.BuffedStat == key, natureActive && state.LoweredStat == key, OnStatChanged);
        }).ToList();
        UpdateDerived();
        TutorPoints = snap.TutorPoints;

        Abilities = snap.Abilities.Select(a => new AbilityInfo(a.Name, a.Effect ?? "")).ToList();
        Capabilities = species.Capabilities.Where(c => c.Name != "").Select(c => new KeyValueRow(c.Name, c.Value ?? "")).ToList();
        Skills = species.Skills.Where(s => s.Name != "")
            .Select(s => new PlaySkillRow(s.Name, s.Roll ?? "", play.SkillModifier(s.Name), RollSkill, OnSkillModifierChanged))
            .ToList();
        Moves = snap.SheetMoves.Select((m, i) => new PlayMoveRow(i, m, play.IsMoveDisabled(m.Name), RollMoveAccuracy, RollMoveDamage, OnMoveToggled)).ToList();

        static string Names(List<string> moves) => moves.Count == 0 ? "None" : string.Join(", ", moves);
        EggMovesText = Names(state.EggMoves);
        TmMovesText = Names(state.TmMoves);
        TutorMovesText = Names(state.TutorMoves);
    }

    private static string Chip(string? value, string symbol) =>
        string.IsNullOrEmpty(value) ? "—" : (symbol.Length > 0 ? $"{symbol} {value}" : value);

    private void UpdateDerived()
    {
        UpdateCustomStatsSummary();
        int Current(string key) => Stats.FirstOrDefault(s => s.Key == key)?.Current ?? 0;
        Evades = $"Phys {SheetRules.Evade(Current("DEF"))} · Spec {SheetRules.Evade(Current("SPDEF"))} · Speed {SheetRules.Evade(Current("SPD"))}";
    }

    // ═══ Play tracking (persisted with the sheet) ═══════════════════════════

    private void SavePlay(Action<SheetPlayState> change)
    {
        if (_record is null) return;
        // Re-read the file so edits made in the editor are never overwritten.
        var record = _services.Library.Sheets.Get(_record.Id) ?? _record;
        record.Play ??= new SheetPlayState();
        change(record.Play);
        _services.Library.Sheets.Save(record, touch: false);
        _record = record;
    }

    /// <summary>Displays an HP state.</summary>
    private void ShowHp(HpStatus hp)
    {
        _hp = hp;
        MaxHp = hp.MaxHp;
        CurrentHp = hp.CurrentHp;
        Injuries = hp.Injuries;
        NotifyHp();
    }

    /// <summary>Displays and saves an HP state; logs the injuries gained or healed.</summary>
    private void ApplyHp(HpStatus hp, string title, string what)
    {
        int before = _hp.Injuries;
        ShowHp(hp);
        var (current, baseMax, injuries, mod) = (hp.CurrentHp, hp.BaseMaxHp, hp.Injuries, TempMaxHp);
        SavePlay(p =>
        {
            p.CurrentHp = current >= baseMax ? null : current;
            p.Injuries = injuries;
            p.InjuriesIncludeDamage = true;
            p.MaxHpModifier = mod;
        });
        var change = hp.Injuries - before;
        var injuryText = change == 0 ? "" : change > 0
            ? $" · +{change} {(change > 1 ? "injuries" : "injury")} (max HP {hp.MaxHp})"
            : $" · {-change} {(-change > 1 ? "injuries" : "injury")} removed (max HP {hp.MaxHp})";
        Log(title, $"{what} → {HpText}{injuryText}");
    }

    [RelayCommand]
    private void Damage()
    {
        var amount = Math.Max(0, HpAmount);
        var massive = InjuryRules.IsMassiveDamage(_hp.BaseMaxHp, amount);
        ApplyHp(InjuryRules.Damage(_hp, amount), massive ? "Damage — MASSIVE" : "Damage",
            $"−{amount} HP" + (massive ? " (more than 50 % of max HP: +1 injury)" : ""));
    }

    /// <summary>A tick of HP: a tenth of the max HP (sheet value + temporary change, before injuries), at least 1.</summary>
    public int TickAmount => Math.Max(1, _hp.BaseMaxHp / 10);
    public string TickLabel => $"− Tick ({TickAmount})";
    public string HealTickLabel => $"+ Tick ({TickAmount})";
    /// <summary>Half of the max HP (sheet value + temporary change, before injuries), at least 1.</summary>
    public int HalfHealAmount => Math.Max(1, _hp.BaseMaxHp / 2);
    public string HealHalfLabel => $"+ 50 % ({HalfHealAmount})";

    /// <summary>Takes a tick of damage (poison, burn, weather…): 10 % of the max HP.</summary>
    [RelayCommand]
    private void TakeTick()
    {
        if (_record is null || _hp.BaseMaxHp <= 0) return;
        var amount = TickAmount;
        ApplyHp(InjuryRules.Damage(_hp, amount), "Tick", $"−{amount} HP (1 tick)");
    }

    [RelayCommand]
    private void Heal()
    {
        var amount = Math.Max(0, HpAmount);
        ApplyHp(InjuryRules.Heal(_hp, amount), "Heal", $"+{amount} HP");
    }

    /// <summary>Heals a tick: 10 % of the max HP.</summary>
    [RelayCommand]
    private void HealTick()
    {
        if (_record is null || _hp.BaseMaxHp <= 0) return;
        var amount = TickAmount;
        ApplyHp(InjuryRules.Heal(_hp, amount), "Heal", $"+{amount} HP (1 tick)");
    }

    /// <summary>Heals half of the max HP.</summary>
    [RelayCommand]
    private void HealHalf()
    {
        if (_record is null || _hp.BaseMaxHp <= 0) return;
        var amount = HalfHealAmount;
        ApplyHp(InjuryRules.Heal(_hp, amount), "Heal", $"+{amount} HP (50 % of max HP)");
    }

    /// <summary>Injury from another source, added by hand.</summary>
    [RelayCommand]
    private void AddInjury() =>
        ApplyHp(InjuryRules.WithInjuries(_hp, _hp.Injuries + 1), "Injury", "Injury added");

    [RelayCommand]
    private void RemoveInjury()
    {
        if (_hp.Injuries <= 0) return;
        ApplyHp(InjuryRules.WithInjuries(_hp, _hp.Injuries - 1), "Injury", "Injury removed");
    }

    private void OnStatChanged(PlayStatRow row)
    {
        UpdateDerived();
        OnPropertyChanged(nameof(HasStatModifiers));
        var key = row.Key;
        var (modifier, stages) = (row.Modifier, row.CombatStages);
        SavePlay(p =>
        {
            if (modifier == 0) p.StatModifiers.Remove(key);
            else p.StatModifiers[key] = modifier;
            if (stages == 0) p.CombatStages.Remove(key);
            else p.CombatStages[key] = stages;
        });
    }

    /// <summary>Temporary max HP bonus (gains the same amount of HP).</summary>
    [RelayCommand]
    private void RaiseMaxHp() => ChangeTempMaxHp(Math.Max(0, HpAmount));

    /// <summary>Temporary max HP malus (current HP is capped to the new maximum).</summary>
    [RelayCommand]
    private void LowerMaxHp() => ChangeTempMaxHp(-Math.Max(0, HpAmount));

    [RelayCommand]
    private void ResetTempMaxHp() => ChangeTempMaxHp(-TempMaxHp);

    private void ChangeTempMaxHp(int delta)
    {
        if (delta == 0 || _record is null) return;
        var newBase = Math.Max(1, _sheetMaxHp + TempMaxHp + delta);
        delta = newBase - _hp.BaseMaxHp;
        TempMaxHp = newBase - _sheetMaxHp;
        // A max HP bonus also gives the HP; a malus only caps them.
        var hp = InjuryRules.Create(newBase, _hp.CurrentHp + Math.Max(0, delta), _hp.Injuries, _sheetMaxHp);
        ApplyHp(hp, "Max HP", $"{Formatting.SignedNumber(delta)} max HP (temporary)");
    }

    [RelayCommand]
    private void ResetStatModifiers()
    {
        foreach (var s in Stats) { s.Modifier = 0; s.CombatStages = 0; }
        UpdateDerived();
        OnPropertyChanged(nameof(HasStatModifiers));
        SavePlay(p => { p.StatModifiers.Clear(); p.CombatStages.Clear(); });
    }

    /// <summary>
    /// Removes every temporary stat change (stat bonuses, combat stages, skill bonuses, max HP change)
    /// without healing: HP, injuries, disabled moves and notes are kept.
    /// </summary>
    [RelayCommand]
    private void ResetTempStats()
    {
        if (!HasTempChanges) return;
        if (TempMaxHp != 0) ChangeTempMaxHp(-TempMaxHp);
        foreach (var s in Stats) { s.Modifier = 0; s.CombatStages = 0; }
        foreach (var s in Skills) s.Modifier = 0;
        UpdateDerived();
        OnPropertyChanged(nameof(HasStatModifiers));
        OnPropertyChanged(nameof(HasSkillModifiers));
        SavePlay(p => { p.StatModifiers.Clear(); p.CombatStages.Clear(); p.SkillModifiers.Clear(); });
        Log("Temp stats reset", "Stat bonuses, combat stages, skill bonuses and max HP change removed.");
    }

    private void OnMoveToggled(PlayMoveRow row)
    {
        OnPropertyChanged(nameof(HasDisabledMoves));
        var (name, disabled) = (row.Name, row.IsDisabled);
        SavePlay(p =>
        {
            p.DisabledMoves.RemoveAll(m => m.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (disabled) p.DisabledMoves.Add(name);
        });
        Log(name, disabled ? "Move disabled" : "Move enabled again");
    }

    [RelayCommand]
    private void EnableAllMoves()
    {
        foreach (var m in Moves) m.IsDisabled = false;
        OnPropertyChanged(nameof(HasDisabledMoves));
        SavePlay(p => p.DisabledMoves.Clear());
    }

    private void OnSkillModifierChanged(PlaySkillRow row)
    {
        OnPropertyChanged(nameof(HasSkillModifiers));
        var (name, mod) = (row.Name, row.Modifier);
        SavePlay(p =>
        {
            if (mod == 0) p.SkillModifiers.Remove(name);
            else p.SkillModifiers[name] = mod;
        });
    }

    [RelayCommand]
    private void ResetSkillModifiers()
    {
        foreach (var s in Skills) s.Modifier = 0;
        OnPropertyChanged(nameof(HasSkillModifiers));
        SavePlay(p => p.SkillModifiers.Clear());
    }

    private void RollSkill(PlaySkillRow row) =>
        RollFormula($"{row.Name} check" + (row.Modifier != 0 ? $" ({row.ModifierText})" : ""), row.EffectiveRoll);

    private void OnNoteChanged(SectionNote note)
    {
        _pendingNotes[note.Key] = note.Text;
        _notesTimer.Stop();
        _notesTimer.Start();
    }

    /// <summary>Writes the notes typed in the last moments (also on sheet change and on close).</summary>
    public void FlushNotes()
    {
        _notesTimer.Stop();
        if (_pendingNotes.Count == 0 || _record is null) return;
        var notes = _pendingNotes.ToList();
        _pendingNotes.Clear();
        SavePlay(p =>
        {
            foreach (var (key, text) in notes)
            {
                if (string.IsNullOrWhiteSpace(text)) p.Notes.Remove(key);
                else p.Notes[key] = text;
            }
        });
    }

    /// <summary>
    /// Rest / end of the fight: HP back to the max allowed by the injuries, and every temporary effect
    /// removed (stat and skill bonuses, max HP change, disabled moves). Injuries and notes are kept.
    /// </summary>
    [RelayCommand]
    private void Rest()
    {
        TempMaxHp = 0;
        ShowHp(InjuryRules.FullHeal(_sheetMaxHp, _hp.Injuries, _sheetMaxHp));
        foreach (var s in Stats) { s.Modifier = 0; s.CombatStages = 0; }
        foreach (var s in Skills) s.Modifier = 0;
        foreach (var m in Moves) m.IsDisabled = false;
        UpdateDerived();
        OnPropertyChanged(nameof(HasStatModifiers));
        OnPropertyChanged(nameof(HasSkillModifiers));
        OnPropertyChanged(nameof(HasDisabledMoves));
        var injuries = _hp.Injuries;
        SavePlay(p => { p.Reset(); p.Injuries = injuries; p.InjuriesIncludeDamage = true; });
        Log("Rest", "HP, stats, skills and moves restored (injuries kept).");
        NotifyHp();
    }

    // ═══ Dice ═══════════════════════════════════════════════════════════════

    private void Log(string title, string detail)
    {
        RollLog.Insert(0, new RollLogEntry(DateTime.Now.ToString("HH:mm:ss"), title, detail));
        while (RollLog.Count > MaxLogEntries) RollLog.RemoveAt(RollLog.Count - 1);
    }

    private void RollFormula(string title, string formula)
    {
        if (string.IsNullOrWhiteSpace(formula) || formula == "--")
        {
            Log(title, "No roll for this.");
            return;
        }
        try
        {
            var r = Dice.RollExpression(formula);
            var detail = string.Join(" + ", r.Rolls.Select(x => $"{x.Dice} ({string.Join(", ", x.Results)})"));
            Log(title, $"{r.Final}  —  {formula}: {detail}");
        }
        catch (FormatException)
        {
            Log(title, $"Can't roll \"{formula}\".");
        }
    }

    /// <summary>Current value of a stat (sheet value + in-fight change).</summary>
    private int CurrentStat(string? key) => key is null ? 0 : Stats.FirstOrDefault(s => s.Key == key)?.Current ?? 0;

    /// <summary>
    /// Accuracy only: d20 against the move's AC. A natural 20 is a critical hit: the next
    /// damage roll of this move gets twice as many damage dice.
    /// </summary>
    private void RollMoveAccuracy(PlayMoveRow row)
    {
        var move = row.Move;
        var r = Dice.RollAttack(null, Dice.ParseAc(move.AC));
        row.CritReady = r.Critical && row.HasDamage;
        Log($"{move.Name} accuracy" + (r.Critical ? " — CRIT!" : ""), r.AccuracyText);
    }

    /// <summary>Damage: move roll + ATK (physical) or SP.ATK (special), doubled dice after a critical accuracy roll.</summary>
    private void RollMoveDamage(PlayMoveRow row)
    {
        var move = row.Move;
        var stat = SheetRules.DamageStat(move);
        var critical = row.CritReady;
        row.CritReady = false;
        var title = $"{move.Name} damage" + (critical ? " (critical)" : "");
        try
        {
            var d = Dice.RollDamage(move.Roll ?? "", CurrentStat(stat), stat, critical);
            Log(title, $"{d.Total} — {d.Describe()}");
        }
        catch (FormatException)
        {
            Log(title, $"Can't roll \"{move.Roll}\".");
        }
    }

    // ── Custom roll (any move or effect, typed by the player) ────────────────

    /// <summary>Stats that can be added to a custom roll (several at once, e.g. ATK + SPEED).</summary>
    public IReadOnlyList<CustomStatOption> CustomStats { get; }
    [ObservableProperty] private string _customName = "";
    [ObservableProperty] private bool _customUseAc = true;
    [ObservableProperty] private int _customAc = 2;
    [ObservableProperty] private string _customDamage = "2d8+10";
    [ObservableProperty] private bool _customForceCrit;
    [ObservableProperty] private string _customStatsSummary = "No stat added";

    /// <summary>The selected stats with their current values.</summary>
    private IReadOnlyList<StatBonus> CustomStatBonuses() =>
        CustomStats.Where(o => o.IsSelected).Select(o => new StatBonus(o.Key, CurrentStat(o.Key))).ToList();

    private void UpdateCustomStatsSummary()
    {
        var bonuses = CustomStatBonuses();
        CustomStatsSummary = bonuses.Count == 0
            ? "No stat added"
            : string.Join(" ", bonuses.Select(b => $"+ {b.Label} {b.Value}")) + (bonuses.Count > 1 ? $" = +{bonuses.Sum(b => b.Value)}" : "");
    }

    private string CustomTitle => string.IsNullOrWhiteSpace(CustomName) ? "Custom roll" : CustomName.Trim();

    /// <summary>Accuracy check against the custom AC, then the custom damage (crit on 20).</summary>
    [RelayCommand]
    private void CustomAttack()
    {
        try
        {
            var stats = CustomStatBonuses();
            var r = Dice.RollAttack(CustomDamage, CustomUseAc ? CustomAc : null, stats);
            var critical = r.Critical || CustomForceCrit;
            var damage = r.Damage;
            if (CustomForceCrit && !r.Critical && !string.IsNullOrWhiteSpace(CustomDamage))
                damage = Dice.RollDamage(CustomDamage, stats, critical: true);
            Log(CustomTitle + (critical ? " — CRIT!" : ""),
                r.AccuracyText + (damage is { } d ? $"\nDamage {d.Total} — {d.Describe()}" : ""));
        }
        catch (FormatException ex)
        {
            Log(CustomTitle, "Invalid damage expression: " + ex.Message);
        }
    }

    /// <summary>The custom damage expression only (twice as many dice when "critical" is checked).</summary>
    [RelayCommand]
    private void CustomDamageRoll()
    {
        if (string.IsNullOrWhiteSpace(CustomDamage))
        {
            Log(CustomTitle, "Enter a damage expression, e.g. 2d8+10.");
            return;
        }
        try
        {
            var d = Dice.RollDamage(CustomDamage, CustomStatBonuses(), CustomForceCrit);
            Log(CustomTitle + " damage" + (CustomForceCrit ? " — CRIT" : ""), $"{d.Total} — {d.Describe()}");
        }
        catch (FormatException ex)
        {
            Log(CustomTitle, "Invalid damage expression: " + ex.Message);
        }
    }

    /// <summary>Accuracy check only.</summary>
    [RelayCommand]
    private void CustomAccuracy()
    {
        var r = Dice.RollAttack(null, CustomUseAc ? CustomAc : null);
        Log(CustomTitle + " accuracy" + (r.Critical ? " — CRIT!" : ""), r.AccuracyText);
    }

    [RelayCommand]
    private void ClearLog() => RollLog.Clear();

    // ═══ Sheet actions ══════════════════════════════════════════════════════

    [RelayCommand]
    private void NewSheet() => _newSheet();

    [RelayCommand]
    private void Edit()
    {
        if (_record != null && _services.Library.Sheets.Get(_record.Id) is { } fresh) _openEditor(fresh);
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (_record is null) return;
        var source = _services.Library.Sheets.Get(_record.Id) ?? _record;
        var copy = JsonSerializer.Deserialize<SheetRecord>(JsonSerializer.Serialize(source, JsonDefaults.Compact), JsonDefaults.Options)!;
        copy.Id = Guid.NewGuid().ToString();
        copy.Nickname = source.Nickname + " (copy)";
        copy.Play = null;
        copy.CreatedAt = DateTimeOffset.UtcNow;
        _services.Library.Sheets.Save(copy);
        _services.Dialogs.Notify($"\"{copy.Nickname}\" created.");
        Select(copy.Id);
        _services.NotifyLibraryChanged();
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (_record is null) return;
        if (!await _services.Dialogs.ConfirmAsync("Delete sheet", $"Delete the sheet \"{_record.Nickname}\"? This cannot be undone.")) return;
        _services.Library.Sheets.Delete(_record.Id);
        if (_activeTab != null) CloseTab(_activeTab);
        else Clear();
        _services.NotifyLibraryChanged();
        _services.Dialogs.Notify("Sheet deleted.");
    }

    /// <summary>Imports sheet files: website / app JSON files and the website's Markdown sheets.</summary>
    [RelayCommand]
    private async Task ImportJson()
    {
        var files = await _services.Dialogs.OpenFilesAsync("Import sheets (.json or website .md)", true,
            DialogService.SheetFiles, DialogService.JsonFiles, DialogService.MarkdownFiles);
        if (files.Count == 0) return;
        int sheets = 0;
        var warnings = new List<string>();
        string? lastId = null;
        foreach (var f in files)
        {
            try
            {
                var before = _services.Library.Sheets.List().Select(r => r.Id).ToHashSet();
                var report = WebsiteImporter.ImportPath(f, _services.Library, _services.Data);
                sheets += report.Sheets;
                warnings.AddRange(report.AllWarnings);
                var added = _services.Library.Sheets.List().Where(r => !before.Contains(r.Id)).ToList();
                // Imported sheets go into the folder open in the list.
                if (CurrentFolder is { } folder)
                    foreach (var r in added.Where(r => string.IsNullOrEmpty(r.Folder)))
                    {
                        r.Folder = folder;
                        _services.Library.Sheets.Save(r, touch: false);
                    }
                lastId = added.Select(r => r.Id).FirstOrDefault() ?? lastId;
            }
            catch (Exception ex) { warnings.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
        }
        _services.NotifyLibraryChanged();
        if (lastId != null) Select(lastId);
        _services.Dialogs.Notify($"Imported {sheets} sheet(s).");
        if (warnings.Count > 0)
            _services.Dialogs.Notify(string.Join("\n", warnings.Take(6)) + (warnings.Count > 6 ? $"\n… and {warnings.Count - 6} more" : ""),
                isError: sheets == 0, title: "Import notes");
    }

    // ═══ Exports ════════════════════════════════════════════════════════════

    private SheetSnapshot? ExportSnapshot()
    {
        if (_snapshot is null || !IsComplete)
        {
            _services.Dialogs.Notify(string.IsNullOrEmpty(IncompleteText) ? "Nothing to export." : IncompleteText, isError: true);
            return null;
        }
        return _snapshot;
    }

    private string FileBaseName()
    {
        var name = string.IsNullOrWhiteSpace(Nickname) ? SpeciesName : Nickname;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "sheet" : cleaned;
    }

    [RelayCommand]
    private async Task CopyGoogleDoc()
    {
        if (ExportSnapshot() is not { } snap) return;
        var url = _services.Settings.ImageBaseUrl;
        await _services.Dialogs.CopyHtmlAsync(SheetExporter.ToHtmlFragment(snap, url), SheetExporter.ToMarkdown(snap, url));
        _services.Dialogs.Notify("Copied — paste it into Google Docs or Word.");
    }

    [RelayCommand]
    private async Task CopyMarkdown()
    {
        if (ExportSnapshot() is not { } snap) return;
        await _services.Dialogs.CopyTextAsync(SheetExporter.ToMarkdown(snap, _services.Settings.ImageBaseUrl));
        _services.Dialogs.Notify("Markdown copied to the clipboard.");
    }

    [RelayCommand]
    private async Task ExportMarkdown()
    {
        if (ExportSnapshot() is not { } snap) return;
        var markdown = SheetExporter.ToMarkdown(snap, _services.Settings.ImageBaseUrl);
        if (!await _services.Dialogs.SaveFileAsync("Export sheet as Markdown", FileBaseName() + ".md", DialogService.MarkdownFiles,
                path => File.WriteAllTextAsync(path, markdown))) return;
        _services.Dialogs.Notify("Markdown exported.");
    }

    [RelayCommand]
    private async Task ExportHtml()
    {
        if (ExportSnapshot() is not { } snap) return;
        var html = SheetExporter.ToHtmlDocument(snap, _services.Settings.ImageBaseUrl);
        if (!await _services.Dialogs.SaveFileAsync("Export sheet as HTML", FileBaseName() + ".html", DialogService.HtmlFiles,
                path => File.WriteAllTextAsync(path, html))) return;
        _services.Dialogs.Notify("HTML exported.");
    }

    [RelayCommand]
    private async Task ExportJson()
    {
        if (_record is null) return;
        var record = _services.Library.Sheets.Get(_record.Id) ?? _record;
        var json = JsonSerializer.Serialize(record, JsonDefaults.Options);
        if (!await _services.Dialogs.SaveFileAsync("Export sheet file", FileBaseName() + ".json", DialogService.JsonFiles,
                path => File.WriteAllTextAsync(path, json))) return;
        _services.Dialogs.Notify("Sheet file exported.");
    }

    // ═══ Add to team ════════════════════════════════════════════════════════

    partial void OnSelectedTeamChanged(TeamRecord? value)
    {
        if (value is null)
        {
            TeamSlots = Array.Empty<TeamSlotOption>();
            return;
        }
        value.Normalize();
        TeamSlots = value.Slots.Select((s, i) => new TeamSlotOption(i,
            $"Slot {i + 1}" + (s is null ? " (empty)" : $" — {(string.IsNullOrWhiteSpace(s.Nickname) ? s.PokemonName : s.Nickname)}"))).ToList();
        SelectedTeamSlot = TeamSlots.FirstOrDefault(t => value.Slots[t.Index] is null) ?? TeamSlots.FirstOrDefault();
    }

    [RelayCommand]
    private void AddToTeam()
    {
        if (_record is null || SelectedTeam is null || SelectedTeamSlot is null) return;
        var team = _services.Library.Teams.Get(SelectedTeam.Id);
        if (team is null) return;
        var state = _record.State;
        team.Normalize();
        team.Slots[SelectedTeamSlot.Index] = new TeamSlot
        {
            CardId = _record.Id,
            PokemonName = state.ChosenPokemon,
            Nickname = string.IsNullOrWhiteSpace(_record.Nickname) ? state.ChosenPokemon : _record.Nickname,
            Level = state.Level > 0 ? SheetRules.FormatLevel(state.Level) : "",
            Rarity = (string.IsNullOrEmpty(state.Rarity) ? "Normal" : state.Rarity).ToLowerInvariant(),
        };
        _services.Library.Teams.Save(team);
        _services.Dialogs.Notify($"Added to \"{team.Name}\" (slot {SelectedTeamSlot.Index + 1}).");
        var id = team.Id;
        _services.NotifyLibraryChanged();
        SelectedTeam = Teams.FirstOrDefault(t => t.Id == id);
    }
}
