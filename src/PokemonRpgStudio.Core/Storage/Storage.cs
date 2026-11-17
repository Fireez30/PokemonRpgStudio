using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PokemonRpgStudio.Core.Json;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.Core.Storage;

/// <summary>Locations of the user's files.</summary>
public sealed class AppPaths
{
    public const string HomeEnvVar = "POKEMON_RPG_STUDIO_HOME";

    public string Root { get; }
    public string Sheets => Path.Combine(Root, "sheets");
    public string Teams => Path.Combine(Root, "teams");
    public string Zones => Path.Combine(Root, "zones");
    /// <summary>The whiteboard (<c>board.json</c>) and the images placed on it.</summary>
    public string Whiteboard => Path.Combine(Root, "whiteboard");
    /// <summary>The shops (<c>shops.json</c>) and the pictures of their items.</summary>
    public string Shops => Path.Combine(Root, "shops");
    public string Data => Path.Combine(Root, "data");
    public string Images => Path.Combine(Data, "images");
    public string SettingsFile => Path.Combine(Root, "settings.json");

    public AppPaths(string root)
    {
        Root = root;
        foreach (var dir in new[] { Root, Sheets, Teams, Zones, Data }) Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// <c>$POKEMON_RPG_STUDIO_HOME</c> when set, otherwise <c>~/.local/share/PokemonRpgStudio</c> on Linux
    /// and <c>%LOCALAPPDATA%\PokemonRpgStudio</c> on Windows.
    /// </summary>
    public static AppPaths Default()
    {
        var env = Environment.GetEnvironmentVariable(HomeEnvVar);
        if (!string.IsNullOrWhiteSpace(env)) return new AppPaths(env);
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return new AppPaths(Path.Combine(baseDir, "PokemonRpgStudio"));
    }
}

/// <summary>Stores records as <c>&lt;id&gt;.json</c> files in a folder (like the website server).</summary>
public sealed class JsonRecordStore<T> where T : class, IStoredRecord
{
    private static readonly Regex ValidId = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.Compiled);

    public string Directory { get; }

    public JsonRecordStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public static bool IsValidId(string? id) => id != null && ValidId.IsMatch(id);

    private string FileFor(string id)
    {
        if (!IsValidId(id)) throw new ArgumentException($"Invalid id '{id}'.", nameof(id));
        return Path.Combine(Directory, id + ".json");
    }

    public List<T> List()
    {
        var result = new List<T>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var record = TryRead(file);
            if (record != null) result.Add(record);
        }
        return result.OrderBy(r => r.CreatedAt).ToList();
    }

    public T? Get(string id)
    {
        var file = FileFor(id);
        return File.Exists(file) ? TryRead(file) : null;
    }

    public static T? TryRead(string file)
    {
        try
        {
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<T>(json, JsonDefaults.Options);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public T Save(T record, bool touch = true)
    {
        if (!IsValidId(record.Id)) record.Id = Guid.NewGuid().ToString();
        if (touch) record.UpdatedAt = DateTimeOffset.UtcNow;
        var file = FileFor(record.Id);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(record, JsonDefaults.Options));
        File.Move(tmp, file, overwrite: true);
        return record;
    }

    public bool Delete(string id)
    {
        var file = FileFor(id);
        if (!File.Exists(file)) return false;
        File.Delete(file);
        return true;
    }

    public bool Exists(string id) => IsValidId(id) && File.Exists(FileFor(id));
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string ImageBaseUrl { get; set; } = Export.SheetExporter.DefaultImageBaseUrl;
    public int ShinyNumber { get; set; } = 1;
    public int ShinyDexnav { get; set; }
    public CardRollSettings Cards { get; set; } = new();
    public string? LastWebsiteImportFolder { get; set; }
    public SheetSortKey SheetSort { get; set; } = SheetSortKey.Nickname;
    public bool SheetSortDescending { get; set; }
    public Data.MoveSortKey MoveSort { get; set; } = Data.MoveSortKey.Name;
    public bool MoveSortDescending { get; set; }
    public Data.AbilitySortKey AbilitySort { get; set; } = Data.AbilitySortKey.Name;
    public bool AbilitySortDescending { get; set; }
    /// <summary>Sheet folders created by the user (kept even when empty).</summary>
    public List<string> SheetFolders { get; set; } = new();
    /// <summary>Folder shown on the Sheets page: null = all sheets, "" = sheets without a folder.</summary>
    public string? SheetFolderFilter { get; set; }
    /// <summary>Zone folders created by the user (kept even when empty).</summary>
    public List<string> ZoneFolders { get; set; } = new();
    /// <summary>Folder shown in the zone list: null = all zones, "" = zones without a folder.</summary>
    public string? ZoneFolderFilter { get; set; }
    /// <summary>View shown by each sidebar section (e.g. "sheets" → "list" or "database").</summary>
    public Dictionary<string, string> SectionViews { get; set; } = new();

    public static AppSettings Load(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), JsonDefaults.Options) ?? new AppSettings();
                // Older versions defaulted to plain HTTP: move the untouched default to HTTPS.
                if (settings.ImageBaseUrl.TrimEnd('/') == "http://faeresdev.site")
                    settings.ImageBaseUrl = Export.SheetExporter.DefaultImageBaseUrl;
                return settings;
            }
        }
        catch (Exception)
        {
            // Corrupted settings: fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save(string file) => File.WriteAllText(file, JsonSerializer.Serialize(this, JsonDefaults.Options));
}

/// <summary>All the user's stores.</summary>
public sealed class UserLibrary
{
    public AppPaths Paths { get; }
    public JsonRecordStore<SheetRecord> Sheets { get; }
    public JsonRecordStore<TeamRecord> Teams { get; }
    public JsonRecordStore<ZoneRecord> Zones { get; }

    public UserLibrary(AppPaths paths)
    {
        Paths = paths;
        Sheets = new JsonRecordStore<SheetRecord>(paths.Sheets);
        Teams = new JsonRecordStore<TeamRecord>(paths.Teams);
        Zones = new JsonRecordStore<ZoneRecord>(paths.Zones);
    }
}

public sealed record ImportReport(int Sheets, int Teams, int Zones, int Skipped, IReadOnlyList<string>? Warnings = null)
{
    public int Total => Sheets + Teams + Zones;
    public IReadOnlyList<string> AllWarnings => Warnings ?? Array.Empty<string>();
    public override string ToString() =>
        $"Imported {Sheets} sheet(s), {Teams} team(s), {Zones} zone(s)" + (Skipped > 0 ? $"; skipped {Skipped} file(s)." : ".");
}

/// <summary>
/// Imports JSON files written by the website (server folders <c>pokemon_cards/</c>, <c>teams/</c>,
/// <c>zones/</c>, or the encounter generator's "Save JSON" export) and the Markdown sheets of the
/// Pokemon Generator (<c>.md</c>, needs <paramref name="data"/>). Ids are kept so team slots still
/// point to the right sheets.
/// </summary>
public static class WebsiteImporter
{
    public static ImportReport ImportPath(string path, UserLibrary library, Data.GameData? data = null)
    {
        var files = new List<string>();
        if (File.Exists(path)) files.Add(path);
        else if (Directory.Exists(path))
        {
            foreach (var sub in new[] { "pokemon_cards", "teams", "zones" })
            {
                var dir = Path.Combine(path, sub);
                if (Directory.Exists(dir)) files.AddRange(Directory.EnumerateFiles(dir, "*.json"));
            }
            if (files.Count == 0) files.AddRange(Directory.EnumerateFiles(path, "*.json"));
            if (data != null) files.AddRange(Directory.EnumerateFiles(path, "*.md"));
        }
        else throw new FileNotFoundException("Path not found.", path);

        int sheets = 0, teams = 0, zones = 0, skipped = 0;
        var warnings = new List<string>();
        foreach (var file in files)
        {
            if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                var markdown = File.ReadAllText(file);
                if (data is null || !MarkdownSheetImporter.LooksLikeSheet(markdown))
                {
                    skipped++;
                    warnings.Add($"{name}: not a Pokémon sheet.");
                    continue;
                }
                try
                {
                    var result = MarkdownSheetImporter.Parse(markdown, data, name);
                    library.Sheets.Save(result.Sheet);
                    sheets++;
                    warnings.AddRange(result.Warnings.Select(w => $"{name}: {w}"));
                }
                catch (Exception ex)
                {
                    skipped++;
                    warnings.Add($"{name}: {ex.Message}");
                }
                continue;
            }
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
                if (node is null) { skipped++; continue; }
                if (node.ContainsKey("state") || node.ContainsKey("pokemonName"))
                {
                    var rec = node.Deserialize<SheetRecord>(JsonDefaults.Options)!;
                    if (string.IsNullOrWhiteSpace(rec.Nickname)) rec.Nickname = rec.PokemonName;
                    if (string.IsNullOrWhiteSpace(rec.State.ChosenPokemon)) rec.State.ChosenPokemon = rec.PokemonName;
                    library.Sheets.Save(rec, touch: false);
                    sheets++;
                }
                else if (node.ContainsKey("slots"))
                {
                    var rec = node.Deserialize<TeamRecord>(JsonDefaults.Options)!;
                    rec.Normalize();
                    library.Teams.Save(rec, touch: false);
                    teams++;
                }
                else if (node.ContainsKey("sections"))
                {
                    var rec = node.Deserialize<ZoneRecord>(JsonDefaults.Options)!;
                    if (string.IsNullOrWhiteSpace(rec.Name)) rec.Name = rec.ZoneName ?? Path.GetFileNameWithoutExtension(file);
                    rec.ZoneName = null;
                    if (!node.ContainsKey("id")) rec.Id = Guid.NewGuid().ToString();
                    library.Zones.Save(rec, touch: false);
                    zones++;
                }
                else skipped++;
            }
            catch (Exception)
            {
                skipped++;
            }
        }
        return new ImportReport(sheets, teams, zones, skipped, warnings);
    }
}
