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
        Content = new ScrollViewer
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
        },
    };
}
