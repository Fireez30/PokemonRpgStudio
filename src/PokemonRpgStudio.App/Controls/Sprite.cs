using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PokemonRpgStudio.App.Services;

namespace PokemonRpgStudio.App.Controls;

/// <summary>
/// <c>c:Sprite.Pokemon="{Binding PokemonName}"</c> on an Image: shows the Pokémon's sprite, decoded in the
/// background the first time (a sprite is a few hundred pixels wide: decoding it on the UI thread while a list
/// scrolls makes it stutter on phones). Sprites already decoded are shown at once.
/// <c>c:Sprite.DecodeWidth</c> sets the decoded width (default 256): small icons can use less.
/// </summary>
public static class Sprite
{
    public static readonly AttachedProperty<string?> PokemonProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Pokemon", typeof(Sprite));

    public static readonly AttachedProperty<int> DecodeWidthProperty =
        AvaloniaProperty.RegisterAttached<Image, int>("DecodeWidth", typeof(Sprite), 256);

    public static string? GetPokemon(Image image) => image.GetValue(PokemonProperty);
    public static void SetPokemon(Image image, string? value) => image.SetValue(PokemonProperty, value);
    public static int GetDecodeWidth(Image image) => image.GetValue(DecodeWidthProperty);
    public static void SetDecodeWidth(Image image, int value) => image.SetValue(DecodeWidthProperty, value);

    static Sprite()
    {
        PokemonProperty.Changed.AddClassHandler<Image>((image, _) => Load(image));
        DecodeWidthProperty.Changed.AddClassHandler<Image>((image, _) => Load(image));
    }

    private static void Load(Image image)
    {
        var name = GetPokemon(image);
        var width = GetDecodeWidth(image);
        if (string.IsNullOrWhiteSpace(name))
        {
            image.Source = null;
            return;
        }
        if (BitmapCache.TryGetPokemon(name, width, out var cached))
        {
            image.Source = cached;
            return;
        }
        image.Source = null; // no stale sprite of the previous item while this one loads
        Task.Run(() =>
        {
            Bitmap? bitmap = null;
            try { bitmap = BitmapCache.ForPokemon(name, width); }
            catch (Exception) { /* missing or broken file: no sprite */ }
            Dispatcher.UIThread.Post(() =>
            {
                // The image may show another Pokémon by now (recycled list row).
                if (GetPokemon(image) == name && GetDecodeWidth(image) == width) image.Source = bitmap;
            });
        });
    }
}
