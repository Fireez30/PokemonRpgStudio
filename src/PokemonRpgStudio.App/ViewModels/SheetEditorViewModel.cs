using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.App.ViewModels;

/// <summary>
/// Sheet builder (website "Pokemon Generator"): creates and edits one sheet at a time.
/// Using and viewing saved sheets happens in <see cref="SheetsViewModel"/>.
/// All rule logic lives in <see cref="SheetRules"/>; this class only maps it to the UI.
/// </summary>
public sealed partial class SheetEditorViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly Action<string?> _onDone;
    private readonly DispatcherTimer _autoSaveTimer;
    private SheetRecord? _record;          // the saved record being edited (null = not saved yet)
    private SheetState _state = new();
    private bool _dirty;
    private bool _loading;

    public override string Title => "Sheet Editor";
    public override string Glyph => "✏";

    public override void OnDeactivated() => FlushAutoSave();

    public IReadOnlyList<string> RarityOptions => SheetRules.Rarities;
    public IReadOnlyList<string> CardOptions => SheetRules.Cards;
    public IReadOnlyList<string> GenderOptions => SheetRules.Genders;
    public IReadOnlyList<string> StatOptions => SheetRules.StatNames;
    public IReadOnlyList<string> StatusOptions => SheetRules.Statuses;

    // ── Editor state ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _nickname = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string? _status = SheetRules.DefaultStatus;
    [ObservableProperty] private bool _autoSave = true;
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private IReadOnlyList<string> _pokemonNames = Array.Empty<string>();
    [ObservableProperty] private string? _pokemonName;
    [ObservableProperty] private PokemonSpecies? _species;
    [ObservableProperty] private string _speciesWarning = "";

    [ObservableProperty] private double _level;
    [ObservableProperty] private int _experience;
    [ObservableProperty] private string? _rarity;
    [ObservableProperty] private string? _card;
    [ObservableProperty] private string? _gender;
    [ObservableProperty] private string? _buffedStat;
    [ObservableProperty] private string? _loweredStat;
    [ObservableProperty] private int _remainingRolls;
    [ObservableProperty] private string _natureText = "";
    [ObservableProperty] private IReadOnlyList<NatureRollVm> _natureRolls = Array.Empty<NatureRollVm>();

    public bool HasNatureRolls => NatureRolls.Count > 0;

    partial void OnNatureRollsChanged(IReadOnlyList<NatureRollVm> value) => OnPropertyChanged(nameof(HasNatureRolls));

    [ObservableProperty] private IReadOnlyList<AbilityTierVm> _abilityTiers = Array.Empty<AbilityTierVm>();
    /// <summary>Abilities added by hand, outside the species' list (unchecking one removes it).</summary>
    [ObservableProperty] private IReadOnlyList<AbilityOption> _extraAbilities = Array.Empty<AbilityOption>();
    [ObservableProperty] private string _abilitySearch = "";
    [ObservableProperty] private IReadOnlyList<AbilityPickVm> _abilityCandidates = Array.Empty<AbilityPickVm>();
    [ObservableProperty] private bool _isAbilityPickerOpen;
    private List<AbilityPickVm> _allAbilityCandidates = new();
    public bool HasExtraAbilities => ExtraAbilities.Count > 0;
    public int CheckedAbilityCount => _allAbilityCandidates.Count(c => c.IsChecked);
    public string AddCheckedAbilitiesText => CheckedAbilityCount > 0 ? $"Add selected ({CheckedAbilityCount})" : "Add selected";
    /// <summary>The search text, when it names no known ability: it can be added as a homebrew ability.</summary>
    public string AddTypedAbilityText => $"Add \"{AbilitySearch.Trim()}\"";
    public bool CanAddTypedAbility => AbilitySearch.Trim().Length > 0
        && _services.Data.FindAbility(AbilitySearch.Trim()) is null
        && !SheetAbilityNames().Contains(AbilitySearch.Trim(), StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<StatRowVm> StatRows { get; } = new();
    [ObservableProperty] private int _availablePoints;
    public bool IsOverstated => AvailablePoints < 0;
    [ObservableProperty] private bool _finalStatsReady;
    [ObservableProperty] private string _hitPoints = "—";
    [ObservableProperty] private string _evades = "—";
    [ObservableProperty] private int _tutorPoints;
    [ObservableProperty] private string _tutorPointsDetail = "";
    [ObservableProperty] private string _tutorNotes = "";

    [ObservableProperty] private IReadOnlyList<MoveOption> _levelMoveOptions = Array.Empty<MoveOption>();
    [ObservableProperty] private IReadOnlyList<MoveOption> _eggMoveOptions = Array.Empty<MoveOption>();
    [ObservableProperty] private IReadOnlyList<MoveOption> _tutorMoveOptions = Array.Empty<MoveOption>();
    [ObservableProperty] private IReadOnlyList<MoveOption> _tmMoveOptions = Array.Empty<MoveOption>();
    [ObservableProperty] private MoveOption? _selectedLevelMove;
    [ObservableProperty] private MoveOption? _selectedEggMove;
    [ObservableProperty] private MoveOption? _selectedTutorMove;
    [ObservableProperty] private MoveOption? _selectedTmMove;
    [ObservableProperty] private IReadOnlyList<MoveRowVm> _chosenMoveRows = Array.Empty<MoveRowVm>();
    [ObservableProperty] private IReadOnlyList<MoveRowVm> _eggMoveRows = Array.Empty<MoveRowVm>();
    [ObservableProperty] private IReadOnlyList<MoveRowVm> _tutorMoveRows = Array.Empty<MoveRowVm>();
    [ObservableProperty] private IReadOnlyList<MoveRowVm> _tmMoveRows = Array.Empty<MoveRowVm>();
    [ObservableProperty] private IReadOnlyList<SheetMovePreview> _sheetMoves = Array.Empty<SheetMovePreview>();
    [ObservableProperty] private string _movesSummary = "";

    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private string _missingText = "";

    public bool HasSpecies => Species != null;
    public bool IsSaved => _record != null;
    public string HeaderTitle => _record is null ? "New sheet" : $"Editing \"{_record.Nickname}\"";
    public bool ShowAdvancedLevelHint => Species != null && Level > 0 && Level < SheetRules.HighAbilityLevel;
    public string LevelHint => Level < SheetRules.AdvancedAbilityLevel
        ? $"Advanced abilities usually unlock at level {SheetRules.AdvancedAbilityLevel}, high abilities at level {SheetRules.HighAbilityLevel}: they can be picked earlier (tutor)."
        : $"High abilities usually unlock at level {SheetRules.HighAbilityLevel}: they can be picked earlier (tutor).";
    public string ImageName => Species?.Name ?? "";

    /// <param name="onDone">Called when the user leaves the editor, with the id of the saved sheet (or null).</param>
    public SheetEditorViewModel(AppServices services, Action<string?> onDone)
    {
        _services = services;
        _onDone = onDone;
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            if (_dirty && CanSave()) SaveInternal(silent: true);
        };
        PokemonNames = services.Data.Pokemon.Select(p => p.Name).ToList();
        services.GameDataChanged += () =>
        {
            PokemonNames = _services.Data.Pokemon.Select(p => p.Name).ToList();
            if (IsEditing) LoadState(_state, keepDirty: true);
        };
    }

    // ═══ Opening / closing ══════════════════════════════════════════════════

    /// <summary>Opens a saved sheet for editing.</summary>
    public void Open(SheetRecord record)
    {
        FlushAutoSave();
        _record = record;
        _loading = true;
        Nickname = record.Nickname;
        Notes = record.Notes;
        Status = SheetRules.Statuses.Contains(record.Status) ? record.Status : SheetRules.DefaultStatus;
        _loading = false;
        if (string.IsNullOrEmpty(record.State.ChosenPokemon)) record.State.ChosenPokemon = record.PokemonName;
        LoadState(record.State.Clone(), keepDirty: false);
        IsEditing = true;
        SaveStatus = $"Saved {record.UpdatedAt.ToLocalTime():g}";
        NotifyRecordChanged();
    }

    /// <summary>Starts a new sheet, optionally for a given species (Pokédex "Create a sheet").</summary>
    /// <summary>Folder given to the sheet being created (the folder open on the Sheets page).</summary>
    private string _newSheetFolder = "";

    public void StartNewSheet(PokemonSpecies? species = null, string? folder = null)
    {
        FlushAutoSave();
        _record = null;
        _newSheetFolder = folder ?? "";
        _loading = true;
        Nickname = "";
        Notes = "";
        Status = SheetRules.DefaultStatus;
        _loading = false;
        LoadState(new SheetState { Rarity = "", Card = "" }, keepDirty: false);
        IsEditing = true;
        SaveStatus = "New sheet — pick a Pokémon and a nickname to save it.";
        NotifyRecordChanged();
        if (species != null) PokemonName = species.Name;
    }

    [RelayCommand]
    private void NewBlankSheet() => StartNewSheet();

    private void NotifyRecordChanged()
    {
        OnPropertyChanged(nameof(IsSaved));
        OnPropertyChanged(nameof(HeaderTitle));
    }

    /// <summary>Saves and goes back to the sheet view.</summary>
    [RelayCommand]
    private async Task Done()
    {
        _autoSaveTimer.Stop();
        if (_dirty)
        {
            if (CanSave()) SaveInternal(silent: true);
            else if (!await _services.Dialogs.ConfirmAsync("Unsaved sheet",
                         "This sheet has no Pokémon or no nickname, so it can't be saved. Discard it?", "Discard")) return;
        }
        _dirty = false;
        var id = _record?.Id;
        IsEditing = false;
        _onDone(id);
    }

    // ═══ Save ═══════════════════════════════════════════════════════════════

    private bool CanSave() => !string.IsNullOrWhiteSpace(Nickname) && !string.IsNullOrWhiteSpace(_state.ChosenPokemon);

    [RelayCommand]
    private void Save()
    {
        if (!IsEditing) return;
        if (!CanSave())
        {
            _services.Dialogs.Notify("Pick a Pokémon and enter a nickname to save the sheet.", isError: true);
            return;
        }
        SaveInternal(silent: false);
    }

    private void SaveInternal(bool silent)
    {
        try
        {
            var isNew = _record is null;
            var record = _record ?? new SheetRecord { Folder = _newSheetFolder };
            // The play state (HP, injuries…) and the folder are changed from the Sheets page: keep the latest ones on disk.
            if (!isNew && _services.Library.Sheets.Get(record.Id) is { } onDisk)
            {
                record.Play = onDisk.Play ?? record.Play;
                record.Folder = onDisk.Folder;
            }
            record.Nickname = Nickname.Trim();
            record.PokemonName = _state.ChosenPokemon;
            record.Notes = Notes;
            record.Status = Status ?? SheetRules.DefaultStatus;
            record.State = _state.Clone();
            _services.Library.Sheets.Save(record);
            _record = record;
            _dirty = false;
            SaveStatus = $"Saved {DateTime.Now:t}";
            NotifyRecordChanged();
            _services.NotifyLibraryChanged();
            if (!silent) _services.Dialogs.Notify($"\"{record.Nickname}\" saved.");
        }
        catch (Exception ex)
        {
            SaveStatus = "Save failed";
            _services.Dialogs.Notify("Save failed: " + ex.Message, isError: true);
        }
    }

    /// <summary>Writes pending changes immediately (page switch, window close).</summary>
    public void FlushAutoSave()
    {
        _autoSaveTimer.Stop();
        if (_dirty && AutoSave && CanSave()) SaveInternal(silent: true);
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        SaveStatus = CanSave() ? (AutoSave ? "Unsaved changes…" : "Unsaved changes") : "Not saved — enter a nickname to save";
        if (AutoSave && CanSave())
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
    }

    partial void OnNicknameChanged(string value) => MarkDirty();
    partial void OnNotesChanged(string value) => MarkDirty();
    partial void OnStatusChanged(string? value) => MarkDirty();

    // ═══ Loading state into the UI ══════════════════════════════════════════

    private void LoadState(SheetState state, bool keepDirty)
    {
        _loading = true;
        _state = state;
        var species = _services.Data.FindPokemon(state.ChosenPokemon);
        Species = species;
        PokemonName = species?.Name ?? (string.IsNullOrEmpty(state.ChosenPokemon) ? null : state.ChosenPokemon);
        SpeciesWarning = species is null && !string.IsNullOrEmpty(state.ChosenPokemon)
            ? $"\"{state.ChosenPokemon}\" is not in the Pokédex. Pick a Pokémon to rebuild this sheet."
            : "";
        Level = state.Level;
        Experience = state.Experience;
        Rarity = NullIfEmpty(state.Rarity);
        Card = NullIfEmpty(state.Card);
        Gender = NullIfEmpty(state.Gender);
        BuffedStat = NullIfEmpty(state.BuffedStat);
        LoweredStat = NullIfEmpty(state.LoweredStat);
        TutorNotes = state.TutorNotes;
        BuildStatRows();
        RebuildAbilities();
        RebuildMoveOptions();
        RebuildMoveRows();
        Refresh();
        _loading = false;
        if (!keepDirty) _dirty = false;
        OnPropertyChanged(nameof(HasSpecies));
        OnPropertyChanged(nameof(ImageName));
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    partial void OnPokemonNameChanged(string? value)
    {
        if (_loading || string.IsNullOrWhiteSpace(value)) return;
        var species = _services.Data.FindPokemon(value);
        if (species is null || species.Name == _state.ChosenPokemon && Species != null) return;

        // Picking another Pokémon starts the sheet over (website behaviour); nickname and notes are kept.
        var fresh = SheetRules.NewSheet(species);
        LoadState(fresh, keepDirty: true);
        if (string.IsNullOrWhiteSpace(Nickname))
        {
            _loading = true;
            Nickname = species.DisplayName;
            _loading = false;
        }
        MarkDirty();
    }

    partial void OnLevelChanged(double value)
    {
        if (_loading) return;
        var clamped = Math.Round(Math.Clamp(value, 1, SheetRules.MaxLevel), 2);
        if (clamped != value)
        {
            Level = clamped;
            return;
        }
        _state.Level = clamped;
        // Abilities are kept when the level goes down: they can be taught early (tutor mechanic).
        RebuildAbilities();
        RebuildMoveOptions();
        Changed();
    }

    partial void OnExperienceChanged(int value)
    {
        if (_loading) return;
        var clamped = Math.Clamp(value, 0, SheetRules.MaxExperience);
        if (clamped != value)
        {
            Experience = clamped;
            return;
        }
        _state.Experience = clamped;
        MarkDirty();
    }

    partial void OnRarityChanged(string? value) { if (!_loading) { _state.Rarity = value ?? ""; Changed(); } }
    partial void OnCardChanged(string? value) { if (!_loading) { _state.Card = value ?? ""; Changed(); } }
    partial void OnGenderChanged(string? value) { if (!_loading) { _state.Gender = value ?? ""; Changed(); } }
    partial void OnBuffedStatChanged(string? value) { if (!_loading) { _state.BuffedStat = value ?? ""; Changed(); } }
    partial void OnLoweredStatChanged(string? value) { if (!_loading) { _state.LoweredStat = value ?? ""; Changed(); } }

    partial void OnTutorNotesChanged(string value) { if (!_loading) { _state.TutorNotes = value; MarkDirty(); } }

    partial void OnAutoSaveChanged(bool value)
    {
        if (value && _dirty) MarkDirty();
    }

    private void Changed()
    {
        Refresh();
        MarkDirty();
    }

    // ═══ Derived values ═════════════════════════════════════════════════════

    private void BuildStatRows()
    {
        StatRows.Clear();
        foreach (var stat in SheetRules.AllStats)
        {
            var label = stat switch { Stat.SPATK => "SP.ATK", Stat.SPDEF => "SP.DEF", _ => stat.ToString() };
            StatRows.Add(new StatRowVm(stat, label, OnStatPointsChanged));
        }
    }

    private void OnStatPointsChanged(StatRowVm row, int value)
    {
        if (_loading) return;
        // No upper bound: a Pokémon may be overstated (remaining points below 0).
        var clamped = Math.Max(0, value);
        _state.PointsByStat[row.Stat.ToString()] = clamped;
        if (clamped != value) Dispatcher.UIThread.Post(() => row.SetPointsSilently(clamped));
        Changed();
    }

    private void Refresh()
    {
        var s = _state;
        var sp = Species;
        RemainingRolls = s.RemainingRolls;
        NatureText = s.BuffedStat != "" && s.LoweredStat != "" ? SheetRules.NatureName(s.BuffedStat, s.LoweredStat) : "No nature yet";
        RebuildNatureRolls();
        AvailablePoints = SheetRules.AvailablePoints(s.Level, s.PointsByStat);
        OnPropertyChanged(nameof(IsOverstated));

        var natureSet = s.BuffedStat != "" && s.LoweredStat != "";
        var buffs = sp != null && natureSet && s.Card != "" && s.Rarity != ""
            ? SheetRules.ComputeStatsBuff(s.Card, s.Rarity, s.BuffedStat, s.LoweredStat)
            : null;
        FinalStatsReady = buffs != null && sp != null && s.Level > 0 && AvailablePoints <= 0;
        var final = FinalStatsReady ? SheetRules.ComputeFinalStats(sp!, s.Card, s.Rarity, s.BuffedStat, s.LoweredStat, s.PointsByStat) : null;

        foreach (var row in StatRows)
        {
            var key = row.Stat.ToString();
            row.Base = sp?.BaseStat(row.Stat) ?? 0;
            row.SetPointsSilently(s.Points(row.Stat));
            row.Buff = buffs is null ? "—" : Formatting.SignedNumber(buffs[key]);
            row.Final = final is null ? "—" : final[key].ToString();
            row.IsBuffed = s.BuffedStat == key && s.BuffedStat != s.LoweredStat;
            row.IsLowered = s.LoweredStat == key && s.BuffedStat != s.LoweredStat;
        }

        if (final != null)
        {
            HitPoints = $"{SheetRules.HitPoints(s.Level, final["HP"])}";
            Evades = $"Phys {SheetRules.Evade(final["DEF"])} · Spec {SheetRules.Evade(final["SPDEF"])} · Speed {SheetRules.Evade(final["SPD"])}";
        }
        else
        {
            HitPoints = "—";
            Evades = "—";
        }
        TutorPoints = SheetRules.TutorPoints(s.Level, s.TutorPointsAdjustment);
        var earned = SheetRules.TutorPoints(s.Level);
        TutorPointsDetail = s.TutorPointsAdjustment == 0
            ? $"{earned} from level (1 every 5 levels)"
            : $"{earned} from level {Formatting.SignedNumber(s.TutorPointsAdjustment)} by hand";
        RemoveTutorPointCommand.NotifyCanExecuteChanged();

        RefreshMoveRowState();

        IsComplete = sp != null && SheetRules.IsComplete(s);
        var missing = SheetRules.MissingRequirements(s);
        MissingText = missing.Count == 0 ? "The sheet is complete." : "To finish the sheet: " + string.Join(" · ", missing);
        RollNatureCommand.NotifyCanExecuteChanged();
        RollGenderCommand.NotifyCanExecuteChanged();
        AutoPointsCommand.NotifyCanExecuteChanged();
        foreach (var tier in AbilityTiers) tier.RollCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowAdvancedLevelHint));
        OnPropertyChanged(nameof(LevelHint));
    }

    // ═══ Gender / nature / points ═══════════════════════════════════════════

    [RelayCommand(CanExecute = nameof(CanRollGender))]
    private void RollGender()
    {
        if (Species is null) return;
        Gender = SheetRules.RollGender(Species);
    }

    private bool CanRollGender() => Species != null && string.IsNullOrEmpty(_state.Gender);

    [RelayCommand(CanExecute = nameof(CanRollNature))]
    private void RollNature()
    {
        _state.RemainingRolls = Math.Max(0, _state.RemainingRolls - 1);
        var (buff, lowered) = SheetRules.RollNature();
        // Keep every roll so the player can come back to a previous one.
        (_state.RolledNatures ??= new()).Add(new RolledNature(buff, lowered));
        ApplyNature(buff, lowered);
    }

    private bool CanRollNature() => Species != null && _state.RemainingRolls > 0;

    /// <summary>Uses one of the natures rolled before.</summary>
    private void PickRolledNature(RolledNature nature) => ApplyNature(nature.Buffed, nature.Lowered);

    private void ApplyNature(string buff, string lowered)
    {
        _loading = true;
        BuffedStat = buff;
        LoweredStat = lowered;
        _loading = false;
        _state.BuffedStat = buff;
        _state.LoweredStat = lowered;
        Changed();
    }

    /// <summary>The natures rolled for this sheet, the one in use highlighted.</summary>
    private void RebuildNatureRolls()
    {
        var rolls = _state.RolledNatures ?? new List<RolledNature>();
        NatureRolls = rolls.Select((n, i) => new NatureRollVm(i + 1, SheetRules.NatureName(n.Buffed, n.Lowered),
            n.Buffed == _state.BuffedStat && n.Lowered == _state.LoweredStat, new RelayCommand(() => PickRolledNature(n)))).ToList();
    }

    [RelayCommand(CanExecute = nameof(CanAutoPoints))]
    private void AutoPoints()
    {
        if (Species is null) return;
        var extra = SheetRules.ComputePointsAutomatically(Species, SheetRules.AvailablePoints(_state.Level, _state.PointsByStat));
        foreach (var kv in extra) _state.PointsByStat[kv.Key] = _state.PointsByStat.GetValueOrDefault(kv.Key) + kv.Value;
        Changed();
    }

    private bool CanAutoPoints() => Species != null && SheetRules.AvailablePoints(_state.Level, _state.PointsByStat) > 0;

    [RelayCommand]
    private void ResetPoints()
    {
        _state.PointsByStat = SheetRulesDefaults.EmptyStats();
        Changed();
    }

    // ═══ Tutor points ═══════════════════════════════════════════════════════

    /// <summary>One more tutor point than the level gives (reward, item…).</summary>
    [RelayCommand]
    private void AddTutorPoint()
    {
        // After a level drop the adjustment may hide more points than the level gives: start from 0 again.
        _state.TutorPointsAdjustment = Math.Max(_state.TutorPointsAdjustment, -SheetRules.TutorPoints(_state.Level)) + 1;
        Changed();
    }

    /// <summary>One tutor point less (spent on a move / ability…); the total never goes below 0.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveTutorPoint))]
    private void RemoveTutorPoint()
    {
        if (!CanRemoveTutorPoint()) return;
        _state.TutorPointsAdjustment--;
        Changed();
    }

    private bool CanRemoveTutorPoint() => SheetRules.TutorPoints(_state.Level, _state.TutorPointsAdjustment) > 0;

    [RelayCommand]
    private void ResetTutorPoints()
    {
        _state.TutorPointsAdjustment = 0;
        Changed();
    }

    // ═══ Abilities ══════════════════════════════════════════════════════════

    private void RebuildAbilities()
    {
        var sp = Species;
        var s = _state;
        var tiers = new List<AbilityTierVm>();
        RebuildExtraAbilities();
        if (sp is null || s.Level <= 0)
        {
            AbilityTiers = tiers;
            return;
        }

        AbilityTierVm Tier(string label, IEnumerable<string> names, List<string> selected, int unlockLevel = 0)
        {
            var note = s.Level < unlockLevel ? $"usually Lv {unlockLevel} · tutor" : "";
            var options = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => new AbilityOption(n, _services.Data.FindAbility(n)?.Effect ?? "",
                    selected.Contains(n, StringComparer.OrdinalIgnoreCase), opt => ToggleAbility(selected, opt)))
                .ToList();
            // Keep abilities stored on the sheet even if the species data no longer lists them.
            foreach (var extra in selected.Where(n => !options.Any(o => o.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList())
                options.Add(new AbilityOption(extra, _services.Data.FindAbility(extra)?.Effect ?? "", true, opt => ToggleAbility(selected, opt)));
            var pool = options.Select(o => o.Name).ToList();
            return new AbilityTierVm(label, options, () =>
            {
                if (pool.Count == 0) return;
                selected.Clear();
                selected.Add(pool[Random.Shared.Next(pool.Count)]);
                RebuildAbilities();
                Changed();
            }, () => selected.Count == 0 && pool.Count > 0, note);
        }

        // Every tier is always offered: an ability can be taken before its usual level (tutor).
        tiers.Add(Tier("Base", sp.BaseAbilities.Select(a => a.Name), s.BaseAbilities));
        tiers.Add(Tier("Advanced", sp.AdvancedAbilities.Select(a => a.Name), s.AdvancedAbilities, SheetRules.AdvancedAbilityLevel));
        tiers.Add(Tier("High", sp.HighAbilities.Select(a => a.Name), s.HighAbilities, SheetRules.HighAbilityLevel));
        if (sp.CustomAbilities is { Count: > 0 })
            foreach (var (section, ability) in sp.CustomAbilities)
                tiers.Add(Tier(section, new[] { ability.Name }, s.CustomAbilities, SheetRules.CustomAbilityLevel));
        AbilityTiers = tiers;
    }

    // ── Extra abilities (outside the species' list) ──

    /// <summary>Every ability on the sheet, any tier, plus the ones the species offers.</summary>
    private HashSet<string> SheetAbilityNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var s = _state;
        foreach (var n in s.BaseAbilities.Concat(s.AdvancedAbilities).Concat(s.HighAbilities).Concat(s.CustomAbilities).Concat(s.ExtraAbilities))
            names.Add(n.Trim());
        if (Species is { } sp)
            foreach (var a in sp.BaseAbilities.Concat(sp.AdvancedAbilities).Concat(sp.HighAbilities).Concat(sp.CustomAbilities?.Values ?? Enumerable.Empty<AbilityRef>()))
                names.Add(a.Name.Trim());
        return names;
    }

    private void RebuildExtraAbilities()
    {
        var list = _state.ExtraAbilities;
        ExtraAbilities = list.Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => new AbilityOption(n, _services.Data.FindAbility(n)?.Effect ?? "", true, opt =>
            {
                if (_loading || opt.IsSelected) return;
                list.RemoveAll(x => x.Equals(opt.Name, StringComparison.OrdinalIgnoreCase));
                RebuildExtraAbilities();
                Changed();
            }))
            .ToList();
        OnPropertyChanged(nameof(HasExtraAbilities));

        // Picker: every known ability not already on the sheet nor offered by the species.
        var taken = SheetAbilityNames();
        var stillChecked = _allAbilityCandidates.Where(c => c.IsChecked).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _allAbilityCandidates = _services.Data.Abilities
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !taken.Contains(a.Name.Trim()))
            .GroupBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AbilityPickVm(a.Name.Trim(), a.Effect ?? "", OnCandidateChecked) { IsChecked = stillChecked.Contains(a.Name.Trim()) })
            .ToList();
        FilterAbilityCandidates();
        OnCandidateChecked();
    }

    private void FilterAbilityCandidates()
    {
        var terms = Core.Storage.SheetSearch.Terms(AbilitySearch);
        AbilityCandidates = terms.Count == 0
            ? _allAbilityCandidates
            : _allAbilityCandidates.Where(c => Core.Storage.SheetSearch.Matches(Core.Storage.SheetSearch.Normalize(c.Name), AbilitySearch)
                                               || c.IsChecked).ToList();
        OnPropertyChanged(nameof(CanAddTypedAbility));
        OnPropertyChanged(nameof(AddTypedAbilityText));
        AddTypedAbilityCommand.NotifyCanExecuteChanged();
    }

    private void OnCandidateChecked()
    {
        OnPropertyChanged(nameof(CheckedAbilityCount));
        OnPropertyChanged(nameof(AddCheckedAbilitiesText));
        AddCheckedAbilitiesCommand.NotifyCanExecuteChanged();
        ClearCheckedAbilitiesCommand.NotifyCanExecuteChanged();
    }

    partial void OnAbilitySearchChanged(string value) => FilterAbilityCandidates();

    [RelayCommand]
    private void ToggleAbilityPicker() => IsAbilityPickerOpen = !IsAbilityPickerOpen;

    [RelayCommand(CanExecute = nameof(HasCheckedAbilities))]
    private void AddCheckedAbilities()
    {
        var names = _allAbilityCandidates.Where(c => c.IsChecked).Select(c => c.Name).ToList();
        foreach (var c in _allAbilityCandidates) c.IsChecked = false;
        AddExtraAbilities(names);
    }

    private bool HasCheckedAbilities() => CheckedAbilityCount > 0;

    [RelayCommand(CanExecute = nameof(HasCheckedAbilities))]
    private void ClearCheckedAbilities()
    {
        foreach (var c in _allAbilityCandidates) c.IsChecked = false;
    }

    [RelayCommand(CanExecute = nameof(CanAddTypedAbility))]
    private void AddTypedAbility() => AddExtraAbilities(new[] { AbilitySearch.Trim() });

    private void AddExtraAbilities(IEnumerable<string> names)
    {
        var taken = SheetAbilityNames();
        foreach (var n in names.Select(n => n.Trim()).Where(n => n.Length > 0))
            if (taken.Add(n)) _state.ExtraAbilities.Add(n);
        AbilitySearch = "";
        RebuildExtraAbilities();
        Changed();
    }

    private void ToggleAbility(List<string> selected, AbilityOption option)
    {
        if (_loading) return;
        selected.RemoveAll(n => n.Equals(option.Name, StringComparison.OrdinalIgnoreCase));
        if (option.IsSelected) selected.Add(option.Name);
        Changed();
    }

    // ═══ Moves ══════════════════════════════════════════════════════════════

    private void RebuildMoveOptions()
    {
        var sp = Species;
        var data = _services.Data;
        if (sp is null)
        {
            LevelMoveOptions = EggMoveOptions = TutorMoveOptions = TmMoveOptions = Array.Empty<MoveOption>();
            return;
        }
        // The whole level-up learnset: moves above the current level can be taught early (tutor).
        LevelMoveOptions = sp.Moves.OrderBy(m => m.Level)
            .Select(m =>
            {
                var types = data.FindMove(m.Name)?.Types ?? m.AllTypes.ToList();
                var early = m.Level > _state.Level ? " · early (tutor)" : "";
                return new MoveOption(m.Name.Replace(":", "").Trim(), $"Lv {m.Level} — {m.Name} ({string.Join("/", types.Select(t => t.Trim()))}){early}", types);
            })
            .GroupBy(o => o.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

        static IReadOnlyList<MoveOption> Options(IEnumerable<MoveData> moves) =>
            moves.Select(m => new MoveOption(m.Name, $"{m.Name} ({m.TypesLabel})", m.Types)).ToList();
        EggMoveOptions = Options(data.ResolveMoves(data.EffectiveEggMoves(sp)));
        TutorMoveOptions = Options(data.ResolveMoves(sp.TutorMoves));
        TmMoveOptions = Options(data.ResolveMoves(sp.TmMoves));
    }

    private List<string> ListFor(MoveKind kind) => kind switch
    {
        MoveKind.LevelUp => _state.ChosenMoves,
        MoveKind.Egg => _state.EggMoves,
        MoveKind.Tutor => _state.TutorMoves,
        _ => _state.TmMoves,
    };

    private List<string> LocksFor(MoveKind kind) => kind switch
    {
        MoveKind.Egg => _state.LockedEggMoves,
        MoveKind.Tutor => _state.LockedTutorMoves,
        MoveKind.Tm => _state.LockedTmMoves,
        _ => new List<string>(),
    };

    private void RebuildMoveRows()
    {
        // Sheets store names only: details are looked up in moves.json each time.
        IReadOnlyList<MoveRowVm> Rows(MoveKind kind) => ListFor(kind)
            .Select(name => SheetRules.ResolveMove(name, Species, _services.Data.FindMove))
            .Select((m, i) => new MoveRowVm(kind, m, _state.DbBuffByMove.GetValueOrDefault(m.Name), RemoveMove, ToggleLock, OnDbBuffChanged)
            {
                StartsSecondGroup = kind == MoveKind.LevelUp && i == SheetRules.FirstTableMoves,
            })
            .ToList();
        ChosenMoveRows = Rows(MoveKind.LevelUp);
        EggMoveRows = Rows(MoveKind.Egg);
        TutorMoveRows = Rows(MoveKind.Tutor);
        TmMoveRows = Rows(MoveKind.Tm);
    }

    private IEnumerable<MoveRowVm> AllMoveRows => ChosenMoveRows.Concat(EggMoveRows).Concat(TutorMoveRows).Concat(TmMoveRows);

    private void RefreshMoveRowState()
    {
        var sp = Species;
        foreach (var row in AllMoveRows)
        {
            row.FinalRoll = sp is null ? row.Move.Roll ?? "" : SheetRules.UpdatedRoll(sp, _state.DbBuffByMove, row.Move);
            row.IsLocked = LocksFor(row.Kind).Contains(row.Name, StringComparer.OrdinalIgnoreCase);
            row.CanLock = true;
        }
        SheetMoves = sp is null
            ? Array.Empty<SheetMovePreview>()
            : SheetRules.PickMoves(_state, sp, _services.Data.FindMove).Select((m, i) => new SheetMovePreview(i, m)).ToList();
        MovesSummary = $"{_state.ChosenMoves.Count} level-up move(s) · {SheetRules.TotalLockedMoves(_state)} locked · " +
                       $"{SheetMoves.Count} on the sheet";
    }

    private void OnDbBuffChanged(MoveRowVm row, int value)
    {
        if (_loading) return;
        if (!row.CanHaveDbBuff)
        {
            // Status moves never get a DB bonus (also drops one stored by an older version / import).
            if (_state.DbBuffByMove.Remove(row.Name)) Changed();
            if (row.DbBuff != 0) row.DbBuff = 0;
            return;
        }
        var clamped = Math.Clamp(value, 0, SheetRules.MaxDbBuff);
        if (clamped == 0) _state.DbBuffByMove.Remove(row.Name);
        else _state.DbBuffByMove[row.Name] = clamped;
        // The same move may be listed twice (e.g. egg + TM): keep their DB in sync.
        foreach (var other in AllMoveRows.Where(r => r != row && r.Name == row.Name && r.DbBuff != clamped))
            other.DbBuff = clamped;
        Changed();
    }

    private void RemoveMove(MoveRowVm row)
    {
        ListFor(row.Kind).RemoveAll(n => n.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
        LocksFor(row.Kind).RemoveAll(n => n.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
        RebuildMoveRows();
        Changed();
    }

    private void ToggleLock(MoveRowVm row)
    {
        var locks = LocksFor(row.Kind);
        if (locks.Contains(row.Name, StringComparer.OrdinalIgnoreCase))
            locks.RemoveAll(n => n.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
        else
            locks.Add(row.Name);
        Changed();
    }

    private void AddMove(MoveKind kind, MoveOption? option)
    {
        if (option is null || Species is null) return;
        var list = ListFor(kind);
        if (list.Any(n => n.Equals(option.Name, StringComparison.OrdinalIgnoreCase)))
        {
            _services.Dialogs.Notify($"{option.Name} is already in the list.");
            return;
        }
        list.Add(_services.Data.FindMove(option.Name)?.Name ?? option.Name);
        RebuildMoveRows();
        Changed();
    }

    [RelayCommand] private void AddLevelMove() { AddMove(MoveKind.LevelUp, SelectedLevelMove); SelectedLevelMove = null; }
    [RelayCommand] private void AddEggMove() { AddMove(MoveKind.Egg, SelectedEggMove); SelectedEggMove = null; }
    [RelayCommand] private void AddTutorMove() { AddMove(MoveKind.Tutor, SelectedTutorMove); SelectedTutorMove = null; }
    [RelayCommand] private void AddTmMove() { AddMove(MoveKind.Tm, SelectedTmMove); SelectedTmMove = null; }

    [RelayCommand]
    private void UnlockAll()
    {
        _state.LockedEggMoves.Clear();
        _state.LockedTmMoves.Clear();
        _state.LockedTutorMoves.Clear();
        Changed();
    }
}
