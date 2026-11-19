using System.Text.Json;
using System.Text.Json.Serialization;
using PokemonRpgStudio.Core.Json;

namespace PokemonRpgStudio.Core.Models;

/// <summary>
/// One entry of the item wiki. <see cref="Section"/> is the name of one of the wiki's sections, or empty for
/// "Unsorted". <see cref="Picture"/> is a file name in the wiki's <c>images</c> folder.
/// </summary>
public sealed class WikiItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Section { get; set; } = "";
    public string Effect { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Picture { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed item" : Name.Trim();
}

/// <summary>The item wiki of the Tools menu, saved as <c>wiki/items.json</c>: user sections and items.</summary>
public sealed class ItemWikiBook
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    /// <summary>The sections, in display order ("Unsorted" is implicit, not listed).</summary>
    public List<string> Sections { get; set; } = new();
    public List<WikiItem> Items { get; set; } = new();

    public static string KeyOf(string? name) => (name ?? "").Trim().ToUpperInvariant();

    /// <summary>The section of the list matching <paramref name="name"/> (ignoring case), or null.</summary>
    public string? FindSection(string? name)
    {
        var key = KeyOf(name);
        return key.Length == 0 ? null : Sections.FirstOrDefault(s => KeyOf(s) == key);
    }

    public WikiItem? FindItem(string? name)
    {
        var key = KeyOf(name);
        return key.Length == 0 ? null : Items.FirstOrDefault(i => KeyOf(i.Name) == key);
    }

    /// <summary>Fixes values a hand-edited file could break (missing lists, duplicates, unknown sections).</summary>
    public ItemWikiBook Normalize()
    {
        Version = CurrentVersion;
        Sections ??= new();
        Items ??= new();
        var sections = new List<string>();
        foreach (var s in Sections)
        {
            var name = s?.Trim() ?? "";
            if (name.Length > 0 && !sections.Any(x => KeyOf(x) == KeyOf(name))) sections.Add(name);
        }
        Sections = sections;
        Items.RemoveAll(i => i is null);
        var ids = new HashSet<string>();
        foreach (var item in Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
                ids.Add(item.Id);
            }
            item.Name ??= "";
            item.Effect ??= "";
            item.Description ??= "";
            if (string.IsNullOrWhiteSpace(item.Picture)) item.Picture = null;
            // An item keeps its section even when the list lost it: the section comes back.
            var section = item.Section?.Trim() ?? "";
            if (section.Length > 0)
            {
                var known = FindSection(section);
                if (known is null) Sections.Add(known = section);
                section = known;
            }
            item.Section = section;
        }
        return this;
    }

    /// <summary>
    /// Adds to the wiki ("Unsorted") every named shop item it doesn't have yet (same name, ignoring case),
    /// with the shop's effect and picture. <paramref name="copyPicture"/> copies a shop picture file into
    /// the wiki's folder and returns its new name (null when it can't). Returns the added items.
    /// </summary>
    public List<WikiItem> AddShopItems(ShopBook shops, Func<string, string?> copyPicture)
    {
        var added = new List<WikiItem>();
        foreach (var group in shops.AllItems.Where(i => !string.IsNullOrWhiteSpace(i.Name)).GroupBy(i => KeyOf(i.Name)))
        {
            if (FindItem(group.Key) is not null) continue;
            var first = group.First();
            string? picture = null;
            if (group.FirstOrDefault(i => i.Picture is not null)?.Picture is { } source)
            {
                try { picture = copyPicture(source); }
                catch (Exception) { /* the item is added without its picture */ }
            }
            var item = new WikiItem
            {
                Name = first.Name.Trim(),
                Effect = group.Select(i => i.Effect?.Trim() ?? "").FirstOrDefault(e => e.Length > 0) ?? "",
                Picture = picture,
            };
            Items.Add(item);
            added.Add(item);
        }
        return added;
    }

    /// <summary>Removes a section; its items go back to "Unsorted".</summary>
    public void RemoveSection(string section)
    {
        var key = KeyOf(section);
        Sections.RemoveAll(s => KeyOf(s) == key);
        foreach (var item in Items.Where(i => KeyOf(i.Section) == key)) item.Section = "";
    }

    /// <summary>Renames a section and moves its items. False when the new name is empty or taken by another section.</summary>
    public bool RenameSection(string section, string newName)
    {
        newName = newName?.Trim() ?? "";
        int index = Sections.FindIndex(s => KeyOf(s) == KeyOf(section));
        if (index < 0 || newName.Length == 0) return false;
        if (FindSection(newName) is { } other && KeyOf(other) != KeyOf(section)) return false;
        var old = Sections[index];
        Sections[index] = newName;
        foreach (var item in Items.Where(i => KeyOf(i.Section) == KeyOf(old))) item.Section = newName;
        return true;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonDefaults.Options);

    public static ItemWikiBook FromJson(string json) =>
        (JsonSerializer.Deserialize<ItemWikiBook>(json, JsonDefaults.Options) ?? new ItemWikiBook()).Normalize();
}
