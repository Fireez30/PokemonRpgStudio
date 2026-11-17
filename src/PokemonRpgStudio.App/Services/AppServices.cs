using Avalonia.Media.Imaging;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Rules;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.Services;

/// <summary>Application-wide singletons (data, user library, settings, UI helpers).</summary>
public sealed class AppServices
{
    public static AppServices Current { get; private set; } = null!;

    public GameData Data { get; private set; }
    public ImageResolver Images { get; }
    public UserLibrary Library { get; }
    public AppSettings Settings { get; }
    public DialogService Dialogs { get; } = new();

    /// <summary>The folder pokemon.json / moves.json / abilities.json are read from.</summary>
    public string DataDir => Data.DataDir;

    /// <summary>Stamp of the shipped data (see the csproj); the copy in the data folder tells which data it holds.</summary>
    public const string DataVersionFile = "data-version.txt";

    /// <summary>True when this start replaced the data files with newer shipped ones.</summary>
    public bool DataUpdated { get; private set; }

    /// <summary>Data files copied from the defaults at this start (shown once to the user).</summary>
    public IReadOnlyList<string> SeededFiles { get; private set; } = Array.Empty<string>();

    /// <summary>Number of sprites copied from the defaults at this start.</summary>
    public int SeededImages { get; private set; }

    /// <summary>The sprites folder (<c>data/images</c>, or <c>$POKEMON_RPG_STUDIO_IMAGES</c>).</summary>
    public string ImagesDir => Images.ImagesDir;

    /// <summary>Raised after sheets / teams / zones change so other pages refresh their pickers.</summary>
    public event Action? LibraryChanged;

    /// <summary>Raised after species data is edited or reloaded.</summary>
    public event Action? GameDataChanged;

    private AppServices(GameData data, ImageResolver images, UserLibrary library, AppSettings settings)
    {
        Data = data;
        Images = images;
        Library = library;
        Settings = settings;
    }

    /// <summary>
    /// Where the game data lives: <c>$POKEMON_RPG_STUDIO_DATA</c>, or the <c>data</c> folder of the
    /// user's files (~/.local/share/PokemonRpgStudio/data, %LOCALAPPDATA%\PokemonRpgStudio\data).
    /// </summary>
    public static string ResolveDataDir(AppPaths paths) =>
        Environment.GetEnvironmentVariable("POKEMON_RPG_STUDIO_DATA") is { Length: > 0 } d ? d : paths.Data;

    /// <summary>
    /// Default copies of the data shipped next to the application (never inside it): the
    /// <c>DefaultData</c> folder of the folder packages, or the <c>PokemonRpgStudio-data</c> folder
    /// placed beside the AppImage. Each holds the three JSON files and an <c>images/</c> folder.
    /// </summary>
    public static IReadOnlyList<string> DefaultDataDirs()
    {
        var dirs = new List<string>();
        if (_providedDefaultDir != null) dirs.Add(_providedDefaultDir);
        dirs.AddRange(new[]
        {
            Path.Combine(AppContext.BaseDirectory, "DefaultData"),
            Path.Combine(AppContext.BaseDirectory, "PokemonRpgStudio-data"),
        });
        // Inside an AppImage, AppContext.BaseDirectory is the read-only mount: look beside the .AppImage file.
        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage && Path.GetDirectoryName(appImage) is { } dir)
        {
            dirs.Add(Path.Combine(dir, "PokemonRpgStudio-data"));
            dirs.Add(Path.Combine(dir, "DefaultData"));
        }
        return dirs.Where(Directory.Exists).Distinct().ToList();
    }

    public static IReadOnlyList<string> DefaultImageDirs() =>
        DefaultDataDirs().Select(d => Path.Combine(d, "images")).Where(Directory.Exists).ToList();

    public static bool HasDefaultData => DefaultDataProvider != null || DefaultDataDirs().Any(d => GameData.FileNames.All(f => File.Exists(Path.Combine(d, f))));

    /// <summary>
    /// Platform hook for defaults that are not plain files next to the application (Android: APK assets).
    /// Called with <c>force</c> = false at start (return null when the data folder is up to date) and true
    /// to restore the defaults; returns a temporary folder holding them, deleted once they are copied.
    /// </summary>
    public static Func<bool, string?>? DefaultDataProvider { get; set; }

    private static string? _providedDefaultDir;

    private static T WithProvidedDefaults<T>(bool force, Func<T> action)
    {
        if (DefaultDataProvider is null) return action();
        _providedDefaultDir = DefaultDataProvider(force);
        try { return action(); }
        finally
        {
            try { if (_providedDefaultDir != null) Directory.Delete(_providedDefaultDir, recursive: true); }
            catch (Exception) { /* temporary folder: the system clears it */ }
            _providedDefaultDir = null;
        }
    }

    /// <summary>
    /// Fills the data folder with the default files that are missing, then loads it.
    /// </summary>
    /// <exception cref="MissingDataException">Files are still missing (no defaults found).</exception>
    public static AppServices Initialize() => WithProvidedDefaults(force: false, InitializeCore);

    private static AppServices InitializeCore()
    {
        var paths = AppPaths.Default();
        var library = new UserLibrary(paths);
        var settings = AppSettings.Load(paths.SettingsFile);
        var dataDir = ResolveDataDir(paths);
        var defaults = DefaultDataDirs();
        // New shipped data (new version of the application, AppImage updated by Gear Lever…): replace the
        // data files and sprites. Sheets, teams, zones and settings are not in the data folder.
        var (shippedVersion, update) = CheckDataVersion(dataDir, defaults);
        var seeded = GameData.SeedDataFolder(dataDir, defaults, overwrite: update);
        // The type chart is optional: copy the shipped one, or write the built-in one, so it can be edited like the other files.
        try { if (SeedTypeChart(dataDir, defaults, overwrite: update)) seeded.Add(TypeChart.FileName); }
        catch (Exception) { /* the built-in chart is used */ }

        // Sprites: plain files in data/images, missing ones filled from the defaults (all replaced on a data update;
        // sprites that only exist in the data folder are kept).
        var envImages = Environment.GetEnvironmentVariable("POKEMON_RPG_STUDIO_IMAGES");
        var imagesDir = string.IsNullOrEmpty(envImages) ? Path.Combine(dataDir, "images") : envImages;
        int seededImages = 0;
        try { seededImages = ImageResolver.SeedImages(imagesDir, DefaultImageDirs(), overwrite: update); }
        catch (Exception) { /* sprites are optional: the app works without them */ }

        if (shippedVersion != null && GameData.MissingFiles(dataDir).Count == 0)
        {
            try { File.WriteAllText(Path.Combine(dataDir, DataVersionFile), shippedVersion); }
            catch (Exception) { /* checked again at the next start */ }
        }

        var data = GameData.Load(dataDir);
        Current = new AppServices(data, new ImageResolver(imagesDir), library, settings)
        {
            SeededFiles = seeded, SeededImages = seededImages, DataUpdated = update && (seeded.Count > 0 || seededImages > 0),
        };
        return Current;
    }

    /// <summary>
    /// Reads the stamp of the shipped data and compares it with the one saved in the data folder.
    /// No stamp shipped (development build): nothing is replaced. An existing data folder without a stamp
    /// (older version) is updated.
    /// </summary>
    private static (string? Shipped, bool Update) CheckDataVersion(string dataDir, IReadOnlyList<string> defaults)
    {
        try
        {
            var stampFile = defaults.Select(d => Path.Combine(d, DataVersionFile)).FirstOrDefault(File.Exists);
            if (stampFile is null) return (null, false);
            var shipped = File.ReadAllText(stampFile);
            var localFile = Path.Combine(dataDir, DataVersionFile);
            var local = File.Exists(localFile) ? File.ReadAllText(localFile) : null;
            bool hasData = GameData.FileNames.Any(f => File.Exists(Path.Combine(dataDir, f)));
            return (shipped, hasData && local != shipped);
        }
        catch (Exception) { return (null, false); }
    }

    /// <summary>
    /// Puts <c>type_chart.json</c> in the data folder when it's missing (or always with <paramref name="overwrite"/>):
    /// the copy shipped in the default data when there is one, otherwise the built-in chart.
    /// </summary>
    private static bool SeedTypeChart(string dataDir, IEnumerable<string> defaultDirs, bool overwrite)
    {
        var target = Path.Combine(dataDir, TypeChart.FileName);
        if (File.Exists(target) && !overwrite) return false;
        var shipped = defaultDirs.Select(d => Path.Combine(d, TypeChart.FileName)).FirstOrDefault(File.Exists);
        if (shipped is null) return TypeChart.EnsureFile(dataDir, overwrite);
        if (Path.GetFullPath(shipped) == Path.GetFullPath(target)) return false;
        TypeChart.Load(shipped); // must be readable before it replaces anything
        Directory.CreateDirectory(dataDir);
        File.Copy(shipped, target, overwrite: true);
        return true;
    }

    public void SaveSettings()
    {
        try { Settings.Save(Library.Paths.SettingsFile); }
        catch (Exception ex) { Dialogs.Notify("Could not save settings: " + ex.Message, isError: true); }
    }

    public void NotifyLibraryChanged() => LibraryChanged?.Invoke();

    public void ReloadGameData()
    {
        Images.ClearCache();
        BitmapCache.Clear();
        Data = GameData.Load(DataDir);
        GameDataChanged?.Invoke();
    }

    /// <summary>Overwrites the data files and the sprites with the defaults shipped with the application.</summary>
    public List<string> RestoreDefaultData() => WithProvidedDefaults(force: true, RestoreDefaultDataCore);

    private List<string> RestoreDefaultDataCore()
    {
        var copied = GameData.SeedDataFolder(DataDir, DefaultDataDirs(), overwrite: true);
        var stamp = DefaultDataDirs().Select(d => Path.Combine(d, DataVersionFile)).FirstOrDefault(File.Exists);
        if (stamp != null) File.Copy(stamp, Path.Combine(DataDir, DataVersionFile), overwrite: true);
        if (SeedTypeChart(DataDir, DefaultDataDirs(), overwrite: true)) copied.Add(TypeChart.FileName);
        var images = ImageResolver.SeedImages(ImagesDir, DefaultImageDirs(), overwrite: true);
        if (images > 0) copied.Add($"{images} sprites");
        RefreshSprites(notify: false);
        ReloadGameData();
        return copied;
    }

    /// <summary>Adds / replaces sprites (file names decide the Pokémon) and refreshes the views.</summary>
    public List<string> InstallSprites(IEnumerable<string> files, List<string> errors)
    {
        var installed = new List<string>();
        foreach (var file in files)
        {
            try { installed.Add(ImageResolver.InstallImage(file, ImagesDir)); }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }
        if (installed.Count > 0) RefreshSprites();
        return installed;
    }

    /// <summary>Forgets cached sprites so changed files on disk are shown.</summary>
    public void RefreshSprites(bool notify = true)
    {
        Images.ClearCache();
        BitmapCache.Clear();
        if (notify) GameDataChanged?.Invoke();
    }

    public void NotifyGameDataChanged() => GameDataChanged?.Invoke();
}

/// <summary>Loads sprites lazily and keeps them in memory.</summary>
public static class BitmapCache
{
    private static readonly Dictionary<string, Bitmap?> Cache = new();
    private static readonly object Lock = new();

    public static void Clear()
    {
        lock (Lock) Cache.Clear();
    }

    public static Bitmap? Load(string? path, int decodeWidth = 256)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var key = path + "|" + decodeWidth;
        lock (Lock)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }
        Bitmap? bmp = null;
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                bmp = decodeWidth > 0 ? Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality) : new Bitmap(stream);
            }
        }
        catch (Exception)
        {
            bmp = null;
        }
        lock (Lock) Cache[key] = bmp;
        return bmp;
    }

    public static Bitmap? ForPokemon(string? name, int decodeWidth = 256) =>
        Load(AppServices.Current.Images.Resolve(name), decodeWidth);
}
