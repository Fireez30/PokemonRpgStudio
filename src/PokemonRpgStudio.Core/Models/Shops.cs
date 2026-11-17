using System.Text.Json;
using System.Text.Json.Serialization;
using PokemonRpgStudio.Core.Json;

namespace PokemonRpgStudio.Core.Models;

/// <summary>
/// One article of a shop. <see cref="Name"/>, <see cref="Quantity"/> and <see cref="Price"/> are mandatory;
/// <see cref="Picture"/> is optional: in the app's storage it's a file name in the shops' <c>images</c> folder,
/// in an exported file it's the picture itself as a <c>data:</c> URI. <see cref="Effect"/> is an optional free text
/// (what the item does); files written before it existed simply have none.
/// </summary>
public sealed class ShopItem
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string? Picture { get; set; }
    public string Effect { get; set; } = "";

    public ShopItem Clone() => new() { Name = Name, Quantity = Quantity, Price = Price, Picture = Picture, Effect = Effect };
}

public sealed class Shop
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<ShopItem> Items { get; set; } = new();

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed shop" : Name.Trim();
}

/// <summary>Every shop of the Tools section, saved as <c>shops.json</c>.</summary>
public sealed class ShopBook
{
    /// <summary>1: first format. 2: items have an <see cref="ShopItem.Effect"/> (optional when reading).</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<Shop> Shops { get; set; } = new();

    /// <summary>Fixes values a hand-edited file could break (missing lists, negative numbers, duplicate ids).</summary>
    public ShopBook Normalize()
    {
        Version = CurrentVersion;
        Shops ??= new();
        Shops.RemoveAll(s => s is null);
        var ids = new HashSet<string>();
        foreach (var shop in Shops)
        {
            shop.Name ??= "";
            if (string.IsNullOrWhiteSpace(shop.Id) || !ids.Add(shop.Id))
            {
                shop.Id = Guid.NewGuid().ToString("N");
                ids.Add(shop.Id);
            }
            shop.Items ??= new();
            shop.Items.RemoveAll(i => i is null);
            foreach (var item in shop.Items)
            {
                item.Name ??= "";
                item.Effect ??= "";
                item.Quantity = Math.Max(0, item.Quantity);
                item.Price = Math.Max(0, item.Price);
                if (string.IsNullOrWhiteSpace(item.Picture)) item.Picture = null;
            }
        }
        return this;
    }

    [JsonIgnore]
    public IEnumerable<ShopItem> AllItems => Shops.SelectMany(s => s.Items);

    public string ToJson() => JsonSerializer.Serialize(this, JsonDefaults.Options);

    public static ShopBook FromJson(string json) =>
        (JsonSerializer.Deserialize<ShopBook>(json, JsonDefaults.Options) ?? new ShopBook()).Normalize();
}
