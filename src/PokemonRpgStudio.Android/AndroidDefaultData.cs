using Android.Content;
using Android.Content.Res;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.Android;

/// <summary>
/// The default game data (JSON files and sprites) is packed as APK assets under <c>DefaultData/</c>.
/// AppServices reads the defaults from a folder, so they are copied to a temporary folder in the cache,
/// only when the data folder must be filled or updated (first start, new data in an update, "restore").
/// </summary>
internal static class AndroidDefaultData
{
    private const string AssetRoot = "DefaultData";

    /// <returns>The temporary folder holding the defaults, or null when the data folder is up to date.</returns>
    public static string? Extract(Context context, bool force)
    {
        var assets = context.Assets ?? throw new InvalidOperationException("No APK assets.");
        string shipped;
        try { shipped = ReadText(assets, $"{AssetRoot}/{AppServices.DataVersionFile}"); }
        catch (Java.IO.IOException) { return null; } // built without data

        if (!force)
        {
            var dataDir = AppServices.ResolveDataDir(AppPaths.Default());
            var stamp = Path.Combine(dataDir, AppServices.DataVersionFile);
            if (File.Exists(stamp) && File.ReadAllText(stamp) == shipped && GameData.MissingFiles(dataDir).Count == 0)
                return null;
        }

        var target = Path.Combine(context.CacheDir!.AbsolutePath, AssetRoot);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        CopyFolder(assets, AssetRoot, target);
        return target;
    }

    private static void CopyFolder(AssetManager assets, string assetDir, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var name in assets.List(assetDir) ?? Array.Empty<string>())
        {
            var path = $"{assetDir}/{name}";
            // Sub-folders (images/) are listed like files; every shipped file has an extension.
            if (!Path.HasExtension(name))
            {
                CopyFolder(assets, path, Path.Combine(target, name));
                continue;
            }
            using var input = assets.Open(path);
            using var output = File.Create(Path.Combine(target, name));
            input.CopyTo(output);
        }
    }

    private static string ReadText(AssetManager assets, string path)
    {
        using var reader = new StreamReader(assets.Open(path));
        return reader.ReadToEnd();
    }
}
