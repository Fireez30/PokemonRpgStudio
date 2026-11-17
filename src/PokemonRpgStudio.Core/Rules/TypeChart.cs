using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PokemonRpgStudio.Core.Json;

namespace PokemonRpgStudio.Core.Rules;

/// <summary>
/// Type effectiveness of the RPG: how much damage an attacking type deals to a defending type
/// (0 = no effect, 0.5 = not very effective, 1 = neutral, 2 = super effective).
/// Read from <c>type_chart.json</c> in the data folder; <see cref="Default"/> is written there when the
/// file is missing, so it can be edited like the other data files.
/// </summary>
public sealed class TypeChart
{
    public const string FileName = "type_chart.json";

    private readonly Dictionary<string, Dictionary<string, double>> _damageTaken;

    /// <summary>Every type, in display order.</summary>
    public IReadOnlyList<string> Types { get; }

    private TypeChart(IReadOnlyList<string> types, Dictionary<string, Dictionary<string, double>> damageTaken)
    {
        Types = types;
        _damageTaken = damageTaken;
    }

    /// <summary>Multiplier of an <paramref name="attacking"/> move on one <paramref name="defending"/> type (1 when unknown).</summary>
    public double Multiplier(string? attacking, string? defending)
    {
        if (string.IsNullOrWhiteSpace(attacking) || string.IsNullOrWhiteSpace(defending)) return 1;
        return _damageTaken.TryGetValue(defending.Trim(), out var row) && row.TryGetValue(attacking.Trim(), out var value) ? value : 1;
    }

    /// <summary>Multiplier on a Pokémon with several types: the multipliers of each type are multiplied.</summary>
    public double Multiplier(string? attacking, IEnumerable<string?> defending)
    {
        double result = 1;
        foreach (var type in defending.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            result *= Multiplier(attacking, type);
        return result;
    }

    /// <summary>Multiplier of every attacking type on a Pokémon of the given types, in chart order.</summary>
    public IReadOnlyList<(string Attacking, double Multiplier)> DamageTaken(IEnumerable<string?> defending)
    {
        var defenders = defending.ToList();
        return Types.Select(t => (t, Multiplier(t, defenders))).ToList();
    }

    /// <summary>"2", "0.5", "0.25"…</summary>
    public static string Format(double multiplier) => multiplier.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Describe(double multiplier) => multiplier switch
    {
        0 => "no effect",
        < 1 => "not very effective",
        > 1 => "super effective",
        _ => "neutral",
    };

    // ---- File format -------------------------------------------------------------------------

    /// <summary>
    /// <c>type_chart.json</c>: <c>types</c> lists the types in display order; <c>damage_taken</c> maps each
    /// defending type to the multiplier of each attacking type. A missing value means 1 (neutral).
    /// </summary>
    private sealed class FileModel
    {
        [JsonPropertyName("_help"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Help { get; set; }

        [JsonPropertyName("types")] public List<string> Types { get; set; } = new();

        [JsonPropertyName("damage_taken")]
        public Dictionary<string, Dictionary<string, double>> DamageTaken { get; set; } = new();
    }

    private const string HelpText =
        "damage_taken.<defending type>.<attacking type> = multiplier (0 no effect, 0.5 not very effective, 1 neutral, 2 super effective). Missing values are 1.";

    public static TypeChart FromJson(string json)
    {
        var model = JsonSerializer.Deserialize<FileModel>(json, JsonDefaults.Options)
                    ?? throw new InvalidDataException("The type chart is empty.");
        return FromRows(model.Types, model.DamageTaken);
    }

    public static TypeChart Load(string file) => FromJson(File.ReadAllText(file));

    /// <summary>Reads <c>type_chart.json</c> from the data folder, or returns <see cref="Default"/> when it doesn't exist.</summary>
    public static TypeChart LoadOrDefault(string dataDir)
    {
        var file = Path.Combine(dataDir, FileName);
        return File.Exists(file) ? Load(file) : Default;
    }

    public string ToJson()
    {
        var model = new FileModel
        {
            Help = HelpText,
            Types = Types.ToList(),
            DamageTaken = Types.ToDictionary(d => d, d => Types.ToDictionary(a => a, a => Multiplier(a, d))),
        };
        return JsonSerializer.Serialize(model, JsonDefaults.Options);
    }

    /// <summary>Writes the chart to <paramref name="file"/> (through a temporary file).</summary>
    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, file, overwrite: true);
    }

    /// <summary>Writes <see cref="Default"/> to the data folder when the file is missing (or always with <paramref name="overwrite"/>).</summary>
    public static bool EnsureFile(string dataDir, bool overwrite = false)
    {
        var file = Path.Combine(dataDir, FileName);
        if (File.Exists(file) && !overwrite) return false;
        Default.Save(file);
        return true;
    }

    private static TypeChart FromRows(IEnumerable<string>? types, Dictionary<string, Dictionary<string, double>>? damageTaken)
    {
        damageTaken ??= new();
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? t)
        {
            if (!string.IsNullOrWhiteSpace(t) && seen.Add(t.Trim())) ordered.Add(t.Trim());
        }
        foreach (var t in types ?? Enumerable.Empty<string>()) Add(t);
        // Types only named inside damage_taken are still shown.
        foreach (var (defending, row) in damageTaken)
        {
            Add(defending);
            foreach (var attacking in row?.Keys ?? Enumerable.Empty<string>()) Add(attacking);
        }
        if (ordered.Count == 0) throw new InvalidDataException("The type chart has no types.");

        var table = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (defending, row) in damageTaken)
        {
            if (string.IsNullOrWhiteSpace(defending) || row is null) continue;
            var target = table.TryGetValue(defending.Trim(), out var existing)
                ? existing
                : table[defending.Trim()] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (attacking, value) in row)
            {
                if (string.IsNullOrWhiteSpace(attacking)) continue;
                if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
                    throw new InvalidDataException($"Invalid multiplier {value} for {attacking} → {defending}.");
                target[attacking.Trim()] = value;
            }
        }
        return new TypeChart(ordered, table);
    }

    // ---- Default chart -----------------------------------------------------------------------

    private static readonly string[] DefaultTypes =
    {
        "Bug", "Dark", "Dragon", "Electric", "Fairy", "Fighting", "Fire", "Flying", "Ghost", "Grass", "Ground",
        "Ice", "Normal", "Poison", "Psychic", "Rock", "Steel", "Water", "Light", "Data", "Sound",
    };

    // One row per defending type; columns are the attacking types, in DefaultTypes order.
    private static readonly double[][] DefaultRows =
    {
        //            Bug  Dark Drag Elec Fair Figh Fire Flyi Ghos Gras Grou Ice  Norm Pois Psyc Rock Stee Wate Ligh Data Soun
        /* Bug      */ new double[] { 1,   1,   1,   1,   1,   0.5, 2,   2,   1,   0.5, 0.5, 1,   1,   1,   1,   2,   1,   1,   1,   1,   1   },
        /* Dark     */ new double[] { 2,   0.5, 1,   1,   2,   2,   1,   1,   0.5, 1,   1,   1,   1,   1,   0,   1,   1,   1,   2,   1,   1   },
        /* Dragon   */ new double[] { 1,   1,   2,   0.5, 2,   1,   0.5, 1,   1,   0.5, 1,   2,   1,   1,   1,   1,   1,   0.5, 1,   1,   0.5 },
        /* Electric */ new double[] { 1,   1,   1,   0.5, 1,   1,   1,   0.5, 1,   1,   2,   1,   1,   1,   1,   1,   0.5, 1,   1,   1,   0.5 },
        /* Fairy    */ new double[] { 0.5, 0.5, 0,   1,   1,   0.5, 1,   1,   1,   1,   1,   1,   1,   2,   1,   1,   2,   1,   0.5, 1,   2   },
        /* Fighting */ new double[] { 0.5, 0.5, 1,   1,   2,   1,   1,   2,   1,   1,   1,   1,   1,   1,   2,   0.5, 1,   1,   1,   1,   1   },
        /* Fire     */ new double[] { 0.5, 1,   1,   1,   0.5, 1,   0.5, 1,   1,   0.5, 2,   0.5, 1,   1,   1,   2,   0.5, 2,   1,   1,   1   },
        /* Flying   */ new double[] { 0.5, 1,   1,   2,   1,   0.5, 1,   1,   1,   0.5, 0,   2,   1,   1,   1,   2,   1,   1,   1,   1,   2   },
        /* Ghost    */ new double[] { 0.5, 2,   1,   1,   1,   0,   1,   1,   2,   1,   1,   1,   0,   0.5, 1,   1,   1,   1,   2,   1,   1   },
        /* Grass    */ new double[] { 2,   1,   1,   0.5, 1,   1,   2,   2,   1,   0.5, 0.5, 2,   1,   2,   1,   1,   1,   0.5, 1,   1,   1   },
        /* Ground   */ new double[] { 1,   1,   1,   0,   1,   1,   1,   1,   1,   2,   1,   2,   1,   0.5, 1,   0.5, 1,   2,   1,   1,   1   },
        /* Ice      */ new double[] { 1,   1,   1,   1,   1,   2,   2,   1,   1,   1,   1,   0.5, 1,   1,   1,   2,   2,   1,   0.5, 0,   0.5 },
        /* Normal   */ new double[] { 1,   1,   1,   1,   1,   2,   1,   1,   0,   1,   1,   1,   1,   1,   1,   1,   1,   1,   1,   1,   1   },
        /* Poison   */ new double[] { 0.5, 1,   1,   1,   0.5, 0.5, 1,   1,   1,   0.5, 2,   1,   1,   0.5, 2,   1,   1,   1,   1,   1,   1   },
        /* Psychic  */ new double[] { 2,   2,   1,   1,   1,   0.5, 1,   1,   2,   1,   1,   1,   1,   1,   0.5, 1,   1,   1,   1,   0.5, 0.5 },
        /* Rock     */ new double[] { 1,   1,   1,   1,   1,   2,   0.5, 0.5, 1,   2,   2,   1,   0.5, 0.5, 1,   1,   2,   2,   1,   1,   1   },
        /* Steel    */ new double[] { 0.5, 1,   0.5, 1,   0.5, 2,   2,   0.5, 1,   0.5, 2,   0.5, 0.5, 0,   0.5, 0.5, 0.5, 1,   0,   1,   1   },
        /* Water    */ new double[] { 1,   1,   1,   2,   1,   1,   0.5, 1,   1,   2,   1,   0.5, 1,   1,   1,   1,   0.5, 2,   1,   1,   2   },
        /* Light    */ new double[] { 1,   2,   1,   0.5, 0.5, 1,   0.5, 1,   2,   1,   1,   1,   1,   1,   1,   1,   0.5, 0.5, 1,   1,   0.5 },
        /* Data     */ new double[] { 2,   2,   0,   1,   0,   0,   2,   0,   1,   0,   0,   1,   0,   0,   2,   0,   0,   2,   2,   0.5, 0   },
        /* Sound    */ new double[] { 1,   1,   2,   2,   0.5, 1,   1,   0.5, 1,   1,   1,   1,   1,   1,   1,   1,   1,   0.5, 1,   0.5, 1   },
    };

    /// <summary>The chart of the RPG (18 official types + Light, Data and Sound).</summary>
    public static TypeChart Default { get; } = FromRows(
        DefaultTypes,
        DefaultTypes.Select((defending, i) => (defending, row: DefaultTypes.Select((attacking, j) => (attacking, value: DefaultRows[i][j]))
                .ToDictionary(x => x.attacking, x => x.value)))
            .ToDictionary(x => x.defending, x => x.row));
}
