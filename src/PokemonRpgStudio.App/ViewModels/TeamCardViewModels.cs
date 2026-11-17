using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.App.ViewModels;

/// <summary>Shared logic of both team pages: theme, trainer image, live PNG preview, export.</summary>
public abstract partial class TeamCardPageBase : PageViewModel
{
    protected readonly AppServices Services;
    private readonly DispatcherTimer _renderTimer;

    public IReadOnlyList<string> ThemeTypes => TeamCardRenderer.ThemeTypes;
    public IReadOnlyList<string> RarityOptions { get; } = new[] { "normal", "shiny", "platine" };

    [ObservableProperty] private string _teamName = "";
    [ObservableProperty] private string _themeType = "Fire";
    [ObservableProperty] private string? _trainerImagePath;
    [ObservableProperty] private Bitmap? _preview;
    [ObservableProperty] private string _previewStatus = "";

    protected TeamCardPageBase(AppServices services)
    {
        Services = services;
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _renderTimer.Tick += (_, _) => { _renderTimer.Stop(); RenderNow(); };
    }

    protected abstract IReadOnlyList<TeamCardSlot?> BuildSlots();

    protected TeamCardSlot? MakeSlot(string? pokemonName, string level, string rarity, bool isMega)
    {
        if (string.IsNullOrWhiteSpace(pokemonName)) return null;
        var species = Services.Data.FindPokemon(pokemonName);
        var name = species?.Name ?? pokemonName;
        var image = isMega ? Services.Images.Resolve(name, mega: true) : Services.Images.Resolve(name);
        return new TeamCardSlot(name, Formatting.TitleCase(name), species?.PokemonTypes ?? new List<string>(), image, level, rarity, isMega);
    }

    /// <summary>Schedules a re-render of the preview (debounced).</summary>
    protected void RequestRender()
    {
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void RenderNow()
    {
        try
        {
            var slots = BuildSlots();
            Preview = TeamCardRenderer.Render(new TeamCardInput(TeamName, ThemeType, TrainerImagePath, slots));
            var count = slots.Count(s => s != null);
            PreviewStatus = $"{count} Pokémon · 1708 × 960 PNG" + (TrainerImagePath is null ? " · no trainer image" : "");
        }
        catch (Exception ex)
        {
            Preview = null;
            PreviewStatus = "Could not render the card: " + ex.Message;
        }
    }

    partial void OnTeamNameChanged(string value) { OnTeamFieldChanged(); RequestRender(); }
    partial void OnThemeTypeChanged(string value) { OnTeamFieldChanged(); RequestRender(); }
    partial void OnTrainerImagePathChanged(string? value) { OnTeamFieldChanged(); RequestRender(); }

    /// <summary>Hook for pages that persist the team.</summary>
    protected virtual void OnTeamFieldChanged() { }

    [RelayCommand]
    private async Task PickTrainerImage()
    {
        var path = await Services.Dialogs.OpenFileAsync("Choose a trainer image", DialogService.ImageFiles);
        if (path != null) TrainerImagePath = path;
    }

    [RelayCommand]
    private void ClearTrainerImage() => TrainerImagePath = null;

    [RelayCommand]
    private async Task ExportPng()
    {
        RenderNow();
        if (Preview is null) return;
        var name = ImageResolver.NormalizeBaseName(string.IsNullOrWhiteSpace(TeamName) ? TeamCardRenderer.DefaultTeamName : TeamName);
        var path = await Services.Dialogs.SaveFileAsync("Export team card", (name.Length == 0 ? "pokemon_team_card" : name) + ".png", DialogService.PngFiles);
        if (path is null) return;
        Preview.Save(path);
        Services.Dialogs.Notify("Team card exported.");
    }
}

// ─── Free team card (website "Pokemon Team Card") ───────────────────────────

public sealed partial class FreeSlotVm : ObservableObject
{
    private readonly Action _changed;
    public int Index { get; }
    public string Title => $"Slot {Index + 1}";
    public IReadOnlyList<string> PokemonNames { get; set; }

    [ObservableProperty] private string? _pokemonName;
    [ObservableProperty] private string _level = "";
    [ObservableProperty] private string _rarity = "normal";
    [ObservableProperty] private bool _isMega;

    public FreeSlotVm(int index, IReadOnlyList<string> names, Action changed)
    {
        Index = index;
        PokemonNames = names;
        _changed = changed;
    }

    partial void OnPokemonNameChanged(string? value) => _changed();
    partial void OnRarityChanged(string value) => _changed();
    partial void OnIsMegaChanged(bool value) => _changed();

    partial void OnLevelChanged(string value)
    {
        // Digits plus one decimal separator: levels can be fractional (partial XP), e.g. "12.5".
        var text = (value ?? "").Replace(',', '.');
        var dot = text.IndexOf('.');
        var whole = new string((dot < 0 ? text : text[..dot]).Where(char.IsDigit).ToArray());
        string normalized;
        if (whole.Length == 0 && dot < 0) normalized = "";
        else
        {
            var w = whole.Length == 0 ? 0 : Math.Clamp(int.Parse(whole[..Math.Min(4, whole.Length)]), 0, 150);
            var decimals = dot < 0 ? null : new string(text[(dot + 1)..].Where(char.IsDigit).Take(2).ToArray());
            normalized = w == 150 || decimals is null ? w.ToString() : $"{w}.{decimals}";
        }
        if (normalized != value) { Level = normalized; return; }
        _changed();
    }

    [RelayCommand]
    private void Clear()
    {
        PokemonName = null;
        Level = "";
        Rarity = "normal";
        IsMega = false;
    }
}

public sealed partial class TeamCardViewModel : TeamCardPageBase
{
    public override string Title => "Team Card";
    public override string Glyph => "🖼";
    public IReadOnlyList<FreeSlotVm> Slots { get; }
    private readonly Action _back;

    public TeamCardViewModel(AppServices services, Action back) : base(services)
    {
        _back = back;
        var names = services.Data.Pokemon.Select(p => p.Name).ToList();
        Slots = Enumerable.Range(0, TeamRecord.Size).Select(i => new FreeSlotVm(i, names, RequestRender)).ToList();
        services.GameDataChanged += () =>
        {
            var n = Services.Data.Pokemon.Select(p => p.Name).ToList();
            foreach (var s in Slots) s.PokemonNames = n;
        };
    }

    public override void OnActivated() => RequestRender();

    /// <summary>Back to the Team Manager.</summary>
    [RelayCommand]
    private void Back() => _back();

    protected override IReadOnlyList<TeamCardSlot?> BuildSlots() =>
        Slots.Select(s => Services.Data.FindPokemon(s.PokemonName) is null ? null : MakeSlot(s.PokemonName, s.Level, s.Rarity, s.IsMega)).ToList();
}

// ─── Team manager (teams of saved sheets) ───────────────────────────────────

public sealed partial class ManagedSlotVm : ObservableObject
{
    private readonly Action<ManagedSlotVm> _changed;
    public int Index { get; }
    public string Title => $"#{Index + 1}";

    [ObservableProperty] private IReadOnlyList<SheetRecord> _sheetOptions;
    [ObservableProperty] private SheetRecord? _selectedSheet;
    [ObservableProperty] private TeamSlot? _slot;
    [ObservableProperty] private bool _isMega;

    public bool HasSlot => Slot != null;
    public string PokemonName => Slot?.PokemonName ?? "";
    public string DisplayName => Slot is null ? "Empty slot"
        : !string.IsNullOrWhiteSpace(Slot.Nickname) && !Slot.Nickname.Equals(Slot.PokemonName, StringComparison.OrdinalIgnoreCase)
            ? $"{Slot.Nickname} ({Formatting.TitleCase(Slot.PokemonName)})" : Formatting.TitleCase(Slot.PokemonName);
    public string Meta => Slot is null ? "" :
        string.Join(" · ", new[] { string.IsNullOrWhiteSpace(Slot.Level) ? null : $"Lv. {Slot.Level}", Slot.Rarity is "normal" or "" ? null : Formatting.Capitalize(Slot.Rarity) }.Where(x => x != null));
    public bool IsMissingSheet => Slot?.CardId != null && SelectedSheet is null;
    /// <summary>The slot is linked to a sheet that still exists: it can be opened.</summary>
    public bool CanOpenSheet => SelectedSheet != null;
    public string OpenTip => CanOpenSheet ? $"Open the sheet of {DisplayName}" : "";
    public IRelayCommand OpenSheetCommand { get; }

    private bool _suppress;

    public ManagedSlotVm(int index, IReadOnlyList<SheetRecord> sheets, Action<ManagedSlotVm> changed, Action<string> openSheet)
    {
        Index = index;
        _sheetOptions = sheets;
        _changed = changed;
        OpenSheetCommand = new RelayCommand(() => { if (SelectedSheet is { } s) openSheet(s.Id); }, () => CanOpenSheet);
    }

    public void Load(TeamSlot? slot, IReadOnlyList<SheetRecord> sheets)
    {
        _suppress = true;
        SheetOptions = sheets;
        Slot = slot;
        IsMega = slot?.IsMega ?? false;
        SelectedSheet = slot?.CardId is null ? null : sheets.FirstOrDefault(s => s.Id == slot.CardId);
        _suppress = false;
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasSlot));
        OnPropertyChanged(nameof(PokemonName));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(IsMissingSheet));
        OnPropertyChanged(nameof(CanOpenSheet));
        OnPropertyChanged(nameof(OpenTip));
        OpenSheetCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSheetChanged(SheetRecord? value)
    {
        if (_suppress) return;
        if (value is null) return; // use "Clear" to empty a slot
        // Level and rarity always come from the sheet so the team stays in sync with it.
        Slot = new TeamSlot
        {
            CardId = value.Id,
            PokemonName = value.PokemonName,
            Nickname = value.Nickname,
            Level = value.State.Level > 0 ? SheetRules.FormatLevel(value.State.Level) : "",
            Rarity = (string.IsNullOrEmpty(value.State.Rarity) ? "normal" : value.State.Rarity).ToLowerInvariant(),
            IsMega = false,
        };
        _suppress = true;
        IsMega = false;
        _suppress = false;
        Notify();
        _changed(this);
    }

    partial void OnIsMegaChanged(bool value)
    {
        if (_suppress || Slot is null) return;
        Slot.IsMega = value;
        _changed(this);
    }

    [RelayCommand]
    private void Clear()
    {
        _suppress = true;
        SelectedSheet = null;
        Slot = null;
        IsMega = false;
        _suppress = false;
        Notify();
        _changed(this);
    }
}

public sealed partial class TeamManagerViewModel : TeamCardPageBase
{
    private readonly DispatcherTimer _autoSaveTimer;
    private TeamRecord? _record;
    private bool _loading;
    private bool _dirty;

    public override string Title => "Team Manager";
    public override string Glyph => "👥";

    [ObservableProperty] private IReadOnlyList<TeamRecord> _teams = Array.Empty<TeamRecord>();
    [ObservableProperty] private TeamRecord? _selectedTeam;
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _slotsSummary = "";

    public IReadOnlyList<ManagedSlotVm> Slots { get; }
    public bool IsSaved => _record != null;

    private readonly Action _openTeamCard;

    public TeamManagerViewModel(AppServices services, Action<string> openSheet, Action openTeamCard) : base(services)
    {
        _openTeamCard = openTeamCard;
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _autoSaveTimer.Tick += (_, _) => { _autoSaveTimer.Stop(); if (_dirty) SaveInternal(silent: true); };
        var sheets = services.Library.Sheets.List();
        Slots = Enumerable.Range(0, TeamRecord.Size).Select(i => new ManagedSlotVm(i, sheets, _ => { MarkDirty(); RequestRender(); UpdateSummary(); },
            id => { Flush(); openSheet(id); })).ToList();
        services.LibraryChanged += Reload;
        Reload();
    }

    public override void OnActivated()
    {
        Reload();
        RequestRender();
    }

    public override void OnDeactivated() => Flush();

    /// <summary>Opens the Team Card: a card for any team (rivals, NPCs…), nothing saved.</summary>
    [RelayCommand]
    private void OpenTeamCard()
    {
        Flush();
        _openTeamCard();
    }

    private IReadOnlyList<SheetRecord> SortedSheets() =>
        Services.Library.Sheets.List().OrderBy(s => s.Nickname, StringComparer.OrdinalIgnoreCase).ToList();

    private void Reload()
    {
        var id = _record?.Id;
        _loading = true;
        Teams = Services.Library.Teams.List().OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        SelectedTeam = Teams.FirstOrDefault(t => t.Id == id);
        _loading = false;
        if (_record != null && !_dirty)
        {
            // Pick up changes made elsewhere (e.g. "Add to team" from the Sheets page).
            var fresh = Services.Library.Teams.Get(_record.Id);
            if (fresh != null) Open(fresh);
        }
        else
        {
            var sheets = SortedSheets();
            foreach (var s in Slots) s.SheetOptions = sheets;
        }
    }

    partial void OnSelectedTeamChanged(TeamRecord? value)
    {
        if (_loading || value is null || value.Id == _record?.Id) return;
        Flush();
        var team = Services.Library.Teams.Get(value.Id);
        if (team != null) Open(team);
    }

    private void Open(TeamRecord team)
    {
        team.Normalize();
        _record = team;
        _loading = true;
        TeamName = team.Name;
        ThemeType = TeamCardRenderer.ThemeTypes.Contains(team.SelectedType) ? team.SelectedType : "Fire";
        TrainerImagePath = team.TrainerImagePath is { } p && File.Exists(p) ? p : null;
        var sheets = SortedSheets();
        for (int i = 0; i < TeamRecord.Size; i++)
        {
            var slot = team.Slots[i];
            // Refresh level / rarity from the linked sheet when it still exists.
            if (slot?.CardId != null && sheets.FirstOrDefault(s => s.Id == slot.CardId) is { } sheet)
            {
                slot.PokemonName = sheet.PokemonName;
                slot.Nickname = sheet.Nickname;
                if (sheet.State.Level > 0) slot.Level = SheetRules.FormatLevel(sheet.State.Level);
                if (!string.IsNullOrEmpty(sheet.State.Rarity)) slot.Rarity = sheet.State.Rarity.ToLowerInvariant();
            }
            Slots[i].Load(slot, sheets);
        }
        _loading = false;
        _dirty = false;
        IsEditing = true;
        SaveStatus = $"Saved {team.UpdatedAt.ToLocalTime():g}";
        OnPropertyChanged(nameof(IsSaved));
        UpdateSummary();
        RequestRender();
    }

    private void UpdateSummary()
    {
        var sheets = Services.Library.Sheets.List().Count;
        SlotsSummary = $"{Slots.Count(s => s.HasSlot)} / {TeamRecord.Size} Pokémon — " +
                       (sheets == 0 ? "no saved sheets yet: create some in the Sheets page." : $"{sheets} sheet(s) available.");
    }

    protected override IReadOnlyList<TeamCardSlot?> BuildSlots() =>
        Slots.Select(s => s.Slot is null ? null : MakeSlot(s.Slot.PokemonName, s.Slot.Level, s.Slot.Rarity, s.Slot.IsMega)).ToList();

    protected override void OnTeamFieldChanged() => MarkDirty();

    private void MarkDirty()
    {
        if (_loading || !IsEditing) return;
        _dirty = true;
        if (string.IsNullOrWhiteSpace(TeamName))
        {
            SaveStatus = "Name the team to save it";
            return;
        }
        SaveStatus = "Unsaved changes…";
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void Flush()
    {
        _autoSaveTimer.Stop();
        if (_dirty) SaveInternal(silent: true);
    }

    private void SaveInternal(bool silent)
    {
        if (string.IsNullOrWhiteSpace(TeamName)) return;
        var isNew = _record is null;
        var team = _record ?? new TeamRecord();
        team.Name = TeamName.Trim();
        team.SelectedType = ThemeType;
        team.TrainerImagePath = TrainerImagePath;
        team.Slots = Slots.Select(s => s.Slot).ToList();
        Services.Library.Teams.Save(team);
        _record = team;
        _dirty = false;
        SaveStatus = $"Saved {DateTime.Now:t}";
        OnPropertyChanged(nameof(IsSaved));
        if (isNew || !silent)
        {
            _loading = true;
            Teams = Services.Library.Teams.List().OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
            SelectedTeam = Teams.FirstOrDefault(t => t.Id == team.Id);
            _loading = false;
        }
        if (!silent) Services.Dialogs.Notify($"Team \"{team.Name}\" saved.");
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(TeamName))
        {
            Services.Dialogs.Notify("Give the team a name first.", isError: true);
            return;
        }
        SaveInternal(silent: false);
    }

    [RelayCommand]
    private void NewTeam()
    {
        Flush();
        _record = null;
        _loading = true;
        SelectedTeam = null;
        TeamName = "";
        ThemeType = "Fire";
        TrainerImagePath = null;
        var sheets = SortedSheets();
        foreach (var s in Slots) s.Load(null, sheets);
        _loading = false;
        _dirty = false;
        IsEditing = true;
        SaveStatus = "New team — name it to save it.";
        OnPropertyChanged(nameof(IsSaved));
        UpdateSummary();
        RequestRender();
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (_record is null) return;
        if (!await Services.Dialogs.ConfirmAsync("Delete team", $"Delete the team \"{_record.Name}\"? The sheets themselves are kept.")) return;
        _autoSaveTimer.Stop();
        Services.Library.Teams.Delete(_record.Id);
        _record = null;
        _dirty = false;
        IsEditing = false;
        Services.NotifyLibraryChanged();
    }
}
