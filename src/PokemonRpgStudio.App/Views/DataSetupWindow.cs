using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.Views;

/// <summary>
/// Shown at start-up when pokemon.json, moves.json or abilities.json is missing from the data folder
/// and no default copy was found next to the application. Lets the user pick the files.
/// </summary>
public sealed class DataSetupWindow : Window
{
    private readonly string _dataDir;
    private readonly Action<AppServices> _onReady;
    private readonly TextBlock _missing = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };

    public DataSetupWindow(string dataDir, Action<AppServices> onReady)
    {
        _dataDir = dataDir;
        _onReady = onReady;
        Title = "Pokémon RPG Studio — game data";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;

        var pickFiles = new Button { Content = "Choose the files…", Classes = { "accent" } };
        var pickFolder = new Button { Content = "Choose a folder…" };
        var openFolder = new Button { Content = "Open data folder" };
        var quit = new Button { Content = "Quit" };
        pickFiles.Click += async (_, _) => await PickFiles();
        pickFolder.Click += async (_, _) => await PickFolder();
        openFolder.Click += (_, _) => new DialogService().OpenInFileManager(_dataDir);
        quit.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Game data needed", FontSize = 20, FontWeight = FontWeight.Bold },
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Pokémon RPG Studio reads the Pokémon, moves and abilities from three files: " +
                           "pokemon.json, moves.json and abilities.json. They are kept as normal files in your data " +
                           "folder so you can update them at any time. Pick them to continue — for example the " +
                           "PokemonRpgStudio-data folder delivered with the application, or the website's server/pokemon_data folder.",
                },
                _missing,
                new SelectableTextBlock { Text = "Data folder: " + dataDir, TextWrapping = TextWrapping.Wrap, FontSize = 12 },
                _status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { quit, openFolder, pickFolder, pickFiles },
                },
            },
        };
        UpdateMissing();
    }

    private void UpdateMissing()
    {
        var missing = GameData.MissingFiles(_dataDir);
        _missing.Text = missing.Count == 0 ? "All files are present." : "Missing: " + string.Join(", ", missing);
    }

    private async Task PickFiles()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pick pokemon.json, moves.json and abilities.json",
            AllowMultiple = true,
            FileTypeFilter = new[] { DialogService.JsonFiles },
        });
        Install(files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!));
    }

    private async Task PickFolder()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder containing the data files" });
        var dir = folders.FirstOrDefault()?.TryGetLocalPath();
        if (dir is null) return;
        var files = GameData.FileNames.Select(f => Path.Combine(dir, f)).Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            _status.Text = "This folder doesn't contain pokemon.json, moves.json or abilities.json.";
            return;
        }
        Install(files);
    }

    private void Install(IEnumerable<string> files)
    {
        var errors = new List<string>();
        foreach (var file in files)
        {
            try { GameData.InstallDataFile(file, _dataDir); }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }
        _status.Text = string.Join("\n", errors);
        UpdateMissing();
        if (GameData.MissingFiles(_dataDir).Count > 0) return;
        try
        {
            var services = AppServices.Initialize();
            _onReady(services);
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = "The data could not be loaded: " + ex.Message;
        }
    }

    /// <summary>The data folder the application uses (for the setup window).</summary>
    public static string CurrentDataDir() => AppServices.ResolveDataDir(AppPaths.Default());
}
