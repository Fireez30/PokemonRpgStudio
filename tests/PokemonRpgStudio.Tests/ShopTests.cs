using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.Tests;

public class ShopTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shop-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }

    private ShopStore NewStore(string name) => new(Path.Combine(_dir, name));

    [Fact]
    public void Shops_survive_a_save_and_load()
    {
        var store = NewStore("a");
        var book = new ShopBook();
        book.Shops.Add(new Shop { Name = "Mart", Items = { new ShopItem { Name = "Potion", Quantity = 5, Price = 300 } } });
        store.Save(book);

        var loaded = store.Load();
        var item = Assert.Single(Assert.Single(loaded.Shops).Items);
        Assert.Equal(("Potion", 5, 300m, (string?)null), (item.Name, item.Quantity, item.Price, item.Picture));
        Assert.DoesNotContain("DisplayName", File.ReadAllText(store.ShopsFile));
    }

    [Fact]
    public void Normalize_fixes_negative_values_and_duplicate_ids()
    {
        var book = ShopBook.FromJson("""{"shops":[{"id":"x","items":[{"name":"A","quantity":-2,"price":-1}]},{"id":"x"}]}""");
        Assert.Equal(0, book.Shops[0].Items[0].Quantity);
        Assert.Equal(0m, book.Shops[0].Items[0].Price);
        Assert.NotEqual(book.Shops[0].Id, book.Shops[1].Id);
        Assert.NotNull(book.Shops[1].Items);
    }

    [Fact]
    public void Export_embeds_pictures_and_import_restores_them()
    {
        var source = NewStore("src");
        var png = Path.Combine(_dir, "ball.png");
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(png, new byte[] { 1, 2, 3, 4 });
        var shop = new Shop { Name = "Mart", Items = { new ShopItem { Name = "Ball", Quantity = 2, Price = 200, Picture = source.ImportImage(png) } } };

        var json = source.Export(new[] { shop });
        Assert.Contains("data:image/png;base64,", json);

        var target = NewStore("dst");
        var warnings = new List<string>();
        var imported = Assert.Single(target.Import(json, "fallback", null, warnings));
        Assert.Empty(warnings);
        Assert.Equal("Mart", imported.Name);
        Assert.NotEqual(shop.Id, imported.Id);
        var item = Assert.Single(imported.Items);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(target.ImagePath(item.Picture)!));
    }

    [Fact]
    public void Import_skips_items_missing_mandatory_fields()
    {
        var store = NewStore("x");
        var warnings = new List<string>();
        var json = """
            { "name": "Black market", "items": [
                { "name": "Rare Candy", "quantity": "3", "price": 4800 },
                { "name": "No price", "quantity": 1 },
                { "quantity": 1, "price": 10 },
                { "name": "Free", "quantity": 0, "price": 0 },
            ] }
            """;
        var shop = Assert.Single(store.Import(json, "fallback", null, warnings));
        Assert.Equal(new[] { "Rare Candy", "Free" }, shop.Items.Select(i => i.Name));
        Assert.Equal(3, shop.Items[0].Quantity);
        Assert.Equal(2, warnings.Count);
        Assert.Contains("missing price", warnings[0]);
        Assert.Contains("missing name", warnings[1]);
    }

    [Fact]
    public void Import_accepts_a_bare_list_of_items()
    {
        var shop = Assert.Single(NewStore("y").Import("""[{"name":"Potion","quantity":1,"price":300}]""", "Pewter Mart", null, new()));
        Assert.Equal("Pewter Mart", shop.Name);
        Assert.Single(shop.Items);
    }

    [Fact]
    public void Import_copies_pictures_from_the_shop_file_folder()
    {
        var folder = Path.Combine(_dir, "shared");
        Directory.CreateDirectory(Path.Combine(folder, "images"));
        File.WriteAllBytes(Path.Combine(folder, "images", "ball.png"), new byte[] { 5, 6 });
        var store = NewStore("pics");
        var warnings = new List<string>();
        var json = """[{"name":"Ball","quantity":1,"price":200,"picture":"ball.png"},{"name":"Ball 2","quantity":1,"price":200,"picture":"images/ball.png"}]""";
        var shop = Assert.Single(store.Import(json, "Mart", folder, warnings));
        Assert.Empty(warnings);
        Assert.All(shop.Items, i => Assert.Equal(new byte[] { 5, 6 }, File.ReadAllBytes(store.ImagePath(i.Picture)!)));
    }

    [Fact]
    public void Import_refuses_pictures_outside_the_shop_file_folder()
    {
        var folder = Path.Combine(_dir, "shared");
        Directory.CreateDirectory(folder);
        var secret = Path.Combine(_dir, "secret.png");
        File.WriteAllBytes(secret, new byte[] { 9 });
        var notImage = Path.Combine(folder, "id_rsa");
        File.WriteAllText(notImage, "key");
        var store = NewStore("evil");
        var warnings = new List<string>();
        var items = new[] { secret, "../secret.png", notImage, "id_rsa" }
            .Select((p, n) => new { name = $"Item {n}", quantity = 1, price = 1, picture = p });
        var json = System.Text.Json.JsonSerializer.Serialize(items);

        var shop = Assert.Single(store.Import(json, "Mart", folder, warnings));
        Assert.Equal(4, warnings.Count);
        Assert.All(shop.Items, i => Assert.Null(i.Picture));
        Assert.False(Directory.Exists(store.ImagesDirectory) && Directory.EnumerateFiles(store.ImagesDirectory).Any());
    }

    [Fact]
    public void Import_rejects_unrelated_json()
    {
        Assert.Throws<InvalidDataException>(() => NewStore("z").Import("""{"foo": 1}""", "f", null, new()));
    }
}
