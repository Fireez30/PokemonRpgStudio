using System.Globalization;
using System.Text;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.Core.Storage;

/// <summary>
/// Free-text search over a sheet: nickname, Pokémon, types, moves, abilities, nature, rarity,
/// card, gender, status, level and notes (sheet notes and session notes). Every word of the query
/// must be found (in any field); case and accents are ignored ("pokemon" finds "Pokémon").
/// </summary>
public static class SheetSearch
{
    /// <summary>All searchable text of a sheet, normalized. <paramref name="species"/> adds its types.</summary>
    public static string BuildIndex(SheetRecord record, PokemonSpecies? species)
    {
        var s = record.State;
        var parts = new List<string?>
        {
            record.Nickname, record.PokemonName, s.ChosenPokemon, species?.Name,
            record.Status, s.Rarity, s.Card, s.Gender,
            SheetRules.NatureName(s.BuffedStat, s.LoweredStat),
            s.Level > 0 ? "lv" + SheetRules.FormatLevel(s.Level) : null,
            record.Notes,
            record.Folder,
        };
        if (species != null) parts.AddRange(species.PokemonTypes);
        parts.AddRange(s.ChosenMoves);
        parts.AddRange(s.EggMoves);
        parts.AddRange(s.TmMoves);
        parts.AddRange(s.TutorMoves);
        parts.AddRange(s.BaseAbilities);
        parts.AddRange(s.AdvancedAbilities);
        parts.AddRange(s.HighAbilities);
        parts.AddRange(s.CustomAbilities);
        parts.AddRange(s.ExtraAbilities);
        if (record.Play != null) parts.AddRange(record.Play.Notes.Values);
        return Normalize(string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p))));
    }

    /// <summary>True when every word of <paramref name="query"/> appears in the index.</summary>
    public static bool Matches(string index, string query)
    {
        var terms = Terms(query);
        return terms.Count == 0 || terms.All(index.Contains);
    }

    /// <summary>Same as <see cref="Matches(string, string)"/> with the query already split by <see cref="Terms"/>
    /// (filtering many sheets: the query is normalized once).</summary>
    public static bool Matches(string index, IReadOnlyList<string> terms) =>
        terms.Count == 0 || terms.All(index.Contains);

    public static IReadOnlyList<string> Terms(string query) =>
        Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Lower case without accents.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
