using System.Text.Json;
using PokemonRpgStudio.Core.Json;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.Core.Data;

/// <summary>
/// The species / move / ability databases. They are always read from three plain files in one data
/// folder (pokemon.json, moves.json, abilities.json) — never from inside the application — so they
/// can be replaced or edited at any time. Packages only ship <em>default</em> copies, used to fill
/// the data folder the first time (see <see cref="SeedDataFolder"/>). Sheets store names only, so
/// updating moves.json updates every sheet.
/// </summary>
public sealed class GameData
{
    public static readonly string[] FileNames = { "pokemon.json", "moves.json", "abilities.json" };

    private Dictionary<string, PokemonSpecies> _speciesByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, MoveData> _movesByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, AbilityData> _abilitiesByName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PokemonSpecies> Pokemon { get; private set; } = Array.Empty<PokemonSpecies>();
    public IReadOnlyList<MoveData> Moves { get; private set; } = Array.Empty<MoveData>();
    public IReadOnlyList<AbilityData> Abilities { get; private set; } = Array.Empty<AbilityData>();

    /// <summary>Type effectiveness, from the optional type_chart.json (built-in chart when it's missing).</summary>
    public TypeChart TypeChart { get; private set; } = TypeChart.Default;

    /// <summary>The folder the three files are read from (and Pokédex edits are written to).</summary>
    public string DataDir { get; private set; } = "";

    public string FilePath(string fileName) => Path.Combine(DataDir, fileName);
    public string PokemonFile => FilePath("pokemon.json");

    /// <summary>Loads the three files of <paramref name="dataDir"/>.</summary>
    /// <exception cref="MissingDataException">When one of the files doesn't exist.</exception>
    public static GameData Load(string dataDir)
    {
        var missing = MissingFiles(dataDir);
        if (missing.Count > 0) throw new MissingDataException(dataDir, missing);
        var data = new GameData { DataDir = dataDir };
        data.SetPokemon(ReadList<PokemonSpecies>(data.FilePath("pokemon.json")));
        data.SetMoves(ReadList<MoveData>(data.FilePath("moves.json")));
        data.SetAbilities(ReadList<AbilityData>(data.FilePath("abilities.json")));
        try { data.TypeChart = TypeChart.LoadOrDefault(dataDir); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"{TypeChart.FileName}: {ex.Message}", ex);
        }
        return data;
    }

    public static List<string> MissingFiles(string dataDir) =>
        FileNames.Where(f => !File.Exists(Path.Combine(dataDir, f))).ToList();

    /// <summary>
    /// Copies default data files into <paramref name="dataDir"/>: only the missing ones, or all of them
    /// when <paramref name="overwrite"/> is set. Each file is taken from the first default folder that
    /// has it. Returns the names of the copied files.
    /// </summary>
    public static List<string> SeedDataFolder(string dataDir, IEnumerable<string> defaultDirs, bool overwrite = false)
    {
        var copied = new List<string>();
        var sources = defaultDirs.Where(Directory.Exists).ToList();
        Directory.CreateDirectory(dataDir);
        foreach (var name in FileNames)
        {
            var target = Path.Combine(dataDir, name);
            if (File.Exists(target) && !overwrite) continue;
            var source = sources.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
            if (source is null || Path.GetFullPath(source) == Path.GetFullPath(target)) continue;
            File.Copy(source, target, overwrite: true);
            copied.Add(name);
        }
        return copied;
    }

    /// <summary>
    /// Installs an updated data file (pokemon.json, moves.json or abilities.json, recognised by its
    /// name) into the data folder after checking that it can be read. Reload to use it.
    /// </summary>
    public static string InstallDataFile(string sourceFile, string dataDir)
    {
        var name = Path.GetFileName(sourceFile).ToLowerInvariant();
        if (name == TypeChart.FileName)
        {
            var chart = TypeChart.Load(sourceFile);
            Directory.CreateDirectory(dataDir);
            var chartTarget = Path.Combine(dataDir, name);
            if (Path.GetFullPath(sourceFile) != Path.GetFullPath(chartTarget)) File.Copy(sourceFile, chartTarget, overwrite: true);
            return $"{name}: {chart.Types.Count} types";
        }
        if (!FileNames.Contains(name))
            throw new InvalidDataException($"\"{Path.GetFileName(sourceFile)}\" is not a data file: expected pokemon.json, moves.json, abilities.json or {TypeChart.FileName}.");
        int count = name switch
        {
            "pokemon.json" => ReadList<PokemonSpecies>(sourceFile).Count(p => !string.IsNullOrWhiteSpace(p.Name)),
            "moves.json" => ReadList<MoveData>(sourceFile).Count(m => !string.IsNullOrWhiteSpace(m.Name)),
            _ => ReadList<AbilityData>(sourceFile).Count(a => !string.IsNullOrWhiteSpace(a.Name)),
        };
        if (count == 0) throw new InvalidDataException($"{name} contains no entries.");
        Directory.CreateDirectory(dataDir);
        var target = Path.Combine(dataDir, name);
        if (Path.GetFullPath(sourceFile) != Path.GetFullPath(target)) File.Copy(sourceFile, target, overwrite: true);
        return $"{name}: {count} entries";
    }

    /// <summary>Builds an in-memory database (used by tests).</summary>
    public static GameData FromLists(IEnumerable<PokemonSpecies> pokemon, IEnumerable<MoveData> moves, IEnumerable<AbilityData> abilities)
    {
        var data = new GameData();
        data.SetPokemon(pokemon.ToList());
        data.SetMoves(moves.ToList());
        data.SetAbilities(abilities.ToList());
        return data;
    }

    private static List<T> ReadList<T>(string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException($"Data file not found: {file}", file);
        using var stream = File.OpenRead(file);
        return JsonSerializer.Deserialize<List<T>>(stream, JsonDefaults.Options) ?? new List<T>();
    }

    private void SetPokemon(List<PokemonSpecies> list)
    {
        // Keep the first entry per name, sorted alphabetically like the website selectors.
        var unique = new Dictionary<string, PokemonSpecies>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in list.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
            unique.TryAdd(p.Name, p);
        _speciesByName = unique;
        Pokemon = unique.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void SetMoves(List<MoveData> list)
    {
        var dict = new Dictionary<string, MoveData>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in list.Where(m => !string.IsNullOrWhiteSpace(m.Name)))
            dict.TryAdd(m.Name.Trim(), m);
        _movesByName = dict;
        Moves = list;
    }

    private void SetAbilities(List<AbilityData> list)
    {
        var dict = new Dictionary<string, AbilityData>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in list.Where(a => !string.IsNullOrWhiteSpace(a.Name)))
            dict.TryAdd(a.Name.Trim(), a);
        _abilitiesByName = dict;
        Abilities = list;
    }

    public PokemonSpecies? FindPokemon(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : _speciesByName.GetValueOrDefault(name.Trim());

    public MoveData? FindMove(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : _movesByName.GetValueOrDefault(name.Replace(":", "").Trim());

    public AbilityData? FindAbility(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : _abilitiesByName.GetValueOrDefault(name.Trim());

    /// <summary>Resolves learnset entries to full move data, skipping unknown moves (website behaviour).</summary>
    public List<MoveData> ResolveMoves(IEnumerable<LearnsetMove> learnset)
    {
        var result = new List<MoveData>();
        foreach (var entry in learnset)
        {
            var move = FindMove(entry.Name);
            if (move != null && !result.Contains(move)) result.Add(move);
        }
        return result;
    }

    /// <summary>
    /// Egg moves usable by a species. When a species has none of its own, the RPG rules borrow
    /// the egg moves of the first form listed in its evolution line.
    /// </summary>
    public List<LearnsetMove> EffectiveEggMoves(PokemonSpecies species)
    {
        if (species.EggMoves.Count > 0) return species.EggMoves;
        if (species.Evolutions.Count > 0)
        {
            var baseForm = FindPokemon(species.Evolutions[0].Trim());
            if (baseForm is { EggMoves.Count: > 0 }) return baseForm.EggMoves;
        }
        return new List<LearnsetMove>();
    }

    /// <summary>Replaces a species and writes the whole list back to the data folder's pokemon.json.</summary>
    public PokemonSpecies SaveSpecies(string originalName, PokemonSpecies updated)
    {
        if (string.IsNullOrEmpty(DataDir)) throw new InvalidOperationException("No data folder configured.");
        var list = Pokemon.ToList();
        var idx = list.FindIndex(p => string.Equals(p.Name, originalName, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) throw new KeyNotFoundException($"Pokémon '{originalName}' not found.");
        list[idx] = updated;
        SetPokemon(list);

        Directory.CreateDirectory(DataDir);
        var tmp = PokemonFile + ".tmp";
        using (var stream = File.Create(tmp))
            JsonSerializer.Serialize(stream, Pokemon, JsonDefaults.Options);
        File.Move(tmp, PokemonFile, overwrite: true);
        return updated;
    }
}

/// <summary>One or more of pokemon.json / moves.json / abilities.json is missing from the data folder.</summary>
public sealed class MissingDataException(string dataDir, IReadOnlyList<string> missing)
    : Exception($"Missing data file(s) in {dataDir}: {string.Join(", ", missing)}")
{
    public string DataDir { get; } = dataDir;
    public IReadOnlyList<string> Missing { get; } = missing;
}
