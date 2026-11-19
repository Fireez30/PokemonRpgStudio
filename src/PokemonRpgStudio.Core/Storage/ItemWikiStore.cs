using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.Core.Storage;

/// <summary>
/// Saves the item wiki as <c>items.json</c> in its folder. Item pictures are copied to its <c>images</c> folder
/// (pictures of shop items too, so the wiki keeps them when the shop item goes away).
/// </summary>
public sealed class ItemWikiStore
{
    public string Directory { get; }
    public string ImagesDirectory => Path.Combine(Directory, "images");
    public string ItemsFile => Path.Combine(Directory, "items.json");

    public ItemWikiStore(string directory) => Directory = directory;

    public ItemWikiBook Load()
    {
        try
        {
            if (File.Exists(ItemsFile)) return ItemWikiBook.FromJson(File.ReadAllText(ItemsFile));
        }
        catch (Exception)
        {
            // Unreadable file: keep a copy so nothing is lost, and start from an empty wiki.
            try { File.Copy(ItemsFile, ItemsFile + ".broken", overwrite: true); } catch (Exception) { /* best effort */ }
        }
        return new ItemWikiBook().Normalize();
    }

    public void Save(ItemWikiBook book)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var tmp = ItemsFile + ".tmp";
        File.WriteAllText(tmp, book.ToJson());
        File.Move(tmp, ItemsFile, overwrite: true);
    }

    /// <summary>Copies a picture into the wiki's folder and returns its new file name.</summary>
    public string ImportImage(string sourceFile)
    {
        System.IO.Directory.CreateDirectory(ImagesDirectory);
        var name = Guid.NewGuid().ToString("N") + Path.GetExtension(sourceFile).ToLowerInvariant();
        File.Copy(sourceFile, Path.Combine(ImagesDirectory, name));
        return name;
    }

    /// <summary>Full path of a picture (null for an unsafe or empty name).</summary>
    public string? ImagePath(string? fileName) =>
        string.IsNullOrEmpty(fileName) || fileName != Path.GetFileName(fileName) ? null : Path.Combine(ImagesDirectory, fileName);

    /// <summary>Deletes the pictures no item uses anymore.</summary>
    public void DeleteUnusedImages(ItemWikiBook book)
    {
        if (!System.IO.Directory.Exists(ImagesDirectory)) return;
        var used = book.Items.Select(i => i.Picture).Where(f => f != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in System.IO.Directory.EnumerateFiles(ImagesDirectory))
        {
            if (used.Contains(Path.GetFileName(file))) continue;
            try { File.Delete(file); } catch (Exception) { /* retried at the next cleanup */ }
        }
    }
}
