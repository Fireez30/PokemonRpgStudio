namespace PokemonRpgStudio.App.Services;

/// <summary>
/// Cheap "has anything changed on disk since I last looked" check for pages that re-read the user's files
/// when they are shown: number, size and date of the files of some folders / files, nothing is parsed.
/// Re-reading and rebuilding every sheet each time a page is shown is slow on phones.
/// </summary>
public sealed class DiskChangeWatch(params Func<string>[] paths)
{
    private string? _seen;

    public bool HasChanged => Compute() != _seen;

    /// <summary>The current state of the files is known to the page.</summary>
    public void MarkSeen() => _seen = Compute();

    private string Compute()
    {
        var parts = new List<string>();
        foreach (var get in paths)
        {
            var path = get();
            try
            {
                if (Directory.Exists(path))
                {
                    long count = 0, size = 0, newest = 0;
                    foreach (var file in new DirectoryInfo(path).EnumerateFiles())
                    {
                        count++;
                        size += file.Length;
                        newest = Math.Max(newest, file.LastWriteTimeUtc.Ticks);
                    }
                    parts.Add($"{count}:{size}:{newest}");
                }
                else if (File.Exists(path))
                {
                    var file = new FileInfo(path);
                    parts.Add($"{file.Length}:{file.LastWriteTimeUtc.Ticks}");
                }
                else parts.Add("-");
            }
            catch (IOException) { parts.Add(Guid.NewGuid().ToString()); } // unreadable: treat as changed
        }
        return string.Join("|", parts);
    }
}
