using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.Core.Rules;

/// <summary>
/// Character-sheet rules of the tabletop Pokémon RPG, ported from the website's
/// <c>PokemonGenerator.jsx</c>.
/// </summary>
public static class SheetRules
{
    public const int MaxLevel = 150;
    /// <summary>Experience of a sheet goes from 0 to 100.</summary>
    public const int MaxExperience = 100;
    /// <summary>Rows of the first move table; a separator splits the 6th and 7th move.</summary>
    public const int FirstTableMoves = 6;
    /// <summary>Minimum number of rows of the exported move table (empty rows are added).</summary>
    public const int MinSheetRows = 9;
    public const int NatureRolls = 3;
    public const int AdvancedAbilityLevel = 20;
    public const int HighAbilityLevel = 40;
    public const int CustomAbilityLevel = 10;
    public const int MaxDbBuff = 20;

    public static readonly string[] Rarities = { "Normal", "Shiny", "Platine" };
    public static readonly string[] Cards = { "None", "Normal", "Shiny", "Platine" };
    public static readonly string[] Genders = { "Genderless", "Male", "Female" };
    /// <summary>Usage status of a sheet in the player's roster.</summary>
    public static readonly string[] Statuses = { "Unavailable", "To delete", "Unused", "Reserve", "Secondary", "Main" };
    public const string DefaultStatus = "Unused";
    public static readonly Stat[] AllStats = { Stat.HP, Stat.ATK, Stat.DEF, Stat.SPATK, Stat.SPDEF, Stat.SPD };
    public static readonly string[] StatNames = AllStats.Select(s => s.ToString()).ToArray();

    public static readonly string[] AllTypes =
    {
        "Normal", "Fire", "Water", "Electric", "Grass", "Ice", "Fighting", "Poison", "Ground", "Flying",
        "Psychic", "Bug", "Rock", "Ghost", "Dragon", "Dark", "Steel", "Fairy", "Sound", "Light",
    };

    private static readonly Dictionary<string, Dictionary<string, string>> NatureMatrix = new()
    {
        ["HP"] = new() { ["ATK"] = "Cuddly", ["DEF"] = "Distracted", ["SPATK"] = "Proud", ["SPDEF"] = "Decisive", ["SPD"] = "Patient", ["HP"] = "Neutral" },
        ["ATK"] = new() { ["HP"] = "Desperate", ["DEF"] = "Lonely", ["SPATK"] = "Adamant", ["SPDEF"] = "Naughty", ["SPD"] = "Brave", ["ATK"] = "Neutral" },
        ["DEF"] = new() { ["HP"] = "Stark", ["ATK"] = "Bold", ["SPATK"] = "Impish", ["SPDEF"] = "Lax", ["SPD"] = "Relaxed", ["DEF"] = "Neutral" },
        ["SPATK"] = new() { ["HP"] = "Curious", ["ATK"] = "Modest", ["DEF"] = "Mild", ["SPDEF"] = "Rash", ["SPD"] = "Quiet", ["SPATK"] = "Neutral" },
        ["SPDEF"] = new() { ["HP"] = "Dreamy", ["ATK"] = "Calm", ["DEF"] = "Gentle", ["SPATK"] = "Careful", ["SPD"] = "Sassy", ["SPDEF"] = "Neutral" },
        ["SPD"] = new() { ["HP"] = "Skittish", ["ATK"] = "Timid", ["DEF"] = "Hasty", ["SPATK"] = "Jolly", ["SPDEF"] = "Naive", ["SPD"] = "Neutral" },
    };

    /// <summary>Damage base → roll table.</summary>
    public static readonly IReadOnlyDictionary<int, string> DamageBaseToRoll = new Dictionary<int, string>
    {
        [1] = "1d6+1", [2] = "1d6+3", [3] = "1d6+5", [4] = "1d8+6", [5] = "1d8+8", [6] = "2d6+8", [7] = "2d6+10",
        [8] = "2d8+10", [9] = "2d10+10", [10] = "3d8+10", [11] = "3d10+10", [12] = "3d12+10", [13] = "4d10+10",
        [14] = "4d10+15", [15] = "4d10+20", [16] = "5d10+20", [17] = "5d12+25", [18] = "6d12+25", [19] = "6d12+30",
        [20] = "6d12+35", [21] = "6d12+40", [22] = "6d12+45", [23] = "6d12+50", [24] = "6d12+55", [25] = "6d12+60",
        [26] = "7d12+65", [27] = "8d12+70", [28] = "8d12+80",
    };

    /// <summary>Roll → damage base (only the rolls listed by the website).</summary>
    public static readonly IReadOnlyDictionary<string, int> RollToDamageBase = new Dictionary<string, int>
    {
        ["1d6+1"] = 1, ["1d6+3"] = 2, ["1d6+5"] = 3, ["1d8+6"] = 4, ["1d8+8"] = 5, ["2d6+8"] = 6, ["2d6+10"] = 7,
        ["2d8+10"] = 8, ["2d10+10"] = 9, ["3d8+10"] = 10, ["3d10+10"] = 11, ["3d12+10"] = 12, ["4d10+10"] = 13,
        ["4d10+15"] = 14, ["4d10+20"] = 15, ["5d10+20"] = 16, ["6d12+25"] = 18, ["6d12+35"] = 20, ["6d12+60"] = 25,
    };

    public static string NatureName(string buffed, string lowered)
    {
        if (!NatureMatrix.TryGetValue(buffed, out var row) || !row.TryGetValue(lowered, out var name)) return "";
        return $"{name} (+{buffed}/-{lowered})";
    }

    /// <summary>Stats of a nature from its name ("Timid" → SPD/ATK). Neutral natures are ambiguous: false.</summary>
    public static bool TryNatureStats(string name, out string buffed, out string lowered)
    {
        foreach (var (b, row) in NatureMatrix)
            foreach (var (l, n) in row)
                if (b != l && n.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    (buffed, lowered) = (b, l);
                    return true;
                }
        (buffed, lowered) = ("", "");
        return false;
    }

    /// <summary>Stat key from the spellings found in sheets: "Sp. Atk", "SP.DEF", "Speed", "spe"… (null when unknown).</summary>
    public static string? NormalizeStatKey(string text)
    {
        var k = new string(text.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        return k switch
        {
            "HP" or "PV" => "HP",
            "ATK" or "ATTACK" => "ATK",
            "DEF" or "DEFENSE" or "DEFENCE" => "DEF",
            "SPATK" or "SPA" or "SPATTACK" or "SPECIALATTACK" => "SPATK",
            "SPDEF" or "SPDEFENSE" or "SPDEFENCE" or "SPECIALDEFENSE" => "SPDEF",
            "SPD" or "SPE" or "SPEED" or "VIT" => "SPD",
            _ => null,
        };
    }

    public static string TierSymbol(string? value) => value switch
    {
        "Normal" => "●",
        "Shiny" => "✦",
        "Platine" => "◆",
        "None" => "–",
        _ => "",
    };

    public static string GenderSymbol(string? value) => value switch
    {
        "Male" => "♂",
        "Female" => "♀",
        "Genderless" => "⚲",
        _ => "",
    };

    /// <summary>Completed levels: a fractional level (partial XP) only counts once it reaches the next whole level.</summary>
    public static int WholeLevel(double level) => (int)Math.Floor(Math.Max(0, level) + 1e-9);

    /// <summary>Level for display: "12", "12.5", "12.25" (invariant culture, at most 2 decimals).</summary>
    public static string FormatLevel(double level) => Math.Round(level, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    public static int AvailablePoints(double level, IReadOnlyDictionary<string, int> pointsByStat) =>
        9 + WholeLevel(level) - pointsByStat.Values.Sum();

    public static int HitPoints(double level, int finalHp) => WholeLevel(level) + 3 * finalHp + 10;
    /// <summary>Tutor points earned by level: one every 5 levels (0 below level 5).</summary>
    public static int TutorPoints(double level) => WholeLevel(level) / 5;
    /// <summary>Tutor points of a sheet: earned by level + added / removed by hand (never below 0).</summary>
    public static int TutorPoints(double level, int adjustment) => Math.Max(0, TutorPoints(level) + adjustment);
    public static int Evade(int stat) => (int)Math.Floor(stat / 10.0);

    /// <summary>Combat stages go from −6 to +6.</summary>
    public const int MaxCombatStages = 6;

    /// <summary>
    /// Stat change from combat stages: 20 % of the stat per stage, rounded to the nearest
    /// (20 ATK at +1 CS → +4, 13 at +1 → +3, 12 at −1 → −2).
    /// </summary>
    public static int CombatStageBonus(int stat, int stages) =>
        (int)Math.Round(stat * Math.Clamp(stages, -MaxCombatStages, MaxCombatStages) / 5.0, MidpointRounding.AwayFromZero);

    private static int CardBonus(string card) => card switch
    {
        "Normal" => 1,
        "Shiny" => 2,
        "Platine" => 3,
        _ => 0,
    };

    private static int RarityBonus(string rarity) => rarity switch
    {
        "Shiny" => 2,
        "Platine" => 4,
        _ => 0,
    };

    /// <summary>
    /// Automatic stat modifiers from card, rarity and nature.
    /// Every stat gets card bonus + rarity bonus; the lowered stat loses 2 and the buffed
    /// stat gains 2 (raised by the card tier when the nature isn't neutral).
    /// </summary>
    public static Dictionary<string, int> ComputeStatsBuff(string card, string rarity, string buffedStat, string loweredStat, int bonusPointNatureStat = 2)
    {
        int toAdd = CardBonus(card) + RarityBonus(rarity);
        var buffs = StatNames.ToDictionary(s => s, _ => toAdd);

        if (buffs.ContainsKey(loweredStat)) buffs[loweredStat] -= bonusPointNatureStat;

        if (loweredStat != buffedStat)
        {
            // Same values as the website: Normal → +2, Shiny → +3, Platine → set to 4.
            switch (card)
            {
                case "Normal": bonusPointNatureStat += 2; break;
                case "Shiny": bonusPointNatureStat += 3; break;
                case "Platine": bonusPointNatureStat = 4; break;
            }
        }

        if (buffs.ContainsKey(buffedStat)) buffs[buffedStat] += bonusPointNatureStat;
        return buffs;
    }

    public static Dictionary<string, int> ComputeFinalStats(PokemonSpecies species, string card, string rarity, string buffedStat, string loweredStat, IReadOnlyDictionary<string, int> pointsByStat)
    {
        var buffs = ComputeStatsBuff(card, rarity, buffedStat, loweredStat);
        var result = new Dictionary<string, int>();
        foreach (var stat in AllStats)
        {
            var name = stat.ToString();
            result[name] = Math.Max(0, species.BaseStat(stat) + buffs[name] + pointsByStat.GetValueOrDefault(name));
        }
        return result;
    }

    /// <summary>
    /// Distributes points proportionally to the base stats, then hands out the remainder
    /// randomly with the same weights (website "Auto" button).
    /// </summary>
    public static Dictionary<string, int> ComputePointsAutomatically(PokemonSpecies species, int availablePoints, Random? rng = null)
    {
        rng ??= Random.Shared;
        var result = StatNames.ToDictionary(s => s, _ => 0);
        if (availablePoints <= 0) return result;

        var baseStats = AllStats.Select(species.BaseStat).ToArray();
        double sum = baseStats.Sum();
        if (sum <= 0)
        {
            for (int i = 0; i < availablePoints; i++) result[StatNames[rng.Next(StatNames.Length)]]++;
            return result;
        }

        var weights = baseStats.Select(b => b / sum).ToArray();
        int toGive = availablePoints;
        for (int i = 0; i < 5; i++)
        {
            int pts = (int)Math.Round(availablePoints * weights[i], MidpointRounding.AwayFromZero);
            pts = Math.Max(0, Math.Min(toGive, pts));
            result[StatNames[i]] = pts;
            toGive -= pts;
        }
        int spd = Math.Max(0, Math.Min(toGive, (int)Math.Round(availablePoints * weights[5], MidpointRounding.AwayFromZero)));
        result[StatNames[5]] = spd;
        toGive -= spd;

        // Cumulative thresholds (0..100), like the website.
        var thresholds = new int[6];
        int acc = 0;
        for (int i = 0; i < 6; i++)
        {
            acc += (int)Math.Round(weights[i] * 100, MidpointRounding.AwayFromZero);
            thresholds[i] = i == 5 ? Math.Min(100, acc) : acc;
        }
        int guard = 0;
        while (toGive > 0)
        {
            int roll = rng.Next(0, 100);
            int idx = Array.FindIndex(thresholds, t => roll < t);
            if (idx < 0)
            {
                // Rounding left a gap above the last threshold; avoid an endless loop.
                if (++guard > 10_000) idx = Array.IndexOf(weights, weights.Max());
                else continue;
            }
            result[StatNames[idx]]++;
            toGive--;
        }
        return result;
    }

    public static (string buffed, string lowered) RollNature(Random? rng = null)
    {
        rng ??= Random.Shared;
        return (StatNames[rng.Next(6)], StatNames[rng.Next(6)]);
    }

    public static string RollGender(PokemonSpecies species, Random? rng = null)
    {
        if (species.IsGenderless) return "Genderless";
        rng ??= Random.Shared;
        double male = species.GenderRatioM ?? 50;
        return rng.Next(0, 100) < male ? "Male" : "Female";
    }

    /// <summary>Does the Pokémon get STAB on this move?</summary>
    public static bool HasStab(PokemonSpecies species, MoveData move)
    {
        var pokeTypes = species.PokemonTypes.Select(t => t.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return move.Types.Any(t => pokeTypes.Contains(t.Trim()));
    }

    public static bool IsStatusMove(MoveData move) =>
        string.Equals((move.MoveClass ?? "").Trim(), "status", StringComparison.OrdinalIgnoreCase);

    /// <summary>A manual damage base bonus can't be given to status moves.</summary>
    public static bool CanHaveDbBuff(MoveData move) => !IsStatusMove(move);

    /// <summary>
    /// Final damage roll of a move for this Pokémon: damage base + STAB (+2) + manual DB buff,
    /// converted back to dice. Moves without damage keep their roll text. A DB buff stored for a
    /// status move is ignored.
    /// </summary>
    public static string UpdatedRoll(PokemonSpecies species, IReadOnlyDictionary<string, int> dbBuffByMove, MoveData move)
    {
        var roll = move.Roll ?? "";
        if (move.DamageBase <= 0 && string.IsNullOrEmpty(roll)) return roll;

        int db = CanHaveDbBuff(move) && dbBuffByMove.TryGetValue(move.Name, out var buff) ? buff : 0;
        int moveDb;
        if (move.DamageBase > 0)
        {
            moveDb = move.DamageBase;
        }
        else
        {
            var baseRoll = roll.Contains('/') ? roll.Split('/')[0] : roll;
            if (!RollToDamageBase.TryGetValue(baseRoll.Trim(), out moveDb)) return roll;
        }

        if (HasStab(species, move)) db += 2;

        var finalDb = Math.Clamp(moveDb + db, 1, DamageBaseToRoll.Keys.Max());
        var finalRoll = DamageBaseToRoll[finalDb];
        return finalRoll.Contains('/') ? finalRoll.Split('/')[0] : finalRoll;
    }

    /// <summary>
    /// Details of a move stored by name on a sheet: a copy of the moves.json entry, or — for a move
    /// missing from moves.json — a placeholder typed from the species learnset.
    /// </summary>
    public static MoveData ResolveMove(string name, PokemonSpecies? species, Func<string, MoveData?> findMove)
    {
        var found = findMove(name);
        if (found != null) return found.Clone();
        var placeholder = MoveData.Placeholder(name.Trim());
        var learnset = species?.Moves.Concat(species.TmMoves).Concat(species.TutorMoves).Concat(species.EggMoves)
            .Concat(species.EvoMoves ?? new List<LearnsetMove>())
            .FirstOrDefault(m => m.Name.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (learnset != null) placeholder.Types = learnset.AllTypes.Select(t => t.Trim()).ToList();
        return placeholder;
    }

    /// <summary>
    /// Moves printed on the sheet: every chosen level-up move first (no limit), then every locked
    /// egg / TM / tutor move. Details come from <paramref name="findMove"/> (moves.json); rolls are
    /// updated with STAB and DB buffs.
    /// </summary>
    public static List<MoveData> PickMoves(SheetState state, PokemonSpecies species, Func<string, MoveData?> findMove)
    {
        var names = new List<string>();
        foreach (var name in state.ChosenMoves.Concat(state.LockedEggMoves).Concat(state.LockedTmMoves).Concat(state.LockedTutorMoves))
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);

        var output = names.Select(n => ResolveMove(n, species, findMove)).ToList();
        foreach (var move in output)
            move.Roll = UpdatedRoll(species, state.DbBuffByMove, move);
        return output;
    }

    /// <summary>
    /// Stat added to a move's damage roll: SP.ATK for special moves, ATK for physical moves,
    /// none for status (or unknown) moves.
    /// </summary>
    public static string? DamageStat(MoveData move) => (move.MoveClass ?? "").Trim().ToLowerInvariant() switch
    {
        "physical" => "ATK",
        "special" => "SPATK",
        _ => null,
    };

    /// <summary>Accuracy rolls of 20 are critical hits.</summary>
    public const int CriticalAccuracy = 20;

    public static string FormatClass(string? moveClass) => (moveClass ?? "").Trim().ToLowerInvariant() switch
    {
        "physical" => "Phys",
        "special" => "Spec",
        "status" => "Status",
        _ => moveClass ?? "",
    };

    public static int TotalLockedMoves(SheetState s) => s.LockedEggMoves.Count + s.LockedTmMoves.Count + s.LockedTutorMoves.Count;


    /// <summary>Clears abilities that are no longer allowed at this level (website <c>setLevel</c>).</summary>
    public static void ApplyLevelConstraints(SheetState s)
    {
        if (s.Level < HighAbilityLevel) s.HighAbilities.Clear();
        if (s.Level < AdvancedAbilityLevel) s.AdvancedAbilities.Clear();
    }

    /// <summary>Level-up moves the species knows at this level, sorted by level.</summary>
    public static List<LearnsetMove> AvailableLevelUpMoves(PokemonSpecies species, double level) =>
        level <= 0
            ? new List<LearnsetMove>()
            : species.Moves.Where(m => m.Level <= level).OrderBy(m => m.Level).ToList();

    /// <summary>True when every required field is set and all level-up points are spent (overspending is allowed).</summary>
    public static bool IsComplete(SheetState s) =>
        s.Level > 0 && s.Rarity != "" && s.Card != "" && s.Gender != "" && s.BuffedStat != "" && s.LoweredStat != ""
        && AvailablePoints(s.Level, s.PointsByStat) <= 0;

    /// <summary>Returns the human-readable reasons why a sheet can't be exported yet.</summary>
    public static List<string> MissingRequirements(SheetState s)
    {
        var missing = new List<string>();
        if (string.IsNullOrEmpty(s.ChosenPokemon)) missing.Add("Pick a Pokémon");
        if (s.Level <= 0) missing.Add("Set a level");
        if (s.Rarity == "") missing.Add("Pick a rarity");
        if (s.Card == "") missing.Add("Pick a card");
        if (s.Gender == "") missing.Add("Pick or roll a gender");
        if (s.BuffedStat == "" || s.LoweredStat == "") missing.Add("Pick or roll a nature");
        var available = AvailablePoints(s.Level, s.PointsByStat);
        if (available > 0) missing.Add($"Spend {available} level-up point(s)");
        return missing;
    }

    /// <summary>Starts a new sheet for a species, with the website defaults.</summary>
    public static SheetState NewSheet(PokemonSpecies species) => new()
    {
        ChosenPokemon = species.Name,
        Level = 1,
        Rarity = "Normal",
        Card = "None",
        Gender = species.IsGenderless ? "Genderless" : "",
        RemainingRolls = NatureRolls,
    };
}
