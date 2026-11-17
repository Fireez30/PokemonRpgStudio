using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
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
        if (PlatformInfo.IsMobile) PokemonRpgStudio.App.Controls.TouchTips.Install();
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
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            // Android: the first start copies the bundled data (a few seconds), so it runs off the UI thread.
            single.MainView = StartupErrorWindow.CreateLoadingView();
            Task.Run(AppServices.Initialize).ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                if (t.Exception is { } ex)
                {
                    single.MainView = StartupErrorWindow.CreateView(ex.InnerException ?? ex);
                    return;
                }
                var vm = new MainWindowViewModel(t.Result);
                single.MainView = CreateMainView(vm);
                // Android may end a background application without warning: save when leaving it.
                if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                    activatable.Deactivated += (_, _) => vm.Shutdown();
            }));
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindow CreateMainWindow(AppServices services)
    {
        var vm = new MainWindowViewModel(services);
        var window = new MainWindow { DataContext = vm };
        window.Opened += (_, _) => ShowDataNotice(services);
        ApplyTheme(services.Settings.Theme);
        return window;
    }

    private static MainView CreateMainView(MainWindowViewModel vm)
    {
        ApplyTheme(vm.Services.Settings.Theme);
        var view = new MainView { DataContext = vm };
        view.Loaded += (_, _) => ShowDataNotice(vm.Services);
        return view;
    }

    private static void ShowDataNotice(AppServices services)
    {
        if (services.SeededFiles.Count == 0 && services.SeededImages == 0) return;
        var what = services.SeededFiles.Concat(services.SeededImages > 0 ? new[] { $"{services.SeededImages} sprites" } : Array.Empty<string>());
        var verb = services.DataUpdated ? "updated to the version shipped with the application" : "set up";
        services.Dialogs.Notify(PlatformInfo.IsMobile
            ? $"Game data {verb} ({string.Join(", ", what)})."
            : $"Game data {verb} in {services.DataDir} ({string.Join(", ", what)}). Update these files from Settings → Game data.");
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
