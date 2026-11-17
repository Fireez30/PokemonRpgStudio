using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.Core.Storage;

/// <summary>
/// Folders of the sheet and zone libraries. A folder is a "/"-separated path stored on each record
/// (<see cref="IFolderRecord.Folder"/>, "" = no folder), so folders can be nested
/// ("Campaign/Fire team"). Empty folders are remembered in the settings.
/// </summary>
public static class SheetFolders
{
    public const char Separator = '/';

    /// <summary>"  Campaign / /Fire team " → "Campaign/Fire team".</summary>
    public static string Normalize(string? path) =>
        string.Join(Separator, (path ?? "").Replace('\\', Separator).Split(Separator)
            .Select(p => p.Trim()).Where(p => p.Length > 0));

    public static string Name(string path)
    {
        var i = path.LastIndexOf(Separator);
        return i < 0 ? path : path[(i + 1)..];
    }

    /// <summary>Parent folder ("" for a top-level folder).</summary>
    public static string Parent(string path)
    {
        var i = path.LastIndexOf(Separator);
        return i < 0 ? "" : path[..i];
    }

    public static int Depth(string path) => path.Length == 0 ? 0 : path.Count(c => c == Separator);

    public static string Combine(string parent, string name) => Normalize(parent.Length == 0 ? name : parent + Separator + name);

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or one of its subfolders.</summary>
    public static bool IsInside(string path, string folder) =>
        folder.Length == 0
            ? path.Length == 0
            : string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
              || path.StartsWith(folder + Separator, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every folder (used by sheets or saved empty) with its parents, sorted as a tree.</summary>
    public static List<string> All(IEnumerable<string?> sheetFolders, IEnumerable<string?> savedFolders)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in sheetFolders.Concat(savedFolders))
        {
            var path = Normalize(raw);
            while (path.Length > 0 && set.Add(path)) path = Parent(path);
        }
        // Sort segment by segment so children come right after their parent.
        return set.OrderBy(p => p.Split(Separator), SegmentComparer.Instance).ToList();
    }

    /// <summary>Path after renaming / moving <paramref name="from"/> to <paramref name="to"/> (subfolders follow).</summary>
    public static string Rename(string path, string from, string to)
    {
        if (!IsInside(path, from) || from.Length == 0) return path;
        return Normalize(to + path[from.Length..]);
    }

    // ── Library operations ──────────────────────────────────────────────────
    // Generic over the record type (sheets, zones): `saved` is the list of empty folders
    // remembered in the settings (AppSettings.SheetFolders / ZoneFolders), changed in place.

    /// <summary>Every folder of a library (records + saved empty folders).</summary>
    public static List<string> All(IEnumerable<IFolderRecord> records, IEnumerable<string> saved) =>
        All(records.Select(r => r.Folder), saved);

    /// <summary>Remembers a new (empty) folder. Returns its normalized path, or "" when the name is empty.</summary>
    public static string Create(List<string> saved, string parent, string name)
    {
        var path = Combine(Normalize(parent), name);
        if (path.Length > 0 && !saved.Contains(path, StringComparer.OrdinalIgnoreCase))
            saved.Add(path);
        return path;
    }

    /// <summary>
    /// Moves folder <paramref name="from"/> (with its subfolders and records) to <paramref name="to"/>.
    /// Moving into an existing folder merges both. Returns the number of records changed.
    /// </summary>
    public static int Move<T>(JsonRecordStore<T> store, List<string> saved, string from, string to) where T : class, IFolderRecord
    {
        from = Normalize(from);
        to = Normalize(to);
        if (from.Length == 0 || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return 0;
        if (IsInside(to, from)) throw new InvalidOperationException("A folder can't be moved inside itself.");
        int changed = 0;
        foreach (var record in store.List())
        {
            var path = Normalize(record.Folder);
            if (!IsInside(path, from)) continue;
            record.Folder = Rename(path, from, to);
            store.Save(record, touch: false);
            changed++;
        }
        var kept = saved.Select(f => IsInside(Normalize(f), from) ? Rename(Normalize(f), from, to) : Normalize(f))
            .Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Keep an emptied destination visible.
        if (to.Length > 0 && !kept.Contains(to, StringComparer.OrdinalIgnoreCase)) kept.Add(to);
        saved.Clear();
        saved.AddRange(kept);
        return changed;
    }

    /// <summary>Renames the last part of a folder ("Campaign/Fire" → "Campaign/Flames").</summary>
    public static string RenameFolder<T>(JsonRecordStore<T> store, List<string> saved, string folder, string newName) where T : class, IFolderRecord
    {
        var target = Combine(Parent(Normalize(folder)), newName);
        if (target.Length == 0) return folder;
        Move(store, saved, folder, target);
        return target;
    }

    /// <summary>Deletes a folder: its records and subfolders go to the parent folder (no record is deleted).</summary>
    public static int Delete<T>(JsonRecordStore<T> store, List<string> saved, string folder) where T : class, IFolderRecord
    {
        folder = Normalize(folder);
        return Move(store, saved, folder, Parent(folder));
    }

    // Sheet library shortcuts (folders saved in AppSettings.SheetFolders).
    public static List<string> All(IEnumerable<SheetRecord> sheets, AppSettings settings) => All(sheets, settings.SheetFolders);
    public static string Create(AppSettings settings, string parent, string name) => Create(settings.SheetFolders, parent, name);
    public static int Move(JsonRecordStore<SheetRecord> store, AppSettings settings, string from, string to) => Move(store, settings.SheetFolders, from, to);
    public static string RenameFolder(JsonRecordStore<SheetRecord> store, AppSettings settings, string folder, string newName) =>
        RenameFolder(store, settings.SheetFolders, folder, newName);
    public static int Delete(JsonRecordStore<SheetRecord> store, AppSettings settings, string folder) => Delete(store, settings.SheetFolders, folder);

    private sealed class SegmentComparer : IComparer<string[]>
    {
        public static readonly SegmentComparer Instance = new();

        public int Compare(string[]? x, string[]? y)
        {
            x ??= Array.Empty<string>();
            y ??= Array.Empty<string>();
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                var c = StringComparer.CurrentCultureIgnoreCase.Compare(x[i], y[i]);
                if (c != 0) return c;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
