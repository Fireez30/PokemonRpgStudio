using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.Tests;

public class ItemWikiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wiki-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }

    private static ShopBook Shops(params (string Shop, ShopItem[] Items)[] shops)
    {
        var book = new ShopBook();
        foreach (var (name, items) in shops) book.Shops.Add(new Shop { Name = name, Items = items.ToList() });
        return book;
    }

    [Fact]
    public void Shop_items_are_added_once_by_name_ignoring_case()
    {
        var wiki = new ItemWikiBook();
        wiki.Items.Add(new WikiItem { Name = "Potion", Section = "" });
        var shops = Shops(
            ("Mart", new[] { new ShopItem { Name = "potion " }, new ShopItem { Name = "Super Potion" }, new ShopItem { Name = "  " } }),
            ("Other", new[] { new ShopItem { Name = "SUPER POTION", Effect = "Heals 50 HP" } }));

        var added = wiki.AddShopItems(shops, _ => null);

        var item = Assert.Single(added);
        Assert.Equal(("Super Potion", "Heals 50 HP", ""), (item.Name, item.Effect, item.Section));
        Assert.Equal(2, wiki.Items.Count);
        Assert.Empty(wiki.AddShopItems(shops, _ => null));
    }

    [Fact]
    public void Shop_pictures_are_copied_and_a_failing_copy_keeps_the_item()
    {
        var wiki = new ItemWikiBook();
        var shops = Shops(("Mart", new[]
        {
            new ShopItem { Name = "Ball", Picture = "ball.png" },
            new ShopItem { Name = "Broken", Picture = "broken.png" },
        }));

        wiki.AddShopItems(shops, p => p == "broken.png" ? throw new IOException() : "copy-" + p);

        Assert.Equal("copy-ball.png", wiki.FindItem("ball")!.Picture);
        Assert.Null(wiki.FindItem("broken")!.Picture);
    }

    [Fact]
    public void Renaming_and_removing_a_section_moves_its_items()
    {
        var wiki = new ItemWikiBook { Sections = { "Medicine", "Balls" } };
        wiki.Items.Add(new WikiItem { Name = "Potion", Section = "Medicine" });

        Assert.False(wiki.RenameSection("Medicine", "balls"));
        Assert.True(wiki.RenameSection("Medicine", "Healing"));
        Assert.Equal("Healing", wiki.Items[0].Section);

        wiki.RemoveSection("healing");
        Assert.Equal(new[] { "Balls" }, wiki.Sections);
        Assert.Equal("", wiki.Items[0].Section);
    }

    [Fact]
    public void Normalize_restores_lost_sections_and_fixes_case_and_duplicates()
    {
        var json = """
            {"sections":["Balls","balls",""],"items":[
              {"id":"a","name":"Poké Ball","section":"BALLS"},
              {"id":"a","name":"Rare Candy","section":"Misc"},
              null]}
            """;
        var wiki = ItemWikiBook.FromJson(json);

        Assert.Equal(new[] { "Balls", "Misc" }, wiki.Sections);
        Assert.Equal(new[] { "Balls", "Misc" }, wiki.Items.Select(i => i.Section));
        Assert.Equal(2, wiki.Items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void Wiki_survives_a_save_and_load()
    {
        var store = new ItemWikiStore(_dir);
        var wiki = new ItemWikiBook { Sections = { "Key items" } };
        wiki.Items.Add(new WikiItem { Name = "Bike", Section = "Key items", Effect = "Go fast", Description = "From the shop" });
        store.Save(wiki);

        var loaded = store.Load();
        var item = Assert.Single(loaded.Items);
        Assert.Equal(("Bike", "Key items", "Go fast", "From the shop"), (item.Name, item.Section, item.Effect, item.Description));
        Assert.DoesNotContain("DisplayName", File.ReadAllText(store.ItemsFile));
    }
}
