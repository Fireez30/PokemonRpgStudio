using System.Text.Json;
using System.Text.Json.Nodes;
using PokemonRpgStudio.Core.Json;
using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.Core.Storage;

/// <summary>
/// Saves the shops as <c>shops.json</c> in their folder. Item pictures are copied to its <c>images</c> folder,
/// so they keep working when the original files move. Exported files embed the pictures, so they can be
/// imported on another computer.
/// </summary>
public sealed class ShopStore
{
    public const string ExportFormat = "pokemon-rpg-studio/shops";

    private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp", [".bmp"] = "image/bmp", [".gif"] = "image/gif",
    };

    public string Directory { get; }
    public string ImagesDirectory => Path.Combine(Directory, "images");
    public string ShopsFile => Path.Combine(Directory, "shops.json");

    public ShopStore(string directory) => Directory = directory;

    public ShopBook Load()
    {
        try
        {
            if (File.Exists(ShopsFile))
            {
                var text = File.ReadAllText(ShopsFile);
                var book = ShopBook.FromJson(text);
                // Older file (no "effect" field, previous version...) or fixed by Normalize: rewrite it in the current format.
                if (book.ToJson() != text) TryUpgrade(book);
                return book;
            }
        }
        catch (Exception)
        {
            // Unreadable file: keep a copy so nothing is lost, and start from an empty book.
            try { File.Copy(ShopsFile, ShopsFile + ".broken", overwrite: true); } catch (Exception) { /* best effort */ }
        }
        return new ShopBook().Normalize();
    }

    private void TryUpgrade(ShopBook book)
    {
        try { Save(book); } catch (Exception) { /* read-only folder: the file is upgraded at the next save */ }
    }

    public void Save(ShopBook book)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var tmp = ShopsFile + ".tmp";
        File.WriteAllText(tmp, book.ToJson());
        File.Move(tmp, ShopsFile, overwrite: true);
    }

    /// <summary>Copies a picture into the shops' folder and returns its new file name.</summary>
    public string ImportImage(string sourceFile)
    {
        System.IO.Directory.CreateDirectory(ImagesDirectory);
        var name = Guid.NewGuid().ToString("N") + Path.GetExtension(sourceFile).ToLowerInvariant();
        File.Copy(sourceFile, Path.Combine(ImagesDirectory, name));
        return name;
    }

    private string SaveImageBytes(byte[] bytes, string extension)
    {
        System.IO.Directory.CreateDirectory(ImagesDirectory);
        var name = Guid.NewGuid().ToString("N") + extension;
        File.WriteAllBytes(Path.Combine(ImagesDirectory, name), bytes);
        return name;
    }

    /// <summary>Full path of a picture (null for an unsafe or empty name).</summary>
    public string? ImagePath(string? fileName) =>
        string.IsNullOrEmpty(fileName) || fileName != Path.GetFileName(fileName) ? null : Path.Combine(ImagesDirectory, fileName);

    /// <summary>Deletes the pictures no item uses anymore.</summary>
    public void DeleteUnusedImages(ShopBook book)
    {
        if (!System.IO.Directory.Exists(ImagesDirectory)) return;
        var used = book.AllItems.Select(i => i.Picture).Where(f => f != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in System.IO.Directory.EnumerateFiles(ImagesDirectory))
        {
            if (used.Contains(Path.GetFileName(file))) continue;
            try { File.Delete(file); } catch (Exception) { /* retried at the next cleanup */ }
        }
    }

    // ---- Export / import ----

    /// <summary>JSON of the shops with their pictures embedded as <c>data:</c> URIs.</summary>
    public string Export(IEnumerable<Shop> shops)
    {
        var exported = shops.Select(s => new Shop
        {
            Id = s.Id,
            Name = s.Name,
            Items = s.Items.Select(i =>
            {
                var copy = i.Clone();
                copy.Picture = PictureToDataUri(i.Picture);
                return copy;
            }).ToList(),
        }).ToList();
        var payload = new ExportFile { Format = ExportFormat, Version = ShopBook.CurrentVersion, Shops = exported };
        return JsonSerializer.Serialize(payload, JsonDefaults.Options);
    }

    private string? PictureToDataUri(string? fileName)
    {
        var path = ImagePath(fileName);
        if (path is null || !File.Exists(path)) return null;
        var mime = MimeByExtension.TryGetValue(Path.GetExtension(path), out var m) ? m : "application/octet-stream";
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    /// <summary>
    /// Reads shops from an exported file (or a <c>shops.json</c>). Accepts <c>{"shops": [...]}</c>, a single shop
    /// <c>{"name": ..., "items": [...]}</c>, an array of shops, or an array of items (one shop named
    /// <paramref name="fallbackName"/>). Items missing a name, quantity or price are skipped and reported in
    /// <paramref name="warnings"/>. Pictures (data URIs, or paths relative to <paramref name="baseDirectory"/>)
    /// are copied to the shops' folder. Imported shops get new ids.
    /// </summary>
    public List<Shop> Import(string json, string fallbackName, string? baseDirectory, List<string> warnings)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        }) ?? throw new InvalidDataException("Empty file.");

        List<ImportedShop> shops;
        if (root is JsonArray array)
        {
            bool areShops = array.Count > 0 && array.All(n => n is JsonObject o && Get(o, "items") is JsonArray);
            shops = areShops
                ? Deserialize<List<ImportedShop>>(array)
                : new() { new ImportedShop { Name = fallbackName, Items = Deserialize<List<ImportedItem?>>(array) } };
        }
        else if (root is JsonObject obj && Get(obj, "shops") is JsonArray list)
            shops = Deserialize<List<ImportedShop>>(list);
        else if (root is JsonObject single && Get(single, "items") is JsonArray)
            shops = new() { Deserialize<ImportedShop>(single) };
        else
            throw new InvalidDataException("No shops or items found in this file.");

        var result = new List<Shop>();
        foreach (var source in shops.Where(s => s is not null))
        {
            var shop = new Shop { Name = string.IsNullOrWhiteSpace(source.Name) ? fallbackName : source.Name.Trim() };
            int index = 0;
            foreach (var item in source.Items ?? new())
            {
                index++;
                var where = $"{shop.DisplayName}, item {index}";
                if (item is null) continue;
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(item.Name)) missing.Add("name");
                if (item.Quantity is null) missing.Add("quantity");
                if (item.Price is null) missing.Add("price");
                if (missing.Count > 0)
                {
                    warnings.Add($"{where}{(string.IsNullOrWhiteSpace(item.Name) ? "" : $" ({item.Name.Trim()})")}: skipped, missing {string.Join(", ", missing)}.");
                    continue;
                }
                string? picture = null;
                if (!string.IsNullOrWhiteSpace(item.Picture))
                {
                    try { picture = ImportPicture(item.Picture.Trim(), baseDirectory); }
                    catch (Exception ex) { warnings.Add($"{where} ({item.Name!.Trim()}): picture not imported ({ex.Message})."); }
                }
                shop.Items.Add(new ShopItem
                {
                    Name = item.Name!.Trim(),
                    Quantity = (int)Math.Clamp(Math.Floor(item.Quantity!.Value), 0, int.MaxValue),
                    Price = Math.Max(0, item.Price!.Value),
                    Picture = picture,
                    Effect = item.Effect?.Trim() ?? "",
                });
            }
            result.Add(shop);
        }
        return result;
    }

    private string? ImportPicture(string picture, string? baseDirectory)
    {
        if (picture.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = picture.IndexOf(',');
            if (comma < 0 || !picture[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("unsupported data URI");
            var mime = picture[5..comma].Split(';')[0];
            var ext = MimeByExtension.FirstOrDefault(p => p.Value.Equals(mime, StringComparison.OrdinalIgnoreCase)).Key ?? ".png";
            if (ext == ".jpeg") ext = ".jpg";
            return SaveImageBytes(Convert.FromBase64String(picture[(comma + 1)..]), ext);
        }
        // File pictures: only image files inside the imported file's folder, so a crafted shop file
        // can't make the app copy (and later export) arbitrary files such as private keys.
        if (baseDirectory is null) throw new InvalidDataException("file pictures need the shop file's folder");
        if (!MimeByExtension.ContainsKey(Path.GetExtension(picture))) throw new InvalidDataException("not an image file");
        var root = Path.GetFullPath(baseDirectory);
        var path = Path.GetFullPath(Path.Combine(root, picture));
        // shops.json copied from another computer: the picture sits in the "images" folder next to it.
        if (!File.Exists(path) && picture == Path.GetFileName(picture))
            path = Path.GetFullPath(Path.Combine(root, "images", picture));
        if (!IsInside(path, root)) throw new InvalidDataException("picture outside the shop file's folder");
        if (!File.Exists(path)) throw new FileNotFoundException("file not found");
        return ImportImage(path);
    }

    private static bool IsInside(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }

    private static JsonNode? Get(JsonObject obj, string name) =>
        obj.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static T Deserialize<T>(JsonNode node) =>
        node.Deserialize<T>(JsonDefaults.Options) ?? throw new InvalidDataException("Empty file.");

    private sealed class ExportFile
    {
        public string Format { get; set; } = ExportFormat;
        public int Version { get; set; }
        public List<Shop> Shops { get; set; } = new();
    }

    private sealed class ImportedShop
    {
        public string? Name { get; set; }
        public List<ImportedItem?>? Items { get; set; }
    }

    /// <summary>Nullable fields tell a missing mandatory value from a zero.</summary>
    private sealed class ImportedItem
    {
        public string? Name { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Price { get; set; }
        public string? Picture { get; set; }
        public string? Effect { get; set; }
    }
}
