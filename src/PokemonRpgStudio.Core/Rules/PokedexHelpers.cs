using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.Core.Rules;

public sealed record EvolutionStep(string Name, string Condition, PokemonSpecies? Match);

public sealed record MegaInfo(IReadOnlyList<string> Types, string Ability, IReadOnlyList<(string Label, string Value)> Stats);

public sealed record TypeCount(string Type, int Count);

public sealed record MoveTypeAnalysis(IReadOnlyList<TypeCount> Counts, int TotalMoves, bool UsedFallbackEggMoves);

/// <summary>Evolution parsing, mega-evolution parsing and analyzer logic from the Pokémon Sheet / Analyzer pages.</summary>
public static class PokedexHelpers
{
    private static readonly Regex EvolMarker = new(
        @"\b(?:\d+|Minimum|Lv\.?|Level|Evolves?|Trade|Stone|Friendship|Happiness|Loyalty|Holding|Learning|Learn|Knowing|If|When|With|Male|Female|Shed|See|Beauty|Affection)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Gender = new(@"\b(?:male|female)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>PDF extraction often swaps uppercase I ↔ lowercase l — normalise both before comparing.</summary>
    public static string NormLI(string s) => s.ToLowerInvariant().Replace('l', 'i');

    public static (string Name, string Condition) ParseEvolEntry(string? str)
    {
        var trimmed = (str ?? "").Trim();
        var m = EvolMarker.Match(trimmed);
        if (!m.Success) return (trimmed, "");
        int cut = m.Index;
        while (cut > 0 && char.IsWhiteSpace(trimmed[cut - 1])) cut--;
        if (cut > 0 && trimmed[cut - 1] == '(') cut--;
        var name = trimmed[..cut].Trim();
        var condition = Regex.Replace(trimmed[cut..].Trim(), @"^[,;]\s*", "");
        return (name, condition);
    }

    public static PokemonSpecies? FindEvolMatch(string name, GameData data)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return data.FindPokemon(name) ?? data.Pokemon.FirstOrDefault(p => NormLI(p.Name) == NormLI(name));
    }

    private static string? ExtractLevel(string condition)
    {
        var m = Regex.Match(condition ?? "", @"\d+");
        return m.Success ? m.Value : null;
    }

    public static bool AreSiblings(string prevCondition, string condition)
    {
        if (string.IsNullOrEmpty(prevCondition) || string.IsNullOrEmpty(condition)) return false;
        var prevLevel = ExtractLevel(prevCondition);
        var level = ExtractLevel(condition);
        if (prevLevel != null && level != null && prevLevel == level) return true;
        if (Gender.IsMatch(prevCondition) && Gender.IsMatch(condition)) return true;
        if (prevLevel == null && level == null)
        {
            var a = prevCondition.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            var b = condition.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            if (a == b) return true;
        }
        return false;
    }

    /// <summary>
    /// Groups the evolution list into stages; entries in the same group are alternative branches.
    /// Stray fragments of a wrapped entry are merged back into the previous entry.
    /// </summary>
    public static List<List<EvolutionStep>> BuildEvolutionGroups(IEnumerable<string> raw, GameData data)
    {
        var list = raw.Select(s => (s ?? "").Trim()).Where(s => s.Length > 0).ToList();
        if (list.Count == 0) return new();

        var merged = new List<string> { list[0] };
        for (int i = 1; i < list.Count; i++)
        {
            var (name, condition) = ParseEvolEntry(list[i]);
            bool isFragment = string.IsNullOrEmpty(name) || (condition.Length == 0 && FindEvolMatch(name, data) == null);
            if (isFragment) merged[^1] = merged[^1] + " " + list[i];
            else merged.Add(list[i]);
        }

        var parsed = merged.Select(r =>
        {
            var (n, c) = ParseEvolEntry(r);
            return new EvolutionStep(n, c, FindEvolMatch(n, data));
        }).ToList();

        var groups = new List<List<EvolutionStep>> { new() { parsed[0] } };
        for (int i = 1; i < parsed.Count; i++)
        {
            var prev = groups[^1];
            bool prevIsBase = groups.Count == 1;
            if (!prevIsBase && AreSiblings(prev[0].Condition, parsed[i].Condition)) prev.Add(parsed[i]);
            else groups.Add(new() { parsed[i] });
        }
        return groups;
    }

    /// <summary>Reads the mega-evolution block in either of its two formats (stats as object or text).</summary>
    public static MegaInfo? ParseMega(PokemonSpecies species)
    {
        var mega = species.MegaEvolution;
        if (mega is null) return null;

        var types = new List<string>();
        switch (mega["type"])
        {
            case JsonArray arr:
                types.AddRange(arr.Select(t => t?.ToString().Trim() ?? "").Where(t => t.Length > 0));
                break;
            case JsonValue v:
                var s = v.ToString().Trim();
                if (s.Length > 0) types.Add(s);
                break;
        }

        var ability = mega["ability"]?.ToString().Trim() ?? "";
        var stats = new List<(string, string)>();
        var statsNode = mega["stats"];
        if (statsNode is JsonObject obj)
        {
            var inner = obj["stats"] as JsonObject ?? obj;
            foreach (var kv in inner) stats.Add((kv.Key, kv.Value?.ToString().Trim() ?? ""));
        }
        else if (statsNode != null)
        {
            foreach (Match m in Regex.Matches(statsNode.ToString(), @"([+-]\d+)\s*([A-Za-z][A-Za-z. ]*?)(?=,|$|\s*[+-]\d)"))
                stats.Add((m.Groups[2].Value.Trim(), m.Groups[1].Value));
        }
        return new MegaInfo(types, ability, stats);
    }

    /// <summary>Number of moves per type over level-up, egg, TM and tutor moves (Pokémon Analyzer).</summary>
    public static MoveTypeAnalysis AnalyzeMoveTypes(PokemonSpecies species, GameData data)
    {
        var eggMoves = data.EffectiveEggMoves(species);
        var lists = new[]
        {
            Resolve(species.Moves, data),
            Resolve(eggMoves, data),
            Resolve(species.TmMoves, data),
            Resolve(species.TutorMoves, data),
        };

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        foreach (var list in lists)
            foreach (var move in list)
            {
                total++;
                foreach (var type in move.Types.Select(t => t.Trim()).Where(t => t.Length > 0))
                    counts[type] = counts.GetValueOrDefault(type) + 1;
            }

        var rows = counts.Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new TypeCount(kv.Key, kv.Value)).ToList();
        bool fallback = species.EggMoves.Count == 0 && eggMoves.Count > 0;
        return new MoveTypeAnalysis(rows, total, fallback);
    }

    // The analyzer counts every learnset entry that resolves (duplicates included), like the website.
    private static List<MoveData> Resolve(IEnumerable<LearnsetMove> learnset, GameData data) =>
        learnset.Select(m => data.FindMove(m.Name)).Where(m => m != null).Select(m => m!).ToList();
}
