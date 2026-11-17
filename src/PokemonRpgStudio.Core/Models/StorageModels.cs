using System.Text.Json;
using System.Text.Json.Serialization;
using PokemonRpgStudio.Core.Json;

namespace PokemonRpgStudio.Core.Models;

/// <summary>Anything persisted as one JSON file per record.</summary>
public interface IStoredRecord
{
    string Id { get; set; }
    string DisplayName { get; }
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A record that can be put in a library folder (see <see cref="Storage.SheetFolders"/>).</summary>
public interface IFolderRecord : IStoredRecord
{
    /// <summary>"/"-separated folder path ("" = no folder).</summary>
    string Folder { get; set; }
}

/// <summary>
/// State of a Pokémon sheet. Property names match the website's Redux slice (and the
/// <c>server/pokemon_cards/*.json</c> files) so sheets can be moved between both tools.
/// </summary>
public sealed class SheetState
{
    [JsonPropertyName("chosen_pokemon")] public string ChosenPokemon { get; set; } = "";
    /// <summary>Can be fractional: Pokémon gain partial levels (0.X) from experience.</summary>
    [JsonPropertyName("pokemon_level"), JsonConverter(typeof(FlexibleDoubleConverter))] public double Level { get; set; }
    /// <summary>
    /// Experience of the Pokémon (0…100). Sheets saved before this field load with 0; out-of-range
    /// values are clamped.
    /// </summary>
    [JsonPropertyName("pokemon_experience"), JsonConverter(typeof(FlexibleIntConverter))]
    public int Experience { get => _experience; set => _experience = Math.Clamp(value, 0, Rules.SheetRules.MaxExperience); }
    private int _experience;
    [JsonPropertyName("pokemon_rarity")] public string Rarity { get; set; } = "Normal";
    [JsonPropertyName("pokemon_card")] public string Card { get; set; } = "None";
    [JsonPropertyName("pokemon_gender")] public string Gender { get; set; } = "";
    [JsonPropertyName("pokemon_final_buffed_stat")] public string BuffedStat { get; set; } = "";
    [JsonPropertyName("pokemon_final_lowered_stat")] public string LoweredStat { get; set; } = "";

    [JsonPropertyName("pokemon_base_ability"), JsonConverter(typeof(StringOrListConverter))]
    public List<string> BaseAbilities { get; set; } = new();

    [JsonPropertyName("pokemon_advanced_ability"), JsonConverter(typeof(StringOrListConverter))]
    public List<string> AdvancedAbilities { get; set; } = new();

    [JsonPropertyName("pokemon_high_ability"), JsonConverter(typeof(StringOrListConverter))]
    public List<string> HighAbilities { get; set; } = new();

    [JsonPropertyName("pokemon_custom_ability"), JsonConverter(typeof(StringOrListConverter))]
    public List<string> CustomAbilities { get; set; } = new();

    /// <summary>
    /// Desktop-only: abilities added by hand on top of the species' list (any ability of
    /// abilities.json, or a free name). Never reported as an error by the validation.
    /// </summary>
    [JsonPropertyName("pokemon_extra_ability"), JsonConverter(typeof(StringOrListConverter))]
    public List<string> ExtraAbilities { get; set; } = new();

    [JsonPropertyName("pokemon_points_by_stats")]
    public Dictionary<string, int> PointsByStat { get; set; } = SheetRulesDefaults.EmptyStats();

    // Move lists hold names only; details come from moves.json at run time (see SheetRules.ResolveMove).
    [JsonPropertyName("pokemon_chosen_moves"), JsonConverter(typeof(MoveNameListConverter))]
    public List<string> ChosenMoves { get; set; } = new();
    [JsonPropertyName("pokemon_chosen_egg_moves"), JsonConverter(typeof(MoveNameListConverter))]
    public List<string> EggMoves { get; set; } = new();
    [JsonPropertyName("pokemon_chosen_tmhm_moves"), JsonConverter(typeof(MoveNameListConverter))]
    public List<string> TmMoves { get; set; } = new();
    [JsonPropertyName("pokemon_chosen_tutor_moves"), JsonConverter(typeof(MoveNameListConverter))]
    public List<string> TutorMoves { get; set; } = new();
    [JsonPropertyName("pokemon_locked_egg_moves")] public List<string> LockedEggMoves { get; set; } = new();
    [JsonPropertyName("pokemon_locked_tmhm_moves")] public List<string> LockedTmMoves { get; set; } = new();
    [JsonPropertyName("pokemon_locked_tutor_moves")] public List<string> LockedTutorMoves { get; set; } = new();
    [JsonPropertyName("pokemon_db_buff_by_move_name")] public Dictionary<string, int> DbBuffByMove { get; set; } = new();
    [JsonPropertyName("remaining_rolls"), JsonConverter(typeof(FlexibleIntConverter))] public int RemainingRolls { get; set; } = 3;

    /// <summary>
    /// Desktop-only: tutor points added (+) or removed (−) by hand, on top of the ones earned by level
    /// (one every 5 levels, see <see cref="Rules.SheetRules.TutorPoints(double, int)"/>).
    /// </summary>
    [JsonPropertyName("tutor_points_adjustment"), JsonConverter(typeof(FlexibleIntConverter))]
    public int TutorPointsAdjustment { get; set; }

    /// <summary>Desktop-only: free notes about the tutor points (what they were spent on…).</summary>
    [JsonPropertyName("tutor_notes")] public string TutorNotes { get; set; } = "";

    /// <summary>Desktop-only: every nature rolled for this sheet, so the player can pick their favourite.</summary>
    [JsonPropertyName("rolled_natures"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RolledNature>? RolledNatures { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public int Points(Stat stat) => PointsByStat.TryGetValue(stat.ToString(), out var v) ? v : 0;

    public SheetState Clone() =>
        JsonSerializer.Deserialize<SheetState>(JsonSerializer.Serialize(this, JsonDefaults.Compact), JsonDefaults.Options)!;
}

/// <summary>One nature roll: the buffed and the lowered stat.</summary>
public sealed record RolledNature(
    [property: JsonPropertyName("buffed")] string Buffed,
    [property: JsonPropertyName("lowered")] string Lowered);

public static class SheetRulesDefaults
{
    public static Dictionary<string, int> EmptyStats() => new()
    {
        ["HP"] = 0, ["ATK"] = 0, ["DEF"] = 0, ["SPATK"] = 0, ["SPDEF"] = 0, ["SPD"] = 0,
    };
}

/// <summary>A saved Pokémon sheet ("fiche"), identical in shape to the website's pokemon card files.</summary>
public sealed class SheetRecord : IFolderRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("nickname")] public string Nickname { get; set; } = "";
    [JsonPropertyName("pokemonName")] public string PokemonName { get; set; } = "";
    [JsonPropertyName("state")] public SheetState State { get; set; } = new();
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    /// <summary>Desktop-only: roster status (see <see cref="Rules.SheetRules.Statuses"/>).</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = Rules.SheetRules.DefaultStatus;
    /// <summary>Desktop-only: folder of the sheet in the library, "/"-separated ("" = no folder).</summary>
    [JsonPropertyName("folder")] public string Folder { get; set; } = "";

    /// <summary>Desktop-only: in-game tracking (HP, injuries, stat changes). Ignored by the website.</summary>
    [JsonPropertyName("play"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SheetPlayState? Play { get; set; }

    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore] public string DisplayName => $"{Nickname} ({PokemonName})";
    public override string ToString() => DisplayName;
}

/// <summary>
/// State of a Pokémon during play. Stat changes are stored relative to the sheet's final stats so
/// they stay meaningful when the sheet is edited (level up, new points…).
/// </summary>
public sealed class SheetPlayState
{
    /// <summary>Current hit points (can be negative); null = full health.</summary>
    [JsonPropertyName("currentHp")] public int? CurrentHp { get; set; }
    /// <summary>
    /// Injuries of the Pokémon: from damage, massive damage and added by hand. Healing doesn't remove
    /// them (see <see cref="Rules.InjuryRules"/>). Before <see cref="InjuriesIncludeDamage"/>, only the
    /// massive damage / hand-added ones were stored and the others were derived from the current HP.
    /// </summary>
    [JsonPropertyName("injuries")] public int Injuries { get; set; }
    /// <summary>True once <see cref="Injuries"/> holds every injury (false for states saved by older versions).</summary>
    [JsonPropertyName("injuriesIncludeDamage")] public bool InjuriesIncludeDamage { get; set; }
    /// <summary>Temporary bonus (+) or malus (−) per stat (HP, ATK…), relative to the sheet's final stats.</summary>
    [JsonPropertyName("statModifiers")] public Dictionary<string, int> StatModifiers { get; set; } = new();
    /// <summary>Combat stages per stat (−6…+6): each one changes the stat by 20 % of its sheet value.</summary>
    [JsonPropertyName("combatStages")] public Dictionary<string, int> CombatStages { get; set; } = new();
    /// <summary>Temporary bonus (+) or malus (−) to the maximum hit points.</summary>
    [JsonPropertyName("maxHpModifier")] public int MaxHpModifier { get; set; }
    /// <summary>Moves temporarily unusable (disabled, sealed…).</summary>
    [JsonPropertyName("disabledMoves")] public List<string> DisabledMoves { get; set; } = new();
    /// <summary>Temporary bonus (+) or malus (−) added to skill rolls, per skill name.</summary>
    [JsonPropertyName("skillModifiers")] public Dictionary<string, int> SkillModifiers { get; set; } = new();
    /// <summary>Free notes written during the session, per section of the sheet (hp, stats, moves…).</summary>
    [JsonPropertyName("notes")] public Dictionary<string, string> Notes { get; set; } = new();

    public int Modifier(string stat) => StatModifiers.GetValueOrDefault(stat);
    public int CombatStage(string stat) => CombatStages.GetValueOrDefault(stat);
    public int SkillModifier(string skill) => SkillModifiers.GetValueOrDefault(skill);
    public bool IsMoveDisabled(string move) => DisabledMoves.Contains(move, StringComparer.OrdinalIgnoreCase);
    public string Note(string section) => Notes.GetValueOrDefault(section, "");

    /// <summary>Sheet maximum + temporary modifier (at least 1).</summary>
    public int EffectiveMaxHp(int sheetMaxHp) => Math.Max(1, sheetMaxHp + MaxHpModifier);

    /// <summary>Hit points with injuries applied (sheet max HP → temporary modifier → injuries).</summary>
    public Rules.HpStatus HpStatus(int sheetMaxHp)
    {
        var baseMax = EffectiveMaxHp(sheetMaxHp);
        var current = CurrentHp ?? baseMax;
        var injuries = Injuries;
        // Older saves: the injuries from damage were derived from the HP; keep them as stored injuries.
        if (!InjuriesIncludeDamage) injuries += Rules.InjuryRules.MarkersPassed(baseMax, sheetMaxHp, current);
        return Rules.InjuryRules.Create(baseMax, current, injuries, sheetMaxHp);
    }

    /// <summary>Clears every temporary effect (rest / end of the session). Injuries and notes are kept.</summary>
    public void Reset()
    {
        CurrentHp = null;
        StatModifiers.Clear();
        CombatStages.Clear();
        MaxHpModifier = 0;
        DisabledMoves.Clear();
        SkillModifiers.Clear();
    }
}

public sealed class TeamSlot
{
    [JsonPropertyName("cardId")] public string? CardId { get; set; }
    [JsonPropertyName("pokemonName")] public string PokemonName { get; set; } = "";
    [JsonPropertyName("nickname")] public string Nickname { get; set; } = "";
    [JsonPropertyName("level"), JsonConverter(typeof(LevelStringConverter))] public string Level { get; set; } = "";
    [JsonPropertyName("rarity")] public string Rarity { get; set; } = "normal";
    [JsonPropertyName("isMega")] public bool IsMega { get; set; }
}

public sealed class TeamRecord : IStoredRecord
{
    public const int Size = 6;

    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("selectedType")] public string SelectedType { get; set; } = "Fire";
    [JsonPropertyName("slots")] public List<TeamSlot?> Slots { get; set; } = Enumerable.Repeat<TeamSlot?>(null, Size).ToList();

    /// <summary>Desktop-only: trainer picture used for the PNG card (ignored by the website).</summary>
    [JsonPropertyName("trainerImagePath"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TrainerImagePath { get; set; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore] public string DisplayName => $"{Name} ({Slots.Count(s => s != null)}/{Size})";
    public override string ToString() => DisplayName;

    public void Normalize()
    {
        while (Slots.Count < Size) Slots.Add(null);
        if (Slots.Count > Size) Slots = Slots.Take(Size).ToList();
    }
}

public sealed class ZoneSections
{
    [JsonPropertyName("common")] public List<string> Common { get; set; } = new();
    [JsonPropertyName("uncommon")] public List<string> Uncommon { get; set; } = new();
    [JsonPropertyName("rare")] public List<string> Rare { get; set; } = new();
    [JsonPropertyName("superRare")] public List<string> SuperRare { get; set; } = new();

    public List<string> Get(ZoneTier tier) => tier switch
    {
        ZoneTier.Common => Common,
        ZoneTier.Uncommon => Uncommon,
        ZoneTier.Rare => Rare,
        _ => SuperRare,
    };
}

public enum ZoneTier { Common, Uncommon, Rare, SuperRare }

public sealed class ZoneRecord : IFolderRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>The website's "Save JSON" export uses <c>zoneName</c> instead of <c>name</c>.</summary>
    [JsonPropertyName("zoneName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ZoneName { get; set; }

    [JsonPropertyName("sections")] public ZoneSections Sections { get; set; } = new();
    /// <summary>Desktop-only: folder of the zone in the library, "/"-separated ("" = no folder).</summary>
    [JsonPropertyName("folder")] public string Folder { get; set; } = "";
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore] public string DisplayName => Name;
    public override string ToString() => Name;
}
