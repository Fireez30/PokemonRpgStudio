using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace PokemonRpgStudio.App.Controls;

/// <summary>
/// A vertical list of items, like an ItemsControl with a StackPanel, for lists that are replaced as a whole
/// (the moves, stats, skills… of the sheet shown). When the items change, the rows already built are kept and
/// only get their new item, instead of being thrown away and built again from the template: switching sheets
/// then costs a few bindings per row rather than new buttons, styles and templates (slow on phones).
/// Rows not needed for the current items are hidden and kept for later.
/// </summary>
public sealed class RecyclingItems : StackPanel
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<RecyclingItems, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<RecyclingItems, IDataTemplate?>(nameof(ItemTemplate));

    private INotifyCollectionChanged? _observed;

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            if (_observed != null) _observed.CollectionChanged -= OnCollectionChanged;
            _observed = change.NewValue as INotifyCollectionChanged;
            if (_observed != null) _observed.CollectionChanged += OnCollectionChanged;
            Sync();
        }
        else if (change.Property == ItemTemplateProperty)
        {
            Children.Clear(); // rows of the old template can't be reused
            Sync();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

    private void Sync()
    {
        var template = ItemTemplate;
        int count = 0;
        if (ItemsSource != null && template != null)
        {
            foreach (var item in ItemsSource)
            {
                if (count < Children.Count)
                {
                    var row = Children[count];
                    row.DataContext = item;
                    row.IsVisible = true;
                }
                else
                {
                    var row = template.Build(item) ?? new TextBlock { Text = item?.ToString() };
                    row.DataContext = item;
                    Children.Add(row);
                }
                count++;
            }
        }
        // Spare rows: hidden (not measured nor drawn), ready for a longer list.
        for (int i = count; i < Children.Count; i++)
        {
            Children[i].IsVisible = false;
            Children[i].DataContext = null;
        }
    }
}
