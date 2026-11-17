using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.Core.Rules;

/// <summary>Problems of a sheet: what is missing (not finished) and what is wrong (not correct).</summary>
public sealed record SheetIssues(IReadOnlyList<string> Missing, IReadOnlyList<string> Errors)
{
    public static readonly SheetIssues None = new(Array.Empty<string>(), Array.Empty<string>());

    public bool IsUnfinished => Missing.Count > 0;
    public bool IsIncorrect => Errors.Count > 0;
    public bool HasIssues => IsUnfinished || IsIncorrect;
    public IEnumerable<string> All => Errors.Concat(Missing);

    /// <summary>"2 errors · not finished" (empty when the sheet is fine).</summary>
    public string Summary => string.Join(" · ", new[]
    {
        IsIncorrect ? $"{Errors.Count} error{(Errors.Count > 1 ? "s" : "")}" : null,
        IsUnfinished ? "not finished" : null,
    }.Where(x => x != null));
}

/// <summary>
/// Checks a sheet against the rules and the game data: required fields and points
/// (<see cref="SheetRules.MissingRequirements"/>), plus values the Sheet Editor wouldn't allow —
/// Pokémon missing from the Pokédex, abilities the species doesn't have, moves it can't learn or
/// missing from moves.json, invalid rarity / card / gender, level above the maximum.
/// Timing is never an error: a Pokémon can get a move or an ability before the usual level
/// (tutor mechanic), so only "can it have it at all" is checked.
/// </summary>
public static class SheetValidation
{
    public static SheetIssues Check(SheetState s, GameData data)
    {
        var missing = SheetRules.MissingRequirements(s);
        var errors = new List<string>();
        var species = data.FindPokemon(s.ChosenPokemon);

        if (!string.IsNullOrEmpty(s.ChosenPokemon) && species is null)
            errors.Add($"\"{s.ChosenPokemon}\" is not in the Pokédex");
        if (s.Level > SheetRules.MaxLevel) errors.Add($"Level {SheetRules.FormatLevel(s.Level)} is above the maximum ({SheetRules.MaxLevel})");
        CheckValue(errors, "rarity", s.Rarity, SheetRules.Rarities);
        CheckValue(errors, "card", s.Card, SheetRules.Cards);
        CheckValue(errors, "gender", s.Gender, SheetRules.Genders);
        if (s.PointsByStat.Values.Any(v => v < 0)) errors.Add("Negative level-up points");

        if (species != null)
        {
            if (s.Gender != "" && species.IsGenderless != (s.Gender == "Genderless"))
                errors.Add(species.IsGenderless ? $"{species.DisplayName} is genderless" : $"{species.DisplayName} can't be genderless");
            CheckAbilities(errors, s, species);
            CheckMoves(errors, s, species, data);
        }
        return missing.Count == 0 && errors.Count == 0 ? SheetIssues.None : new SheetIssues(missing, errors);
    }

    private static void CheckValue(List<string> errors, string what, string value, string[] allowed)
    {
        if (value != "" && !allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Unknown {what} \"{value}\"");
    }

    private static void CheckAbilities(List<string> errors, SheetState s, PokemonSpecies sp)
    {
        // Unlock levels (20 / 40 / 10) are not checked: tutors can teach an ability early.
        void Tier(string label, List<string> chosen, IEnumerable<string> allowed)
        {
            var names = chosen.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            if (names.Count == 0) return;
            var allowedSet = allowed.Select(Key).ToHashSet();
            var wrong = names.Where(n => !allowedSet.Contains(Key(n))).ToList();
            if (wrong.Count > 0)
                errors.Add($"{label} abilit{(wrong.Count > 1 ? "ies" : "y")} {sp.DisplayName} doesn't have: {string.Join(", ", wrong)}");
        }

        Tier("Base", s.BaseAbilities, sp.BaseAbilities.Select(a => a.Name));
        Tier("Advanced", s.AdvancedAbilities, sp.AdvancedAbilities.Select(a => a.Name));
        Tier("High", s.HighAbilities, sp.HighAbilities.Select(a => a.Name));
        Tier("Custom", s.CustomAbilities, (sp.CustomAbilities ?? new()).Values.Select(a => a.Name));
    }

    /// <summary>
    /// The species and its earlier forms ("Snivy", "Servine Minimum 17", "Serperior Minimum 36"):
    /// a Pokémon keeps what it could learn before evolving.
    /// </summary>
    public static List<PokemonSpecies> Line(PokemonSpecies sp, GameData data)
    {
        var line = new List<PokemonSpecies>();
        foreach (var entry in sp.Evolutions)
        {
            var name = entry.Split(" Minimum", 2, StringSplitOptions.TrimEntries)[0].Trim();
            if (Key(name) == Key(sp.Name)) break;
            if (data.FindPokemon(name) is { } form && !line.Contains(form)) line.Add(form);
        }
        line.Add(sp);
        return line;
    }

    private static void CheckMoves(List<string> errors, SheetState s, PokemonSpecies sp, GameData data)
    {
        var line = Line(sp, data);
        HashSet<string> Names(Func<PokemonSpecies, IEnumerable<LearnsetMove>> pick) =>
            line.SelectMany(pick).Select(m => Key(m.Name)).ToHashSet();
        var egg = line.SelectMany(f => data.EffectiveEggMoves(f)).Select(m => Key(m.Name)).ToHashSet();
        var tm = Names(f => f.TmMoves);
        var tutor = Names(f => f.TutorMoves);
        var evo = Names(f => f.EvoMoves ?? new());
        var levelUp = Names(f => f.Moves);

        // Level-up list: anything the line can learn by any method (moves picked up before
        // evolving, egg / TM / tutor moves added there). The level isn't checked: a level-up move
        // can be taught early with a tutor.
        var notLearnable = s.ChosenMoves
            .Where(m => !new[] { levelUp, egg, tm, tutor, evo }.Any(set => set.Contains(Key(m)))).ToList();
        if (notLearnable.Count > 0) errors.Add($"Moves {sp.DisplayName} can't learn: {string.Join(", ", notLearnable)}");

        void List(string label, List<string> chosen, List<string> locked, HashSet<string> allowed)
        {
            var wrong = chosen.Where(m => !allowed.Contains(Key(m))).ToList();
            if (wrong.Count > 0) errors.Add($"{label} moves {sp.DisplayName} can't learn: {string.Join(", ", wrong)}");
            var chosenSet = chosen.Select(Key).ToHashSet();
            var orphans = locked.Where(m => !chosenSet.Contains(Key(m))).ToList();
            if (orphans.Count > 0) errors.Add($"Locked {label} moves not in the {label} list: {string.Join(", ", orphans)}");
        }

        List("Egg", s.EggMoves, s.LockedEggMoves, egg);
        List("TM", s.TmMoves, s.LockedTmMoves, tm);
        List("Tutor", s.TutorMoves, s.LockedTutorMoves, tutor);

        var unknown = s.ChosenMoves.Concat(s.EggMoves).Concat(s.TmMoves).Concat(s.TutorMoves)
            .Where(m => !string.IsNullOrWhiteSpace(m) && data.FindMove(m) is null)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0) errors.Add($"Moves missing from moves.json: {string.Join(", ", unknown)}");
    }

    /// <summary>Learnset names may carry a ":" or extra spaces ("Tackle:").</summary>
    private static string Key(string name) => name.Replace(":", "").Trim().ToLowerInvariant();
}
