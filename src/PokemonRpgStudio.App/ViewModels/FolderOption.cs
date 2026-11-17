using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.ViewModels;

/// <summary>
/// An entry of a folder picker (sheets or zones). <see cref="Path"/>: null = all records, "" = no folder,
/// otherwise a folder path (nested folders are indented).
/// </summary>
public sealed record FolderOption(string? Path, string Label, int Depth, int Count, bool ShowCount = true)
{
    public bool IsFolder => !string.IsNullOrEmpty(Path);
    public Avalonia.Thickness Indent => new(Depth * 14, 0, 0, 0);
    public string Glyph => Path is null ? "🗂" : Path.Length == 0 ? "📄" : "📁";
    public string Text => ShowCount ? $"{Label} ({Count})" : Label;
    public override string ToString() => Path is null or "" ? Label : Path;

    /// <summary>"All sheets", "No folder", then every folder as a tree, with the sheet counts.</summary>
    public static IReadOnlyList<FolderOption> ForFilter(IReadOnlyCollection<SheetRecord> sheets, AppSettings settings) =>
        ForFilter(sheets, settings.SheetFolders, "All sheets");

    /// <summary>Destinations of "Move to folder": "No folder", then every folder.</summary>
    public static IReadOnlyList<FolderOption> ForMove(IReadOnlyCollection<SheetRecord> sheets, AppSettings settings) =>
        ForMove(sheets, settings.SheetFolders);

    /// <summary>"All …", "No folder", then every folder as a tree, with the record counts.</summary>
    public static IReadOnlyList<FolderOption> ForFilter(IReadOnlyCollection<IFolderRecord> records, IEnumerable<string> saved, string allLabel)
    {
        var list = new List<FolderOption>
        {
            new(null, allLabel, 0, records.Count),
            new("", "No folder", 0, records.Count(r => SheetFolders.Normalize(r.Folder).Length == 0)),
        };
        list.AddRange(Folders(records, saved, showCount: true));
        return list;
    }

    /// <summary>Destinations of "Move to folder": "No folder", then every folder.</summary>
    public static IReadOnlyList<FolderOption> ForMove(IReadOnlyCollection<IFolderRecord> records, IEnumerable<string> saved) =>
        new[] { new FolderOption("", "No folder", 0, 0, ShowCount: false) }
            .Concat(Folders(records, saved, showCount: false)).ToList();

    private static IEnumerable<FolderOption> Folders(IReadOnlyCollection<IFolderRecord> records, IEnumerable<string> saved, bool showCount) =>
        SheetFolders.All(records, saved).Select(f => new FolderOption(f, SheetFolders.Name(f), SheetFolders.Depth(f),
            showCount ? records.Count(r => SheetFolders.IsInside(SheetFolders.Normalize(r.Folder), f)) : 0, showCount));
}
