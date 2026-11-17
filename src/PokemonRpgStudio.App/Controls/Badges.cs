using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PokemonRpgStudio.App.Controls;

/// <summary>Row of coloured type badges (Fire, Water…).</summary>
public sealed class TypeBadges : WrapPanel
{
    public static readonly StyledProperty<IEnumerable?> TypesProperty =
        AvaloniaProperty.Register<TypeBadges, IEnumerable?>(nameof(Types));

    public static readonly StyledProperty<double> BadgeFontSizeProperty =
        AvaloniaProperty.Register<TypeBadges, double>(nameof(BadgeFontSize), 11);

    public IEnumerable? Types
    {
        get => GetValue(TypesProperty);
        set => SetValue(TypesProperty, value);
    }

    public double BadgeFontSize
    {
        get => GetValue(BadgeFontSizeProperty);
        set => SetValue(BadgeFontSizeProperty, value);
    }

    static TypeBadges()
    {
        TypesProperty.Changed.AddClassHandler<TypeBadges>((b, _) => b.Rebuild());
        BadgeFontSizeProperty.Changed.AddClassHandler<TypeBadges>((b, _) => b.Rebuild());
    }

    public TypeBadges()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
    }

    private List<string> _shown = new();
    private double _shownFontSize;

    private void Rebuild()
    {
        var names = new List<string>();
        if (Types != null)
            foreach (var item in Types)
                if (item?.ToString()?.Trim() is { Length: > 0 } text) names.Add(text);
        // Rows reused for another item (sheet switch) often show the same types: keep the badges.
        if (names.SequenceEqual(_shown) && BadgeFontSize == _shownFontSize && Children.Count == names.Count) return;
        _shown = names;
        _shownFontSize = BadgeFontSize;
        Children.Clear();
        foreach (var text in names) Children.Add(Make(text, TypeColors.ForType(text), BadgeFontSize));
    }

    internal static Border Make(string text, Color color, double fontSize) => new()
    {
        Background = new SolidColorBrush(color),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(8, 2),
        Margin = new Thickness(0, 1, 4, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(TypeColors.TextOn(color)),
        },
    };
}

/// <summary>Physical / Special / Status badge.</summary>
public sealed class ClassBadge : ContentControl
{
    public static readonly StyledProperty<string?> ClassNameProperty =
        AvaloniaProperty.Register<ClassBadge, string?>(nameof(ClassName));

    public string? ClassName
    {
        get => GetValue(ClassNameProperty);
        set => SetValue(ClassNameProperty, value);
    }

    static ClassBadge()
    {
        ClassNameProperty.Changed.AddClassHandler<ClassBadge>((b, _) =>
        {
            var text = b.ClassName?.Trim();
            b.Content = string.IsNullOrEmpty(text) ? null : TypeBadges.Make(text, TypeColors.ForClass(text), 11);
        });
    }
}
