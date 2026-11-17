using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PokemonRpgStudio.Core.Json;

namespace PokemonRpgStudio.Core.Models;

/// <summary>Reference to an ability inside a species entry (name only is meaningful).</summary>
public sealed class AbilityRef
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("effect")] public string? Effect { get; set; } = "";
    [JsonPropertyName("id")] public int Id { get; set; } = -1;

    public AbilityRef() { }
    public AbilityRef(string name) { Name = name; }
    public override string ToString() => Name;
}

/// <summary>A move entry inside a species learnset (level-up, TM, tutor or egg).</summary>
public sealed class LearnsetMove
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("level"), JsonConverter(typeof(FlexibleIntConverter))]
    public int Level { get; set; } = -1;

    [JsonPropertyName("types"), JsonConverter(typeof(StringOrListConverter)),
     JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Types { get; set; }

    [JsonPropertyName("type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonIgnore]
    public IReadOnlyList<string> AllTypes =>
        Types is { Count: > 0 } ? Types : (string.IsNullOrWhiteSpace(Type) ? Array.Empty<string>() : new[] { Type! });

    public override string ToString() => Name;
}

public sealed class NameValue
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value"), JsonConverter(typeof(AnyToStringConverter))] public string? Value { get; set; } = "";
}

public sealed class SkillEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("roll"), JsonConverter(typeof(AnyToStringConverter))] public string? Roll { get; set; } = "";
}

/// <summary>A Pokémon species as described in <c>pokemon.json</c>.</summary>
public sealed class PokemonSpecies
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("stat_hp"), JsonConverter(typeof(FlexibleIntConverter))] public int StatHp { get; set; }
    [JsonPropertyName("stat_atk"), JsonConverter(typeof(FlexibleIntConverter))] public int StatAtk { get; set; }
    [JsonPropertyName("stat_def"), JsonConverter(typeof(FlexibleIntConverter))] public int StatDef { get; set; }
    [JsonPropertyName("stat_sp_atk"), JsonConverter(typeof(FlexibleIntConverter))] public int StatSpAtk { get; set; }
    [JsonPropertyName("stat_sp_def"), JsonConverter(typeof(FlexibleIntConverter))] public int StatSpDef { get; set; }
    [JsonPropertyName("stat_spd"), JsonConverter(typeof(FlexibleIntConverter))] public int StatSpd { get; set; }

    [JsonPropertyName("pokemon_types"), JsonConverter(typeof(StringOrListConverter))]
    public List<string> PokemonTypes { get; set; } = new();

    [JsonPropertyName("base_abilities")] public List<AbilityRef> BaseAbilities { get; set; } = new();
    [JsonPropertyName("advanced_abilities")] public List<AbilityRef> AdvancedAbilities { get; set; } = new();
    [JsonPropertyName("high_abilities")] public List<AbilityRef> HighAbilities { get; set; } = new();
    [JsonPropertyName("custom_abilities")] public Dictionary<string, AbilityRef>? CustomAbilities { get; set; }

    [JsonPropertyName("evolutions"), JsonConverter(typeof(StringOrListConverter))]
    public List<string> Evolutions { get; set; } = new();

    [JsonPropertyName("height")] public string? Height { get; set; } = "";
    [JsonPropertyName("weight")] public string? Weight { get; set; } = "";

    [JsonPropertyName("gender_ratio_m"), JsonConverter(typeof(FlexibleNullableDoubleConverter))]
    public double? GenderRatioM { get; set; }

    [JsonPropertyName("gender_ratio_f"), JsonConverter(typeof(FlexibleNullableDoubleConverter))]
    public double? GenderRatioF { get; set; }

    [JsonPropertyName("egg_group")] public string? EggGroup { get; set; } = "";

    [JsonPropertyName("average_hatch_rate"), JsonConverter(typeof(AnyToStringConverter))]
    public string? AverageHatchRate { get; set; } = "";

    [JsonPropertyName("diet")] public string? Diet { get; set; } = "";
    [JsonPropertyName("habitat")] public string? Habitat { get; set; } = "";
    [JsonPropertyName("capabilities")] public List<NameValue> Capabilities { get; set; } = new();
    [JsonPropertyName("skills")] public List<SkillEntry> Skills { get; set; } = new();
    [JsonPropertyName("evo_moves")] public List<LearnsetMove>? EvoMoves { get; set; }
    [JsonPropertyName("moves")] public List<LearnsetMove> Moves { get; set; } = new();
    [JsonPropertyName("tm_moves")] public List<LearnsetMove> TmMoves { get; set; } = new();
    [JsonPropertyName("tutor_moves")] public List<LearnsetMove> TutorMoves { get; set; } = new();
    [JsonPropertyName("egg_moves")] public List<LearnsetMove> EggMoves { get; set; } = new();

    /// <summary>Free-form mega evolution block (its shape varies between entries).</summary>
    [JsonPropertyName("mega_evolution")] public JsonObject? MegaEvolution { get; set; }

    /// <summary>Keeps any field this model doesn't know about so saving never loses data.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore] public bool IsGenderless => GenderRatioM is { } m && Math.Abs(m - -1) < 0.0001;

    [JsonIgnore] public string DisplayName => Formatting.TitleCase(Name);

    public int BaseStat(Stat stat) => stat switch
    {
        Stat.HP => StatHp,
        Stat.ATK => StatAtk,
        Stat.DEF => StatDef,
        Stat.SPATK => StatSpAtk,
        Stat.SPDEF => StatSpDef,
        Stat.SPD => StatSpd,
        _ => 0,
    };

    public PokemonSpecies DeepClone() =>
        JsonSerializer.Deserialize<PokemonSpecies>(JsonSerializer.Serialize(this, JsonDefaults.Compact), JsonDefaults.Options)!;

    public override string ToString() => Name;
}

/// <summary>Move details from <c>moves.json</c>; also the shape stored inside saved sheets.</summary>
public sealed class MoveData
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("types"), JsonConverter(typeof(StringOrListConverter))]
    public List<string> Types { get; set; } = new();

    [JsonPropertyName("frequency"), JsonConverter(typeof(AnyToStringConverter))] public string? Frequency { get; set; } = "";
    [JsonPropertyName("AC"), JsonConverter(typeof(AnyToStringConverter))] public string? AC { get; set; } = "";

    [JsonPropertyName("damage_base"), JsonConverter(typeof(FlexibleIntConverter))]
    public int DamageBase { get; set; } = -1;

    [JsonPropertyName("roll"), JsonConverter(typeof(AnyToStringConverter))] public string? Roll { get; set; } = "";
    [JsonPropertyName("m_class"), JsonConverter(typeof(AnyToStringConverter))] public string? MoveClass { get; set; } = "";
    [JsonPropertyName("range"), JsonConverter(typeof(AnyToStringConverter))] public string? Range { get; set; } = "";
    [JsonPropertyName("effect"), JsonConverter(typeof(AnyToStringConverter))] public string? Effect { get; set; } = "";

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore] public string TypesLabel => string.Join(" / ", Types.Select(t => t.Trim()).Where(t => t.Length > 0));

    public MoveData Clone() => new()
    {
        Name = Name,
        Types = new List<string>(Types),
        Frequency = Frequency,
        AC = AC,
        DamageBase = DamageBase,
        Roll = Roll,
        MoveClass = MoveClass,
        Range = Range,
        Effect = Effect,
        Extra = Extra is null ? null : new Dictionary<string, JsonElement>(Extra),
    };

    public static MoveData Placeholder(string name) => new() { Name = name, DamageBase = 0, Roll = "", AC = "", Frequency = "", MoveClass = "", Range = "", Effect = "" };

    public override string ToString() => Name;
}

public sealed class AbilityData
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("effect"), JsonConverter(typeof(AnyToStringConverter))] public string? Effect { get; set; } = "";
    [JsonPropertyName("id"), JsonConverter(typeof(FlexibleIntConverter))] public int Id { get; set; } = -1;
    public override string ToString() => Name;
}

public enum Stat { HP, ATK, DEF, SPATK, SPDEF, SPD }

public static class Formatting
{
    public static string TitleCase(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return string.Join(" ", value.Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }

    public static string Capitalize(string? value) =>
        string.IsNullOrEmpty(value) ? "" : char.ToUpperInvariant(value[0]) + value[1..];

    public static string SignedNumber(int value) => value > 0 ? "+" + value : value.ToString();

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    public static string CollapseWhitespace(string? s) => Whitespace.Replace(s ?? "", " ").Trim();
}
