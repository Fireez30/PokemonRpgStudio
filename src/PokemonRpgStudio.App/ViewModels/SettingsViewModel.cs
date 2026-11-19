using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Rules;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.ViewModels;

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly AppServices _services;
    public override string Title => "Settings";
    public override string Glyph => "⚙";

    public IReadOnlyList<string> Themes { get; } = new[] { "Dark", "Light", "System" };

    [ObservableProperty] private string _theme;
    [ObservableProperty] private string _imageBaseUrl;
    [ObservableProperty] private string _dataStatus = "";

    public string UserFolder => _services.Library.Paths.Root;
    public string ImagesFolder => _services.ImagesDir;
    [ObservableProperty] private string _spritesInfo = "";
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>The shops (exported with their pictures by the Shops page's own command).</summary>
    public ShopsViewModel Shops { get; }

    public SettingsViewModel(AppServices services, ShopsViewModel shops)
    {
        _services = services;
        Shops = shops;
        _theme = services.Settings.Theme;
        _imageBaseUrl = services.Settings.ImageBaseUrl;
        RefreshStatus();
    }

    public override void OnActivated() => RefreshStatus();

    [ObservableProperty] private IReadOnlyList<DataFileRow> _dataFiles = Array.Empty<DataFileRow>();
    public string UserDataFolder => _services.DataDir;
    public bool HasDefaultData => AppServices.HasDefaultData;

    private void RefreshStatus()
    {
        var d = _services.Data;
        DataStatus = $"{d.Pokemon.Count} Pokémon, {d.Moves.Count} moves, {d.Abilities.Count} abilities loaded from your data folder.";
        DataFiles = GameData.FileNames.Append(TypeChart.FileName).Select(name =>
        {
            var path = d.FilePath(name);
            var info = new FileInfo(path);
            return new DataFileRow(name, path, !info.Exists ? (name == TypeChart.FileName ? "missing: built-in chart used" : "missing")
                : info.Length < 1024 * 1024 ? $"{info.Length / 1024.0:0.0} KB · {info.LastWriteTime:g}"
                : $"{info.Length / 1024.0 / 1024.0:0.0} MB · {info.LastWriteTime:g}");
        }).ToList();
        OnPropertyChanged(nameof(UserDataFolder));
        var count = ImageResolver.CountImages(_services.ImagesDir);
        SpritesInfo = count == 0 ? "No sprites: Pokémon are shown without images." : $"{count} sprites";
    }

    [RelayCommand] private void OpenDataFolder() => _services.Dialogs.OpenInFileManager(UserDataFolder);

    [RelayCommand]
    private void OpenSpritesFolder()
    {
        Directory.CreateDirectory(_services.ImagesDir);
        _services.Dialogs.OpenInFileManager(_services.ImagesDir);
    }

    /// <summary>Adds or replaces sprites: the file name decides the Pokémon (e.g. charizard.png, charizard_mega.png).</summary>
    [RelayCommand]
    private async Task InstallSprites()
    {
        var files = await _services.Dialogs.OpenFilesAsync("Add or replace sprites (file name = Pokémon, e.g. charizard.png)", true, DialogService.ImageFiles);
        if (files.Count == 0) return;
        var errors = new List<string>();
        var installed = _services.InstallSprites(files, errors);
        RefreshStatus();
        if (installed.Count > 0) _services.Dialogs.Notify($"Installed {installed.Count} sprite(s): {string.Join(", ", installed.Take(5))}{(installed.Count > 5 ? "…" : "")}");
        if (errors.Count > 0) _services.Dialogs.Notify(string.Join("\n", errors.Take(5)), isError: true);
    }

    /// <summary>Copies updated pokemon.json / moves.json / abilities.json into the user data folder and reloads.</summary>
    [RelayCommand]
    private async Task InstallDataFiles()
    {
        var files = await _services.Dialogs.OpenFilesAsync("Pick updated pokemon.json, moves.json, abilities.json and/or type_chart.json", true, DialogService.JsonFiles);
        if (files.Count == 0) return;
        var done = new List<string>();
        foreach (var f in files)
        {
            try { done.Add(GameData.InstallDataFile(f, UserDataFolder)); }
            catch (Exception ex) { _services.Dialogs.Notify($"{Path.GetFileName(f)}: {ex.Message}", isError: true); }
        }
        if (done.Count == 0) return;
        ReloadData();
        _services.Dialogs.Notify("Installed " + string.Join(", ", done) + ".");
    }

    /// <summary>Reads the data files again (after replacing them by hand, for example).</summary>
    [RelayCommand]
    private void ReloadData()
    {
        try
        {
            _services.ReloadGameData();
            _services.RefreshSprites();
            RefreshStatus();
            _services.Dialogs.Notify(DataStatus);
        }
        catch (Exception ex)
        {
            _services.Dialogs.Notify("Could not load the data: " + ex.Message, isError: true);
        }
    }

    partial void OnThemeChanged(string value)
    {
        App.ApplyTheme(value);
        _services.Settings.Theme = value;
        _services.SaveSettings();
    }

    partial void OnImageBaseUrlChanged(string value)
    {
        _services.Settings.ImageBaseUrl = string.IsNullOrWhiteSpace(value) ? Core.Export.SheetExporter.DefaultImageBaseUrl : value.Trim();
        _services.SaveSettings();
    }

    // ---- Export all ----

    [RelayCommand] private Task ExportAllSheets() => ExportAllAsync(_services.Library.Sheets, "sheets", "sheet(s)");
    [RelayCommand] private Task ExportAllTeams() => ExportAllAsync(_services.Library.Teams, "teams", "team(s)");
    [RelayCommand] private Task ExportAllZones() => ExportAllAsync(_services.Library.Zones, "zones", "zone(s)");

    private async Task ExportAllAsync<T>(JsonRecordStore<T> store, string fileName, string what) where T : class, Core.Models.IStoredRecord
    {
        try
        {
            var count = store.List().Count;
            if (count == 0)
            {
                _services.Dialogs.Notify($"There is no {what.Replace("(s)", "")} to export.", isError: true);
                return;
            }
            var json = LibraryExporter.ExportAll(store);
            if (!await _services.Dialogs.SaveFileAsync($"Export all {fileName}", $"all-{fileName}.json", DialogService.JsonFiles,
                    path => File.WriteAllTextAsync(path, json))) return;
            _services.Dialogs.Notify($"Exported {count} {what}.");
        }
        catch (Exception ex)
        {
            _services.Dialogs.Notify("Could not export: " + ex.Message, isError: true);
        }
    }

    [RelayCommand] private void OpenUserFolder() => _services.Dialogs.OpenInFileManager(UserFolder);

    [RelayCommand]
    private async Task ImportWebsiteFolder()
    {
        var folder = await _services.Dialogs.OpenFolderAsync("Select the website 'server' folder (or a folder of JSON files)");
        if (folder is null) return;
        Import(folder);
    }

    [RelayCommand]
    private async Task ImportWebsiteFiles()
    {
        var files = await _services.Dialogs.OpenFilesAsync("Import sheets / teams / zones (.json) or website sheets (.md)", true,
            DialogService.SheetFiles, DialogService.JsonFiles, DialogService.MarkdownFiles);
        foreach (var f in files) Import(f);
    }

    private void Import(string path)
    {
        try
        {
            var report = WebsiteImporter.ImportPath(path, _services.Library, _services.Data);
            _services.NotifyLibraryChanged();
            _services.Dialogs.Notify(report.ToString());
            if (report.AllWarnings.Count > 0)
                _services.Dialogs.Notify(string.Join("\n", report.AllWarnings.Take(6)) + (report.AllWarnings.Count > 6 ? $"\n… and {report.AllWarnings.Count - 6} more" : ""),
                    title: "Import notes");
        }
        catch (Exception ex)
        {
            _services.Dialogs.Notify("Import failed: " + ex.Message, isError: true);
        }
    }

    /// <summary>Overwrites the data folder with the default files shipped next to the application.</summary>
    [RelayCommand]
    private async Task ResetPokedexEdits()
    {
        if (!AppServices.HasDefaultData)
        {
            _services.Dialogs.Notify("No default data was found next to the application.", isError: true);
            return;
        }
        if (!await _services.Dialogs.ConfirmAsync("Restore default data",
                "Replace pokemon.json, moves.json, abilities.json, type_chart.json and the sprites in your data folder with the default files shipped with the application? Installed updates, replaced sprites and Pokédex edits will be lost (sprites you added are kept).", "Restore")) return;
        try
        {
            var copied = _services.RestoreDefaultData();
            RefreshStatus();
            _services.Dialogs.Notify("Restored " + string.Join(", ", copied) + ".");
        }
        catch (Exception ex)
        {
            _services.Dialogs.Notify("Could not restore the data: " + ex.Message, isError: true);
        }
    }
}

public sealed record DataFileRow(string Name, string Path, string Info);
