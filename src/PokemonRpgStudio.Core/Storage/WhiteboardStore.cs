using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.Core.Storage;

/// <summary>
/// Saves the whiteboard as <c>board.json</c> in its folder. Images added to the board are copied to its
/// <c>images</c> folder, so the board keeps working when the original files move.
/// </summary>
public sealed class WhiteboardStore
{
    public string Directory { get; }
    public string ImagesDirectory => Path.Combine(Directory, "images");
    public string BoardFile => Path.Combine(Directory, "board.json");

    public WhiteboardStore(string directory) => Directory = directory;

    public Whiteboard Load()
    {
        try
        {
            if (File.Exists(BoardFile)) return Whiteboard.FromJson(File.ReadAllText(BoardFile));
        }
        catch (Exception)
        {
            // Unreadable board: start from an empty one (the file is replaced at the next save).
        }
        return new Whiteboard().Normalize();
    }

    public void Save(Whiteboard board)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var tmp = BoardFile + ".tmp";
        File.WriteAllText(tmp, board.ToJson());
        File.Move(tmp, BoardFile, overwrite: true);
    }

    /// <summary>Copies an image into the board's folder and returns its new file name.</summary>
    public string ImportImage(string sourceFile)
    {
        System.IO.Directory.CreateDirectory(ImagesDirectory);
        var name = Guid.NewGuid().ToString("N") + Path.GetExtension(sourceFile).ToLowerInvariant();
        File.Copy(sourceFile, Path.Combine(ImagesDirectory, name));
        return name;
    }

    /// <summary>Full path of an image of the board (null for an unsafe or empty name).</summary>
    public string? ImagePath(string? fileName) =>
        string.IsNullOrEmpty(fileName) || fileName != Path.GetFileName(fileName) ? null : Path.Combine(ImagesDirectory, fileName);

    /// <summary>Deletes the images no item uses anymore.</summary>
    public void DeleteUnusedImages(Whiteboard board)
    {
        if (!System.IO.Directory.Exists(ImagesDirectory)) return;
        var used = board.AllItems.Select(i => i.ImageFile).Where(f => f != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in System.IO.Directory.EnumerateFiles(ImagesDirectory))
        {
            if (used.Contains(Path.GetFileName(file))) continue;
            try { File.Delete(file); } catch (Exception) { /* retried at the next cleanup */ }
        }
    }
}
