using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.App.ViewModels;

namespace PokemonRpgStudio.App.Views;

/// <summary>
/// The application: navigation + current page. Hosted by <see cref="MainWindow"/> on the desktop and
/// used directly as the single view on Android. The navigation adapts to the width (phone, foldable, desktop).
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>Below this width: phone layout (top bar, menu over the page).</summary>
    public const double NarrowWidth = 600;
    /// <summary>Below this width: icon rail; above, the full sidebar.</summary>
    public const double MediumWidth = 900;

    private enum ShellMode { None, Narrow, Medium, Wide }
    private ShellMode _mode;
    private TopLevel? _topLevel;

    public MainView()
    {
        InitializeComponent();
        TopMenuButton.Click += (_, _) => Shell.IsPaneOpen = !Shell.IsPaneOpen;
        RailMenuButton.Click += (_, _) => Shell.IsPaneOpen = !Shell.IsPaneOpen;
        Shell.PaneOpened += (_, _) => UpdatePaneContent();
        Shell.PaneClosed += (_, _) => UpdatePaneContent();
        // Picking a page closes the menu when it covers the page.
        Nav.SelectionChanged += (_, _) =>
        {
            if (_mode is ShellMode.Narrow or ShellMode.Medium) Shell.IsPaneOpen = false;
        };
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyMode(e.NewSize.Width);
    }

    private void ApplyMode(double width)
    {
        if (width <= 0) return;
        var mode = width < NarrowWidth ? ShellMode.Narrow : width < MediumWidth ? ShellMode.Medium : ShellMode.Wide;
        if (mode == _mode) return;
        _mode = mode;
        TopBar.IsVisible = mode == ShellMode.Narrow;
        RailMenuButton.IsVisible = mode == ShellMode.Medium;
        switch (mode)
        {
            case ShellMode.Narrow:
                Shell.DisplayMode = SplitViewDisplayMode.Overlay;
                Shell.OpenPaneLength = Math.Min(280, width - 56);
                Shell.IsPaneOpen = false;
                break;
            case ShellMode.Medium:
                Shell.DisplayMode = SplitViewDisplayMode.CompactOverlay;
                Shell.OpenPaneLength = 264;
                Shell.IsPaneOpen = false;
                break;
            default:
                Shell.DisplayMode = SplitViewDisplayMode.Inline;
                Shell.OpenPaneLength = 220;
                Shell.IsPaneOpen = true;
                break;
        }
        UpdatePaneContent();
    }

    /// <summary>Icon rail (medium, closed): only the menu button and the page icons.</summary>
    private void UpdatePaneContent()
    {
        bool rail = _mode == ShellMode.Medium && !Shell.IsPaneOpen;
        Brand.IsVisible = !rail;
        VersionText.IsVisible = !rail;
        Nav.Classes.Set("rail", rail);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is null) return;
        if (DataContext is MainWindowViewModel vm) vm.Services.Dialogs.Attach(_topLevel, DialogLayer);
        _topLevel.BackRequested += OnBackRequested;
        if (_topLevel.InsetsManager is { } insets)
        {
            insets.SafeAreaChanged += OnSafeAreaChanged;
            Padding = insets.SafeAreaPadding;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_topLevel is null) return;
        _topLevel.BackRequested -= OnBackRequested;
        if (_topLevel.InsetsManager is { } insets) insets.SafeAreaChanged -= OnSafeAreaChanged;
        _topLevel = null;
    }

    /// <summary>Keeps the content clear of the status / navigation bars and camera cut-outs (Android).</summary>
    private void OnSafeAreaChanged(object? sender, Avalonia.Controls.Platform.SafeAreaChangedArgs e) => Padding = e.SafeAreaPadding;

    /// <summary>
    /// Android back button / gesture: closes the dialog, then the menu, then drawers of the page, then goes back to
    /// the Sheets page; on the Sheets page the system handles it (leaves the application).
    /// </summary>
    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.Services.Dialogs.CancelDialog()) { e.Handled = true; return; }
        if (Shell.IsPaneOpen && Shell.DisplayMode != SplitViewDisplayMode.Inline)
        {
            Shell.IsPaneOpen = false;
            e.Handled = true;
            return;
        }
        if (Adaptive.CloseOpenDrawer(this)) { e.Handled = true; return; }
        if (vm.CurrentPage != vm.SheetsSection)
        {
            vm.CurrentPage = vm.SheetsSection;
            e.Handled = true;
        }
    }
}
