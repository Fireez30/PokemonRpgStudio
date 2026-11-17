using Avalonia;
using Avalonia.Controls;

namespace PokemonRpgStudio.App.Views;

/// <summary>
/// Shows <see cref="Content"/> like a ContentControl, but keeps the view of each content it has shown and
/// only hides it when another one is shown. Coming back to a page is then instant, and the page keeps its
/// scroll positions. (Building a page's view takes a few hundred ms on a desktop and much longer on a
/// phone; the page view models are long-lived, so their views can be too.)
/// </summary>
public sealed class PageHost : Panel
{
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<PageHost, object?>(nameof(Content));

    private readonly Dictionary<object, ContentControl> _views = new(ReferenceEqualityComparer.Instance);
    private ContentControl? _shown;

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty) Show(change.NewValue);
    }

    private void Show(object? content)
    {
        ContentControl? view = null;
        if (content != null && !_views.TryGetValue(content, out view))
        {
            // The ContentControl picks the view from the DataTemplates, like before; it is created once.
            view = new ContentControl { Content = content };
            _views[content] = view;
            Children.Add(view);
        }
        if (_shown == view) return;
        if (_shown != null) _shown.IsVisible = false;
        _shown = view;
        if (view != null) view.IsVisible = true;
    }
}
