using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PokemonRpgStudio.App.Views;

/// <summary>Shown when the game data can't be loaded (e.g. missing Data/ folder).</summary>
public static class StartupErrorWindow
{
    public static Window Create(Exception ex) => new()
    {
        Title = "Pokémon RPG Studio — startup error",
        Width = 640,
        Height = 320,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Content = CreateView(ex),
    };

    /// <summary>The message alone (Android: no windows).</summary>
    public static Control CreateView(Exception ex) => new ScrollViewer
    {
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "The application could not start.", FontSize = 18, FontWeight = FontWeight.Bold },
                new TextBlock
                {
                    Text = "The Pokémon data (Data/pokemon.json, moves.json, abilities.json) must sit next to the executable. " +
                           "Reinstall the application or set POKEMON_RPG_STUDIO_DATA to the folder containing these files.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new SelectableTextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed },
            },
        },
    };

    /// <summary>Shown while the data is prepared (Android: the first start copies the bundled data).</summary>
    public static Control CreateLoadingView() => new StackPanel
    {
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        Spacing = 16,
        Children =
        {
            new Image { Width = 72, Height = 72, Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://" + typeof(StartupErrorWindow).Assembly.GetName().Name + "/Assets/app-icon.png"))) },
            new TextBlock { Text = "Pokémon RPG Studio", FontSize = 20, FontWeight = FontWeight.Bold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
            new ProgressBar { IsIndeterminate = true, Width = 200 },
            new TextBlock { Text = "Preparing the game data…", Foreground = Brushes.Gray, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
        },
    };
}
