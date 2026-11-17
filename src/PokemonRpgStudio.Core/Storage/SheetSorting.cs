using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.Core.Storage;

public enum SheetSortKey { Nickname, Pokemon, Level, Rarity, Updated, Created }

/// <summary>Sort orders of the Sheets list. Ties are broken by nickname, then Pokémon.</summary>
public static class SheetSorting
{
    public static readonly IReadOnlyList<(SheetSortKey Key, string Label)> Options = new[]
    {
        (SheetSortKey.Nickname, "Nickname"),
        (SheetSortKey.Pokemon, "Pokémon"),
        (SheetSortKey.Level, "Level"),
        (SheetSortKey.Rarity, "Rarity"),
        (SheetSortKey.Updated, "Last updated"),
        (SheetSortKey.Created, "Created"),
    };

    /// <summary>Direction used when a sort is picked: highest levels, rarest and newest sheets first.</summary>
    public static bool DefaultDescending(SheetSortKey key) =>
        key is SheetSortKey.Level or SheetSortKey.Rarity or SheetSortKey.Updated or SheetSortKey.Created;

    private static int RarityRank(string? rarity) => (rarity ?? "").Trim().ToLowerInvariant() switch
    {
        "platine" => 2,
        "shiny" => 1,
        _ => 0,
    };

    public static IEnumerable<SheetRecord> Sort(IEnumerable<SheetRecord> sheets, SheetSortKey key, bool descending)
    {
        var cmp = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<SheetRecord> ordered = key switch
        {
            SheetSortKey.Pokemon => Order(sheets, s => s.PokemonName, descending, cmp),
            SheetSortKey.Level => Order(sheets, s => s.State.Level, descending),
            SheetSortKey.Rarity => Order(sheets, s => RarityRank(s.State.Rarity), descending),
            SheetSortKey.Updated => Order(sheets, s => s.UpdatedAt, descending),
            SheetSortKey.Created => Order(sheets, s => s.CreatedAt, descending),
            _ => Order(sheets, s => s.Nickname, descending, cmp),
        };
        return ordered.ThenBy(s => s.Nickname, cmp).ThenBy(s => s.PokemonName, cmp);
    }

    private static IOrderedEnumerable<SheetRecord> Order<T>(IEnumerable<SheetRecord> sheets, Func<SheetRecord, T> key, bool descending, IComparer<T>? comparer = null) =>
        descending ? sheets.OrderByDescending(key, comparer) : sheets.OrderBy(key, comparer);
}
