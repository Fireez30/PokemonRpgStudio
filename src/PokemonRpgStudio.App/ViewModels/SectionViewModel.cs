using CommunityToolkit.Mvvm.ComponentModel;
using PokemonRpgStudio.App.Services;

namespace PokemonRpgStudio.App.ViewModels;

/// <summary>One view of a section: the label shown in the section's switch and the page it shows.</summary>
public sealed record SectionTab(string Key, string Label, string Glyph, PageViewModel View)
{
    public string ToolTip => View.Title;
}

/// <summary>
/// Sidebar page grouping several pages shown one at a time (Sheets: list / database view;
/// Tools: dice, type chart, analyzer, whiteboard; Shops: shop / edit view). The selected view is remembered.
/// </summary>
public sealed partial class SectionViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly string _key;
    private readonly string _title;
    private readonly string _glyph;
    private bool _active;
    private SectionTab _lastTab;

    public override string Title => _title;
    public override string Glyph => _glyph;

    public IReadOnlyList<SectionTab> Tabs { get; }

    [ObservableProperty] private SectionTab _selectedTab;

    public PageViewModel CurrentView => (SelectedTab ?? _lastTab).View;

    public SectionViewModel(AppServices services, string key, string title, string glyph, params SectionTab[] tabs)
    {
        _services = services;
        _key = key;
        _title = title;
        _glyph = glyph;
        Tabs = tabs;
        var remembered = services.Settings.SectionViews.TryGetValue(key, out var k) ? k : null;
        _selectedTab = tabs.FirstOrDefault(t => t.Key == remembered) ?? tabs[0];
        _lastTab = _selectedTab;
    }

    public bool Contains(PageViewModel view) => Tabs.Any(t => t.View == view);

    public bool IsShowing(PageViewModel view) => CurrentView == view;

    /// <summary>Switches to the tab showing <paramref name="view"/>.</summary>
    public void Show(PageViewModel view)
    {
        if (Tabs.FirstOrDefault(t => t.View == view) is { } tab) SelectedTab = tab;
    }

    partial void OnSelectedTabChanging(SectionTab value)
    {
        // A list box can briefly clear its selection: keep the current view.
        if (_active && value is not null && value != _lastTab) _lastTab.View.OnDeactivated();
    }

    partial void OnSelectedTabChanged(SectionTab value)
    {
        if (value is null)
        {
            // Never leave the section empty.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (SelectedTab is null) SelectedTab = _lastTab; });
            return;
        }
        if (value == _lastTab) return;
        _lastTab = value;
        OnPropertyChanged(nameof(CurrentView));
        if (_active) value.View.OnActivated();
        _services.Settings.SectionViews[_key] = value.Key;
        _services.SaveSettings();
    }

    public override void OnActivated()
    {
        _active = true;
        CurrentView.OnActivated();
    }

    public override void OnDeactivated()
    {
        _active = false;
        CurrentView.OnDeactivated();
    }
}
