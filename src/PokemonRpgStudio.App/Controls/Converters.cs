using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PokemonRpgStudio.App.Services;

namespace PokemonRpgStudio.App.Controls;

public static class TypeColors
{
    private static readonly Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Normal"] = "#A8A77A", ["Fire"] = "#EE8130", ["Water"] = "#6390F0", ["Electric"] = "#F7D02C",
        ["Grass"] = "#7AC74C", ["Ice"] = "#96D9D6", ["Fighting"] = "#C22E28", ["Poison"] = "#A33EA1",
        ["Ground"] = "#E2BF65", ["Flying"] = "#A98FF3", ["Psychic"] = "#F95587", ["Bug"] = "#A6B91A",
        ["Rock"] = "#B6A136", ["Ghost"] = "#735797", ["Dragon"] = "#6F35FC", ["Dark"] = "#705746",
        ["Steel"] = "#B7B7CE", ["Fairy"] = "#D685AD", ["Sound"] = "#B1B1B1", ["Light"] = "#F5F7B0",
        ["Data"] = "#2C2C2C",
    };

    private static readonly Dictionary<string, string> ClassColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Physical"] = "#C94830", ["Special"] = "#4C6EC4", ["Status"] = "#7A7A7A",
    };

    public static Color ForType(string? type) =>
        Color.Parse(Colors.GetValueOrDefault((type ?? "").Trim(), "#888888"));

    public static Color ForClass(string? cls) =>
        Color.Parse(ClassColors.GetValueOrDefault((cls ?? "").Trim(), "#888888"));

    /// <summary>Dark text on light badges (Electric, Light, Ice…), white otherwise.</summary>
    public static Color TextOn(Color background)
    {
        double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
        return luminance > 0.68 ? Color.Parse("#1d1d1d") : Avalonia.Media.Colors.White;
    }
}

/// <summary>Type name → badge brush.</summary>
public sealed class TypeBrushConverter : IValueConverter
{
    public static readonly TypeBrushConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(TypeColors.ForType(value as string));
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Type chart multiplier → cell background (green super effective, red resisted, grey no effect).</summary>
public sealed class EffectivenessBrushConverter : IValueConverter
{
    public static readonly EffectivenessBrushConverter Instance = new();
    private static readonly IBrush Super = new SolidColorBrush(Color.Parse("#7066D17A"));
    private static readonly IBrush Resisted = new SolidColorBrush(Color.Parse("#60FF6B6B"));
    private static readonly IBrush Immune = new SolidColorBrush(Color.Parse("#70888888"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        double d when d == 0 => Immune,
        double d when d < 1 => Resisted,
        double d when d > 1 => Super,
        _ => Brushes.Transparent,
    };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Type name → readable foreground for the badge.</summary>
public sealed class TypeForegroundConverter : IValueConverter
{
    public static readonly TypeForegroundConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(TypeColors.TextOn(TypeColors.ForType(value as string)));
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ClassBrushConverter : IValueConverter
{
    public static readonly ClassBrushConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(TypeColors.ForClass(value as string));
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Pokémon name → sprite bitmap (cached).</summary>
public sealed class PokemonImageConverter : IValueConverter
{
    public static readonly PokemonImageConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name ? BitmapCache.ForPokemon(name, parameter is string w && int.TryParse(w, out var width) ? width : 256) : null;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>File path → bitmap (cached).</summary>
public sealed class PathImageConverter : IValueConverter
{
    public static readonly PathImageConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string path ? BitmapCache.Load(path) : null;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
