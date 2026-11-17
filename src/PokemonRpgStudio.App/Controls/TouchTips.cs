using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace PokemonRpgStudio.App.Controls;

/// <summary>
/// Tooltips only open on mouse hover, so a touch screen never shows them: the ability effects, move details…
/// On phones, tapping an element that has a tooltip (and isn't itself a button or an input) shows the same
/// content in a flyout instead; a tap elsewhere closes it.
/// </summary>
public static class TouchTips
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        // Every tap bubbles up to the TopLevel: one handler for the whole app.
        InputElement.TappedEvent.AddClassHandler<TopLevel>(OnTapped, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static void OnTapped(TopLevel _, TappedEventArgs e)
    {
        for (var v = e.Source as Visual; v != null; v = v.GetVisualParent())
        {
            // A tap on a button, a field… is its own action: its tooltip is only a hint.
            if (v is Button or TextBox or ComboBox or MenuItem or Slider or ListBoxItem or TopLevel) return;
            if (v is Control c && ToolTip.GetTip(c) is { } tip)
            {
                Show(c, tip);
                return;
            }
        }
    }

    private static void Show(Control target, object tip)
    {
        object content = tip switch
        {
            string text => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 },
            Control { Parent: not null } => null!, // already shown in a tooltip: can't be moved
            _ => tip, // a view model (move details…): shown with its data template
        };
        if (content is null)
        {
            ToolTip.SetIsOpen(target, true);
            return;
        }
        var flyout = new Flyout
        {
            Content = content,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard,
        };
        flyout.ShowAt(target);
    }
}
