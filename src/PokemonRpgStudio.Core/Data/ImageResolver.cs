using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.Core.Data;

/// <summary>
/// Finds the sprite of a Pokémon in the sprites folder. Like the JSON data, sprites are plain files
/// in the user's data folder (<c>data/images/</c>), not part of the application, so they can be
/// added or replaced; packages only ship default copies used to fill the folder.
/// </summary>
public sealed class ImageResolver
{
    private static readonly string[] Extensions = { "png", "jpeg", "jpg", "webp" };
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string ImagesDir { get; }

    public ImageResolver(string imagesDir) => ImagesDir = imagesDir;

    /// <summary>Forgets resolved paths (after sprites were added or replaced).</summary>
    public void ClearCache() => _cache.Clear();

    public static bool IsImageFile(string path) =>
        Extensions.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant());

    public static int CountImages(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir).Count(IsImageFile) : 0;

    /// <summary>
    /// Copies sprites from the default folders into <paramref name="imagesDir"/>: only the missing
    /// files, or all of them when <paramref name="overwrite"/> is set. Returns the number copied.
    /// </summary>
    public static int SeedImages(string imagesDir, IEnumerable<string> defaultDirs, bool overwrite = false)
    {
        Directory.CreateDirectory(imagesDir);
        var target = Path.GetFullPath(imagesDir);
        int copied = 0;
        foreach (var dir in defaultDirs.Where(Directory.Exists))
        {
            if (Path.GetFullPath(dir) == target) continue;
            foreach (var file in Directory.EnumerateFiles(dir).Where(IsImageFile))
            {
                var dest = Path.Combine(imagesDir, Path.GetFileName(file));
                if (File.Exists(dest) && !overwrite) continue;
                File.Copy(file, dest, overwrite: true);
                copied++;
            }
        }
        return copied;
    }

    /// <summary>
    /// Adds or replaces one sprite. The file name decides the Pokémon (website naming, e.g.
    /// <c>charizard.png</c>, <c>charizard_mega.png</c>).
    /// </summary>
    public static string InstallImage(string sourceFile, string imagesDir)
    {
        if (!IsImageFile(sourceFile))
            throw new InvalidDataException($"\"{Path.GetFileName(sourceFile)}\" is not a sprite (png, jpg, jpeg or webp).");
        Directory.CreateDirectory(imagesDir);
        var name = Path.GetFileName(sourceFile).ToLowerInvariant();
        var dest = Path.Combine(imagesDir, name);
        if (Path.GetFullPath(sourceFile) != Path.GetFullPath(dest)) File.Copy(sourceFile, dest, overwrite: true);
        return name;
    }

    /// <summary>File name used by the website (<c>/images/&lt;name&gt;.png</c>).</summary>
    public static string WebsiteImageName(string pokemonName) =>
        pokemonName.ToLowerInvariant().Replace(" (", "_(").Replace(") ", ")_").Replace(" ", "_") + ".png";

    /// <summary>Same normalisation as the website team card: strips accents and punctuation.</summary>
    public static string NormalizeBaseName(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        var s = sb.ToString().ToLowerInvariant().Replace("&", "and");
        s = Regex.Replace(s, "['’.]", "");
        s = Regex.Replace(s, "[()]", "");
        s = Regex.Replace(s, "[^a-z0-9]+", "_");
        return s.Trim('_');
    }

    public IEnumerable<string> CandidateNames(string pokemonName, bool mega = false)
    {
        var bases = new List<string>
        {
            Path.GetFileNameWithoutExtension(WebsiteImageName(pokemonName)),
            pokemonName.Replace(" ", "_").ToLowerInvariant(),
            NormalizeBaseName(pokemonName),
        };
        if (mega) bases = bases.Select(b => b + "_mega").Concat(bases).ToList();
        foreach (var b in bases.Distinct())
            foreach (var ext in Extensions)
                yield return $"{b}.{ext}";
    }

    public string? Resolve(string? pokemonName, bool mega = false)
    {
        if (string.IsNullOrWhiteSpace(pokemonName)) return null;
        var key = (mega ? "mega:" : "") + pokemonName;
        return _cache.GetOrAdd(key, _ =>
        {
            foreach (var candidate in CandidateNames(pokemonName, mega))
            {
                var path = Path.Combine(ImagesDir, candidate);
                if (File.Exists(path)) return path;
            }
            return null;
        });
    }

    /// <summary>Image for the mega form, using the <c>image_path</c> field when present.</summary>
    public string? ResolveMega(PokemonSpecies species)
    {
        var imagePath = species.MegaEvolution?["image_path"]?.ToString() ?? species.MegaEvolution?["img_path"]?.ToString();
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            var baseName = Path.GetFileNameWithoutExtension(imagePath);
            foreach (var b in new[] { baseName, baseName.Replace('-', '_') })
                foreach (var ext in Extensions)
                {
                    var p = Path.Combine(ImagesDir, $"{b}.{ext}");
                    if (File.Exists(p)) return p;
                }
        }
        var megaPath = Resolve(species.Name + " mega");
        return megaPath;
    }
}
