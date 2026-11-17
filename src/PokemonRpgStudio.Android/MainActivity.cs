using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using PokemonRpgStudio.App.Services;

namespace PokemonRpgStudio.App.Android;

// ConfigurationChanges: folding / unfolding, rotating or resizing (split screen, free-form) must not
// recreate the activity: Avalonia relays out the same window, and the adaptive layout follows the new width.
[Activity(
    Label = "Pokémon RPG Studio",
    Theme = "@style/AppTheme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ResizeableActivity = true,
    WindowSoftInputMode = global::Android.Views.SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize |
                           ConfigChanges.ScreenLayout | ConfigChanges.UiMode | ConfigChanges.Density |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // The game data is packed in the APK: AppServices asks for it when the data folder needs (re)filling.
        AppServices.DefaultDataProvider = force => AndroidDefaultData.Extract(this, force);
        base.OnCreate(savedInstanceState);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont().LogToTrace();
}
