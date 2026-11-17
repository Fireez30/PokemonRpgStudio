using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace PokemonRpgStudio.App.Views;

/// <summary>
/// Adaptive layouts for narrow screens (phones, folded foldables, split screen).
/// <para>
/// <c>views:Adaptive.DrawerBelow="600"</c> on a Grid whose first (list) or last (side panel) column has a
/// fixed width: when the Grid is narrower than the value, that column stops taking space. Its content slides
/// over the page instead, opened by a round button in the bottom corner (a tap outside or the back button
/// closes it; picking an item in a list closes it too). Wider, the Grid is left as written.
/// <c>views:Adaptive.DrawerGlyph</c> sets the icon of the button.
/// </para>
/// <para>
/// <c>views:Adaptive.StackBelow="560"</c> on a one-row Grid: narrower than the value, its columns are shown
/// one under the other (toolbars, cards side by side).
/// </para>
/// <para>
/// <c>views:Adaptive.MinContentWidth="780"</c> on a ScrollViewer (horizontal scrolling, vertical disabled):
/// the content is as wide as the viewer, but never narrower than the value, so wide tables scroll sideways
/// on phones instead of being cut.
/// </para>
/// <para>
/// <c>views:Adaptive.FillUpTo="380"</c> instead of a fixed Width: that width, or less when the page is
/// narrower (the width of the closest vertical panel, minus the other items of its row).
/// </para>
/// </summary>
public static class Adaptive
{
    public static readonly AttachedProperty<double> DrawerBelowProperty =
        AvaloniaProperty.RegisterAttached<Grid, double>("DrawerBelow", typeof(Adaptive));

    public static readonly AttachedProperty<string> DrawerGlyphProperty =
        AvaloniaProperty.RegisterAttached<Grid, string>("DrawerGlyph", typeof(Adaptive), "☰");

    private static readonly AttachedProperty<DrawerLayout?> LayoutProperty =
        AvaloniaProperty.RegisterAttached<Grid, DrawerLayout?>("Layout", typeof(Adaptive));

    public static readonly AttachedProperty<double> StackBelowProperty =
        AvaloniaProperty.RegisterAttached<Grid, double>("StackBelow", typeof(Adaptive));

    public static readonly AttachedProperty<double> MinContentWidthProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double>("MinContentWidth", typeof(Adaptive));

    public static readonly AttachedProperty<double> FillUpToProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("FillUpTo", typeof(Adaptive));

    public static double GetStackBelow(Grid grid) => grid.GetValue(StackBelowProperty);
    public static void SetStackBelow(Grid grid, double value) => grid.SetValue(StackBelowProperty, value);
    public static double GetMinContentWidth(ScrollViewer viewer) => viewer.GetValue(MinContentWidthProperty);
    public static void SetMinContentWidth(ScrollViewer viewer, double value) => viewer.SetValue(MinContentWidthProperty, value);
    public static double GetFillUpTo(Control control) => control.GetValue(FillUpToProperty);
    public static void SetFillUpTo(Control control, double value) => control.SetValue(FillUpToProperty, value);

    public static double GetDrawerBelow(Grid grid) => grid.GetValue(DrawerBelowProperty);
    public static void SetDrawerBelow(Grid grid, double value) => grid.SetValue(DrawerBelowProperty, value);
    public static string GetDrawerGlyph(Grid grid) => grid.GetValue(DrawerGlyphProperty);
    public static void SetDrawerGlyph(Grid grid, string value) => grid.SetValue(DrawerGlyphProperty, value);

    static Adaptive()
    {
        DrawerBelowProperty.Changed.AddClassHandler<Grid>((grid, _) =>
        {
            if (grid.GetValue(LayoutProperty) is null) grid.SetValue(LayoutProperty, new DrawerLayout(grid));
        });
        StackBelowProperty.Changed.AddClassHandler<Grid>((grid, e) =>
        {
            if (e.OldValue is double old && old > 0) return; // set up once
            var layout = new StackLayout(grid);
            grid.SizeChanged += (_, args) => layout.Update(args.NewSize.Width);
        });
        MinContentWidthProperty.Changed.AddClassHandler<ScrollViewer>((viewer, e) =>
        {
            if (e.OldValue is double old && old > 0) return;
            void Update(double width)
            {
                if (viewer.Content is Control content && width > 0)
                    content.Width = Math.Max(GetMinContentWidth(viewer), width);
            }
            viewer.SizeChanged += (_, args) => Update(args.NewSize.Width);
            Update(viewer.Bounds.Width);
        });
        FillUpToProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.OldValue is double old && old > 0) return;
            control.Width = GetFillUpTo(control);
            Control? host = null;
            void Update(object? sender, SizeChangedEventArgs args) => Fit(args.NewSize.Width);
            void Fit(double hostWidth)
            {
                var available = hostWidth - control.Margin.Left - control.Margin.Right - 4;
                // In a row (horizontal StackPanel), leave room for the other items of the row.
                if (control.Parent is StackPanel { Orientation: Orientation.Horizontal } row)
                {
                    var others = row.Children.Where(c => c != control && c.IsVisible).ToList();
                    available -= others.Sum(c => c.DesiredSize.Width) + row.Spacing * others.Count;
                }
                if (available > 0) control.Width = Math.Max(80, Math.Min(GetFillUpTo(control), available));
            }
            control.AttachedToVisualTree += (_, _) =>
            {
                // The closest panel whose width doesn't depend on its children: vertical StackPanel, DockPanel, Grid, page.
                host = control.GetVisualAncestors().OfType<Control>().FirstOrDefault(a => a switch
                {
                    StackPanel sp => sp.Orientation == Orientation.Vertical,
                    WrapPanel => false,
                    Panel => true,
                    ContentPresenter => true,
                    _ => false,
                });
                if (host is null) return;
                host.SizeChanged += Update;
                if (host.Bounds.Width > 0) Fit(host.Bounds.Width); // already laid out: no size change will come
            };
            control.DetachedFromVisualTree += (_, _) =>
            {
                if (host != null) host.SizeChanged -= Update;
                host = null;
            };
        });
    }

    /// <summary>Closes an open drawer inside <paramref name="root"/> (back button). False when none is open.</summary>
    public static bool CloseOpenDrawer(Visual root)
    {
        foreach (var grid in root.GetVisualDescendants().OfType<Grid>())
        {
            if (grid.IsEffectivelyVisible && grid.GetValue(LayoutProperty) is { IsOpen: true } layout)
            {
                layout.SetOpen(false);
                return true;
            }
        }
        return false;
    }

    private sealed class DrawerLayout
    {
        private readonly Grid _grid;
        private readonly Border _scrim;
        private readonly Button _toggle;
        private readonly List<(Control Control, int Column, int ColumnSpan, int RowSpan)> _moved = new();
        private readonly List<(Control Control, IDisposable? Background, HorizontalAlignment Alignment, double Width, int ZIndex)> _drawers = new();
        private GridLength[]? _columns;
        private int _drawerColumn = -1;
        private bool _narrow;

        public bool IsOpen { get; private set; }

        public DrawerLayout(Grid grid)
        {
            _grid = grid;
            _scrim = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),
                IsVisible = false,
                ZIndex = 90,
            };
            _scrim.PointerPressed += (_, e) => { SetOpen(false); e.Handled = true; };
            _toggle = new Button
            {
                Classes = { "accent", "fab" },
                Width = 52,
                Height = 52,
                CornerRadius = new CornerRadius(26),
                FontSize = 20,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(16),
                IsVisible = false,
                ZIndex = 95,
            };
            _toggle.Click += (_, _) => SetOpen(!IsOpen);
            grid.SizeChanged += (_, e) => Update(e.NewSize.Width);
        }

        private void Update(double width)
        {
            if (width <= 0) return;
            bool narrow = width < GetDrawerBelow(_grid);
            if (narrow != _narrow)
            {
                if (narrow) Collapse();
                else Expand();
            }
            if (_narrow)
            {
                foreach (var d in _drawers)
                    d.Control.Width = Math.Max(160, Math.Min(_columns![_drawerColumn].Value + 24, width - 48));
            }
        }

        /// <summary>Narrow: the side column becomes a drawer over the page.</summary>
        private void Collapse()
        {
            var columns = _grid.ColumnDefinitions;
            if (columns.Count < 2) return;
            int drawer = columns[0].Width.IsAbsolute && !columns[^1].Width.IsAbsolute ? 0
                : columns[^1].Width.IsAbsolute ? columns.Count - 1 : -1;
            if (drawer < 0) return;
            _narrow = true;
            _drawerColumn = drawer;
            _columns = columns.Select(c => c.Width).ToArray();
            bool left = drawer == 0;
            int rows = Math.Max(1, _grid.RowDefinitions.Count);

            foreach (var child in _grid.Children.OfType<Control>().ToList())
            {
                if (child == _scrim || child == _toggle) continue;
                _moved.Add((child, Grid.GetColumn(child), Grid.GetColumnSpan(child), Grid.GetRowSpan(child)));
                bool isDrawer = Grid.GetColumn(child) == drawer;
                Grid.SetColumn(child, 0);
                Grid.SetColumnSpan(child, columns.Count);
                if (!isDrawer) continue;
                Grid.SetRowSpan(child, rows);
                var background = BackgroundSetter.HasOwn(child) ? null
                    : child.GetResourceObservable("PageBackground").Subscribe(new BackgroundSetter(child));
                _drawers.Add((child, background, child.HorizontalAlignment, child.Width, child.ZIndex));
                child.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                child.ZIndex = 100;
                if (left) child.AddHandler(SelectingItemsControl.SelectionChangedEvent, OnDrawerSelection, RoutingStrategies.Bubble, true);
            }
            for (int i = 0; i < columns.Count; i++)
                columns[i].Width = i == drawer ? new GridLength(0) : columns[i].Width.IsAbsolute ? new GridLength(0) : columns[i].Width;

            Grid.SetColumnSpan(_scrim, columns.Count);
            Grid.SetRowSpan(_scrim, rows);
            Grid.SetColumnSpan(_toggle, columns.Count);
            Grid.SetRowSpan(_toggle, rows);
            _toggle.Content = GetDrawerGlyph(_grid);
            _toggle.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            _grid.Children.Add(_scrim);
            _grid.Children.Add(_toggle);

            // A list with nothing picked yet starts open (the page would be empty otherwise).
            bool startOpen = left && _drawers.Any(d => d.Control.GetVisualDescendants().OfType<ListBox>().FirstOrDefault() is { SelectedItem: null });
            SetOpen(startOpen);
        }

        /// <summary>Wide again: the Grid as written.</summary>
        private void Expand()
        {
            _narrow = false;
            IsOpen = false;
            _grid.Children.Remove(_scrim);
            _grid.Children.Remove(_toggle);
            if (_columns != null)
                for (int i = 0; i < _columns.Length && i < _grid.ColumnDefinitions.Count; i++) _grid.ColumnDefinitions[i].Width = _columns[i];
            foreach (var (control, column, span, rowSpan) in _moved)
            {
                Grid.SetColumn(control, column);
                Grid.SetColumnSpan(control, span);
                Grid.SetRowSpan(control, rowSpan);
            }
            foreach (var (control, background, alignment, width, zIndex) in _drawers)
            {
                if (background != null)
                {
                    background.Dispose();
                    BackgroundSetter.Clear(control);
                }
                control.HorizontalAlignment = alignment;
                control.Width = width;
                control.ZIndex = zIndex;
                control.SetCurrentValue(Visual.IsVisibleProperty, true);
                control.RemoveHandler(SelectingItemsControl.SelectionChangedEvent, OnDrawerSelection);
            }
            _moved.Clear();
            _drawers.Clear();
        }

        public void SetOpen(bool open)
        {
            if (!_narrow) return;
            IsOpen = open;
            foreach (var d in _drawers) d.Control.SetCurrentValue(Visual.IsVisibleProperty, open);
            _scrim.IsVisible = open;
            _toggle.IsVisible = !open;
        }

        /// <summary>An item picked in the list: show it.</summary>
        private void OnDrawerSelection(object? sender, SelectionChangedEventArgs e)
        {
            if (IsOpen && e.Source is ListBox && e.AddedItems.Count > 0) SetOpen(false);
        }
    }

    /// <summary>One-row Grid shown as a column of its cells when narrow.</summary>
    private sealed class StackLayout(Grid grid)
    {
        private GridLength[]? _columns;
        private RowDefinitions? _rows;
        private readonly List<(Control Control, int Column, int Row, HorizontalAlignment Alignment, Thickness Margin)> _cells = new();

        public void Update(double width)
        {
            if (width <= 0) return;
            bool narrow = width < GetStackBelow(grid);
            if (narrow == (_columns != null)) return;
            if (narrow) Stack();
            else Unstack();
        }

        private void Stack()
        {
            if (grid.RowDefinitions.Count > 1) return;
            _columns = grid.ColumnDefinitions.Select(c => c.Width).ToArray();
            _rows = grid.RowDefinitions;
            var cells = grid.Children.OfType<Control>().OrderBy(Grid.GetColumn).ToList();
            var rows = new RowDefinitions();
            int row = 0;
            foreach (var cell in cells)
            {
                _cells.Add((cell, Grid.GetColumn(cell), Grid.GetRow(cell), cell.HorizontalAlignment, cell.Margin));
                bool spacer = grid.ColumnDefinitions[Math.Min(Grid.GetColumn(cell), grid.ColumnDefinitions.Count - 1)].Width is { IsAbsolute: true, Value: < 24 };
                if (spacer) continue;
                rows.Add(new RowDefinition(GridLength.Auto));
                Grid.SetColumn(cell, 0);
                Grid.SetRow(cell, row);
                if (row > 0) cell.Margin = new Thickness(cell.Margin.Left, Math.Max(cell.Margin.Top, 8), cell.Margin.Right, cell.Margin.Bottom);
                if (cell.HorizontalAlignment == HorizontalAlignment.Right) cell.HorizontalAlignment = HorizontalAlignment.Left;
                row++;
            }
            grid.RowDefinitions = rows;
            for (int i = 0; i < grid.ColumnDefinitions.Count; i++)
                grid.ColumnDefinitions[i].Width = i == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }

        private void Unstack()
        {
            if (_columns is null) return;
            for (int i = 0; i < _columns.Length && i < grid.ColumnDefinitions.Count; i++) grid.ColumnDefinitions[i].Width = _columns[i];
            grid.RowDefinitions = _rows ?? new RowDefinitions();
            foreach (var (control, column, row, alignment, margin) in _cells)
            {
                Grid.SetColumn(control, column);
                Grid.SetRow(control, row);
                control.HorizontalAlignment = alignment;
                control.Margin = margin;
            }
            _cells.Clear();
            _columns = null;
        }
    }

    /// <summary>Gives a drawer an opaque background (the page color), following the theme.</summary>
    private sealed class BackgroundSetter(Control control) : IObserver<object?>
    {
        public void OnNext(object? value)
        {
            if (value is not IBrush brush) return;
            switch (control)
            {
                case Border b: b.Background = brush; break;
                case Panel p: p.Background = brush; break;
                case TemplatedControl t: t.Background = brush; break;
            }
        }

        public static bool HasOwn(Control control) => control switch
        {
            Border b => b.IsSet(Border.BackgroundProperty),
            Panel p => p.IsSet(Panel.BackgroundProperty),
            TemplatedControl t => t.IsSet(TemplatedControl.BackgroundProperty),
            _ => true,
        };

        public static void Clear(Control control)
        {
            switch (control)
            {
                case Border b: b.ClearValue(Border.BackgroundProperty); break;
                case Panel p: p.ClearValue(Panel.BackgroundProperty); break;
                case TemplatedControl t: t.ClearValue(TemplatedControl.BackgroundProperty); break;
            }
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
