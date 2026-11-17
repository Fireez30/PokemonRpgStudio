using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.App.ViewModels;
using PokemonRpgStudio.App.Views;

namespace PokemonRpgStudio.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                desktop.MainWindow = CreateMainWindow(AppServices.Initialize());
            }
            catch (MissingDataException missing)
            {
                // No data files and no default copy next to the app: ask the user for them.
                desktop.MainWindow = new DataSetupWindow(missing.DataDir, services =>
                {
                    var main = CreateMainWindow(services);
                    desktop.MainWindow = main;
                    main.Show();
                });
            }
            catch (Exception ex)
            {
                desktop.MainWindow = StartupErrorWindow.Create(ex);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindow CreateMainWindow(AppServices services)
    {
        ApplyTheme(services.Settings.Theme);
        var window = new MainWindow { DataContext = new MainWindowViewModel(services) };
        services.Dialogs.Attach(window);
        if (services.SeededFiles.Count > 0 || services.SeededImages > 0)
        {
            var what = services.SeededFiles.Concat(services.SeededImages > 0 ? new[] { $"{services.SeededImages} sprites" } : Array.Empty<string>());
            var verb = services.DataUpdated ? "updated to the version shipped with the application" : "set up";
            window.Opened += (_, _) => services.Dialogs.Notify(
                $"Game data {verb} in {services.DataDir} ({string.Join(", ", what)}). Update these files from Settings → Game data.");
        }
        return window;
    }

    public static void ApplyTheme(string theme)
    {
        if (Current is null) return;
        Current.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };
    }
}
