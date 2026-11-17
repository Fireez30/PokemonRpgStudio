using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Json;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.ViewModels;

public sealed partial class ZonePokemonVm : ObservableObject
{
    public string Name { get; }
    public string Label => Formatting.TitleCase(Name);
    public IRelayCommand RemoveCommand { get; }
    [ObservableProperty] private bool _isRolled;

    public ZonePokemonVm(string name, Action<ZonePokemonVm> remove)
    {
        Name = name;
        RemoveCommand = new RelayCommand(() => remove(this));
    }
}

public sealed partial class ZoneSectionVm : ObservableObject
{
    private readonly Action _changed;
    private readonly Func<string, bool> _isKnown;
    public ZoneTier Tier { get; }
    public string Label { get; }
    public IReadOnlyList<string> PokemonNames { get; set; }

    [ObservableProperty] private IReadOnlyList<ZonePokemonVm> _pokemon = Array.Empty<ZonePokemonVm>();
    [ObservableProperty] private string? _pendingName;
    [ObservableProperty] private string _rolledText = "";

    public bool CanRoll => Pokemon.Count >= 2;
    public string CountText => $"{Pokemon.Count} Pokémon";

    public ZoneSectionVm(ZoneTier tier, string label, IReadOnlyList<string> names, Func<string, bool> isKnown, Action changed)
    {
        Tier = tier;
        Label = label;
        PokemonNames = names;
        _isKnown = isKnown;
        _changed = changed;
    }

    partial void OnPokemonChanged(IReadOnlyList<ZonePokemonVm> value)
    {
        OnPropertyChanged(nameof(CanRoll));
        OnPropertyChanged(nameof(CountText));
        RollCommand.NotifyCanExecuteChanged();
    }

    public void Load(IEnumerable<string> names)
    {
        Pokemon = names.Select(n => new ZonePokemonVm(n, Remove)).ToList();
        RolledText = "";
    }

    public List<string> Names() => Pokemon.Select(p => p.Name).ToList();

    [RelayCommand]
    private void Add()
    {
        var name = PendingName?.Trim();
        if (string.IsNullOrEmpty(name) || !_isKnown(name)) return;
        if (Pokemon.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { PendingName = null; return; }
        Pokemon = Pokemon.Append(new ZonePokemonVm(name, Remove)).ToList();
        PendingName = null;
        _changed();
    }

    private void Remove(ZonePokemonVm p)
    {
        Pokemon = Pokemon.Where(x => x != p).ToList();
        RolledText = "";
        _changed();
    }

    [RelayCommand(CanExecute = nameof(CanRoll))]
    private void Roll()
    {
        if (Pokemon.Count == 0) return;
        var pick = Pokemon[Random.Shared.Next(Pokemon.Count)];
        foreach (var p in Pokemon) p.IsRolled = p == pick;
        RolledText = $"Rolled: {pick.Label}";
    }
}

/// <summary>Zone builder and encounter roller (website "Pokemon Encounter Generator").</summary>
public sealed partial class EncounterViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _autoSaveTimer;
    private ZoneRecord? _record;
    private bool _loading;
    private bool _dirty;
    /// <summary>Folder given to the zone being created (not saved yet).</summary>
    private string _newZoneFolder = "";
    private List<ZoneRecord> _allZones = new();

    public override string Title => "Encounters";
    public override string Glyph => "🌿";

    /// <summary>Zones shown in the list (those of the folder picked).</summary>
    [ObservableProperty] private IReadOnlyList<ZoneRecord> _zones = Array.Empty<ZoneRecord>();
    [ObservableProperty] private ZoneRecord? _selectedZone;
    [ObservableProperty] private string _zoneName = "";
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private bool _isEditorExpanded = true;
    [ObservableProperty] private int _encounterValue;
    [ObservableProperty] private string _encounterLabel = "0 encounter";
    [ObservableProperty] private string _encounterDetail = "No encounter";

    // ── Folders ──────────────────────────────────────────────────────────────
    [ObservableProperty] private IReadOnlyList<FolderOption> _folderOptions = Array.Empty<FolderOption>();
    [ObservableProperty] private FolderOption? _selectedFolder;
    /// <summary>Destinations of "Move to folder" for the zone being edited.</summary>
    [ObservableProperty] private IReadOnlyList<FolderOption> _moveFolderOptions = Array.Empty<FolderOption>();
    [ObservableProperty] private FolderOption? _zoneFolder;
    private bool _loadingFolders;
    /// <summary>The folder open in the list (null = all zones / no folder): new and imported zones go there.</summary>
    public string? CurrentFolder => SelectedFolder is { IsFolder: true } f ? f.Path : null;
    public bool HasFolderSelected => SelectedFolder?.IsFolder == true;

    public IReadOnlyList<ZoneSectionVm> Sections { get; }
    public bool IsSaved => _record != null;

    public EncounterViewModel(AppServices services)
    {
        _services = services;
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _autoSaveTimer.Tick += (_, _) => { _autoSaveTimer.Stop(); if (_dirty) SaveInternal(silent: true); };
        var names = services.Data.Pokemon.Select(p => p.Name).ToList();
        bool Known(string n) => _services.Data.FindPokemon(n) != null;
        Sections = new[]
        {
            new ZoneSectionVm(ZoneTier.Common, "Common", names, Known, MarkDirty),
            new ZoneSectionVm(ZoneTier.Uncommon, "Uncommon", names, Known, MarkDirty),
            new ZoneSectionVm(ZoneTier.Rare, "Rare", names, Known, MarkDirty),
            new ZoneSectionVm(ZoneTier.SuperRare, "Super Rare", names, Known, MarkDirty),
        };
        services.LibraryChanged += ReloadZones;
        services.GameDataChanged += () =>
        {
            var n = _services.Data.Pokemon.Select(p => p.Name).ToList();
            foreach (var s in Sections) s.PokemonNames = n;
        };
        ReloadZones();
    }

    public override void OnActivated() => ReloadZones();

    private void ReloadZones()
    {
        _allZones = _services.Library.Zones.List().OrderBy(z => z.Name, StringComparer.OrdinalIgnoreCase).ToList();
        // The folder may have been changed on disk (folder renamed / deleted): keep the open zone in sync.
        if (_record != null && _allZones.FirstOrDefault(z => z.Id == _record.Id) is { } onDisk) _record.Folder = onDisk.Folder;
        ReloadFolders();
        ApplyFolderFilter();
    }

    private void ReloadFolders()
    {
        var wanted = SelectedFolder is null && FolderOptions.Count == 0 ? _services.Settings.ZoneFolderFilter : SelectedFolder?.Path;
        _loadingFolders = true;
        FolderOptions = FolderOption.ForFilter(_allZones, _services.Settings.ZoneFolders, "All zones");
        SelectedFolder = FolderOptions.FirstOrDefault(o => string.Equals(o.Path, wanted, StringComparison.OrdinalIgnoreCase)) ?? FolderOptions[0];
        MoveFolderOptions = FolderOption.ForMove(_allZones, _services.Settings.ZoneFolders);
        ZoneFolder = FindMoveOption(_record?.Folder ?? _newZoneFolder);
        _loadingFolders = false;
        OnPropertyChanged(nameof(CurrentFolder));
        OnPropertyChanged(nameof(HasFolderSelected));
    }

    private void ApplyFolderFilter()
    {
        var folder = SelectedFolder?.Path;
        Zones = folder is null ? _allZones : _allZones.Where(z => SheetFolders.IsInside(SheetFolders.Normalize(z.Folder), folder)).ToList();
        _loading = true;
        SelectedZone = Zones.FirstOrDefault(z => z.Id == _record?.Id);
        _loading = false;
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
        _services.Settings.ZoneFolderFilter = value.Path;
        _services.SaveSettings();
        ApplyFolderFilter();
    }

    /// <summary>Moves the zone being edited to the folder picked next to its name.</summary>
    partial void OnZoneFolderChanged(FolderOption? value)
    {
        if (_loadingFolders || value?.Path is null) return;
        if (_record is null)
        {
            // Not saved yet: the folder is used when the zone is saved.
            _newZoneFolder = value.Path;
            return;
        }
        var zone = _services.Library.Zones.Get(_record.Id);
        if (zone is null) { _record.Folder = value.Path; return; }
        if (string.Equals(SheetFolders.Normalize(zone.Folder), value.Path, StringComparison.OrdinalIgnoreCase)) return;
        zone.Folder = value.Path;
        _services.Library.Zones.Save(zone, touch: false);
        _record.Folder = value.Path;
        _services.NotifyLibraryChanged();
        var name = string.IsNullOrWhiteSpace(ZoneName) ? zone.Name : ZoneName.Trim();
        _services.Dialogs.Notify(value.Path.Length == 0 ? $"\"{name}\" removed from its folder." : $"\"{name}\" moved to {value.Path}.");
    }

    /// <summary>Forgets the folder selection so the next reload picks the folder saved in the settings.</summary>
    private void ResetFolderSelection()
    {
        _loadingFolders = true;
        SelectedFolder = null;
        FolderOptions = Array.Empty<FolderOption>();
        _loadingFolders = false;
    }

    /// <summary>New folder, inside the folder currently open.</summary>
    [RelayCommand]
    private async Task NewFolder()
    {
        var parent = CurrentFolder ?? "";
        var name = await _services.Dialogs.PromptAsync("New folder",
            parent.Length == 0 ? "Name of the new folder (use / for subfolders, e.g. Region/Forest):" : $"Name of the new folder inside \"{parent}\":",
            confirmText: "Create", watermark: "Folder name");
        if (name is null) return;
        var path = SheetFolders.Create(_services.Settings.ZoneFolders, parent, name);
        if (path.Length == 0) return;
        _services.Settings.ZoneFolderFilter = path;
        _services.SaveSettings();
        ResetFolderSelection();
        ReloadZones();
    }

    [RelayCommand]
    private async Task RenameFolder()
    {
        if (CurrentFolder is not { } folder) return;
        var name = await _services.Dialogs.PromptAsync("Rename folder", $"New name for \"{folder}\":", SheetFolders.Name(folder), "Rename");
        if (name is null) return;
        try
        {
            Flush();
            var target = SheetFolders.RenameFolder(_services.Library.Zones, _services.Settings.ZoneFolders, folder, name);
            _services.Settings.ZoneFolderFilter = target;
            _services.SaveSettings();
            ResetFolderSelection();
            _services.NotifyLibraryChanged();
        }
        catch (InvalidOperationException ex)
        {
            _services.Dialogs.Notify(ex.Message, isError: true);
        }
    }

    /// <summary>Deletes the open folder; its zones and subfolders go to the parent folder.</summary>
    [RelayCommand]
    private async Task DeleteFolder()
    {
        if (CurrentFolder is not { } folder) return;
        var parent = SheetFolders.Parent(folder);
        var count = _allZones.Count(z => SheetFolders.IsInside(SheetFolders.Normalize(z.Folder), folder));
        var where = parent.Length == 0 ? "out of any folder" : $"to \"{parent}\"";
        if (!await _services.Dialogs.ConfirmAsync("Delete folder",
                $"Delete the folder \"{folder}\"? No zone is deleted: its {count} zone(s) and subfolders are moved {where}.")) return;
        Flush();
        SheetFolders.Delete(_services.Library.Zones, _services.Settings.ZoneFolders, folder);
        _services.Settings.ZoneFolderFilter = parent.Length == 0 ? null : parent;
        _services.SaveSettings();
        ResetFolderSelection();
        _services.NotifyLibraryChanged();
        _services.Dialogs.Notify("Folder deleted.");
    }

    partial void OnSelectedZoneChanged(ZoneRecord? value)
    {
        if (_loading || value is null || value.Id == _record?.Id) return;
        Flush();
        var zone = _services.Library.Zones.Get(value.Id);
        if (zone != null) Open(zone);
    }

    private void Open(ZoneRecord zone)
    {
        _record = zone;
        _loading = true;
        ZoneName = zone.Name;
        foreach (var s in Sections)
            s.Load(zone.Sections.Get(s.Tier).Where(n => _services.Data.FindPokemon(n) != null).Distinct(StringComparer.OrdinalIgnoreCase));
        _loading = false;
        _dirty = false;
        SaveStatus = $"Saved {zone.UpdatedAt.ToLocalTime():g}";
        OnPropertyChanged(nameof(IsSaved));
        ShowZoneFolder(zone.Folder);
    }

    private void ShowZoneFolder(string folder)
    {
        _loadingFolders = true;
        ZoneFolder = FindMoveOption(folder);
        _loadingFolders = false;
    }

    partial void OnZoneNameChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        if (string.IsNullOrWhiteSpace(ZoneName))
        {
            SaveStatus = "Name the zone to save it";
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

    private ZoneSections CurrentSections()
    {
        var s = new ZoneSections();
        foreach (var section in Sections) s.Get(section.Tier).AddRange(section.Names());
        return s;
    }

    private void SaveInternal(bool silent)
    {
        if (string.IsNullOrWhiteSpace(ZoneName)) return;
        var isNew = _record is null;
        var zone = _record ?? new ZoneRecord { Folder = _newZoneFolder };
        // The folder is changed from the folder picker / folder commands: keep the latest one on disk.
        if (!isNew && _services.Library.Zones.Get(zone.Id) is { } onDisk) zone.Folder = onDisk.Folder;
        zone.Name = ZoneName.Trim();
        zone.Sections = CurrentSections();
        _services.Library.Zones.Save(zone);
        _record = zone;
        _dirty = false;
        SaveStatus = $"Saved {DateTime.Now:t}";
        OnPropertyChanged(nameof(IsSaved));
        if (isNew || !silent) ReloadZones();
        if (!silent) _services.Dialogs.Notify($"Zone \"{zone.Name}\" saved.");
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(ZoneName))
        {
            _services.Dialogs.Notify("Give the zone a name first.", isError: true);
            return;
        }
        SaveInternal(silent: false);
    }

    [RelayCommand]
    private void NewZone()
    {
        Flush();
        _record = null;
        _loading = true;
        ZoneName = "";
        SelectedZone = null;
        foreach (var s in Sections) s.Load(Array.Empty<string>());
        _loading = false;
        _dirty = false;
        SaveStatus = "New zone";
        OnPropertyChanged(nameof(IsSaved));
        // New zones go into the folder open in the list.
        _newZoneFolder = CurrentFolder ?? "";
        ShowZoneFolder(_newZoneFolder);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (_record is null) return;
        if (!await _services.Dialogs.ConfirmAsync("Delete zone", $"Delete the zone \"{_record.Name}\"? This cannot be undone.")) return;
        _autoSaveTimer.Stop();
        _services.Library.Zones.Delete(_record.Id);
        _record = null;
        NewZone();
        ReloadZones();
    }

    [RelayCommand]
    private async Task ExportJson()
    {
        var name = string.IsNullOrWhiteSpace(ZoneName) ? "zone" : ZoneName.Trim();
        // Same shape as the website's "Save JSON" button.
        var payload = new { zoneName = ZoneName, sections = CurrentSections() };
        if (!await _services.Dialogs.SaveFileAsync("Export zone", name + ".json", DialogService.JsonFiles,
                path => File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonDefaults.Options)))) return;
        _services.Dialogs.Notify("Zone exported.");
    }

    [RelayCommand]
    private async Task ImportJson()
    {
        var path = await _services.Dialogs.OpenFileAsync("Import a zone (.json)", DialogService.JsonFiles);
        if (path is null) return;
        try
        {
            var zone = JsonSerializer.Deserialize<ZoneRecord>(await File.ReadAllTextAsync(path), JsonDefaults.Options)
                       ?? throw new InvalidDataException("Empty file.");
            Flush();
            zone.Id = Guid.NewGuid().ToString();
            zone.Name = string.IsNullOrWhiteSpace(zone.Name) ? zone.ZoneName ?? Path.GetFileNameWithoutExtension(path) : zone.Name;
            zone.ZoneName = null;
            Open(zone);
            _record = null; // not saved yet: will be created on save
            // Imported zones go into the folder open in the list.
            _newZoneFolder = CurrentFolder ?? "";
            ShowZoneFolder(_newZoneFolder);
            MarkDirty();
            _services.Dialogs.Notify("Zone loaded. Unknown Pokémon were skipped.");
        }
        catch (Exception ex)
        {
            _services.Dialogs.Notify("Invalid zone file: " + ex.Message, isError: true);
        }
    }

    partial void OnEncounterValueChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 20);
        if (clamped != value) EncounterValue = clamped;
    }

    [RelayCommand]
    private void RollEncounters()
    {
        var r = Dice.ResolveEncounterRule(EncounterValue);
        EncounterLabel = r.Label;
        EncounterDetail = r.Detail;
    }

    [RelayCommand]
    private void RollValue()
    {
        EncounterValue = Dice.Roll(20);
        RollEncounters();
    }
}
