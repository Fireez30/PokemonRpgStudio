using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PokemonRpgStudio.App.ViewModels;

namespace PokemonRpgStudio.App.Views;

/// <summary>
/// Item wiki. Besides the bindings: an item of the list can be dragged with the mouse onto a section
/// (or "Unsorted") of the sections column to move it there. On touch screens the Section picker does it.
/// </summary>
public partial class ItemWikiView : UserControl
{
    private static readonly DataFormat<string> DragFormat = DataFormat.CreateStringApplicationFormat("pokemon-rpg-studio-wiki-item");
    private const double DragThreshold = 6;

    private WikiItemVm? _pressedItem;
    private Point _pressedAt;
    private ListBoxItem? _dropTarget;

    public ItemWikiView()
    {
        InitializeComponent();
        SplitLayout.Attach(Split, DetailsPane, minWidth: 760, detailsWidth: 380, stackedHeight: 420);

        // handledEventsToo: the ListBoxItem handles the press to select the item.
        ItemList.AddHandler(PointerPressedEvent, OnItemPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        ItemList.AddHandler(PointerMovedEvent, OnItemMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        ItemList.AddHandler(PointerReleasedEvent, (_, _) => _pressedItem = null, RoutingStrategies.Bubble, handledEventsToo: true);

        SectionList.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        SectionList.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropTarget(null));
        SectionList.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnItemPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedItem = null;
        if (e.Pointer.Type != PointerType.Mouse || !e.GetCurrentPoint(ItemList).Properties.IsLeftButtonPressed) return;
        // Not from the scroll bar.
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: WikiItemVm item }) return;
        _pressedItem = item;
        _pressedAt = e.GetPosition(ItemList);
    }

    private async void OnItemMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedItem is not { } item) return;
        if (!e.GetCurrentPoint(ItemList).Properties.IsLeftButtonPressed) { _pressedItem = null; return; }
        var delta = e.GetPosition(ItemList) - _pressedAt;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;
        _pressedItem = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DragFormat, item.Item.Id));
        try { await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move); }
        finally { SetDropTarget(null); }
    }

    private static ListBoxItem? SectionTarget(DragEventArgs e) =>
        (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: WikiSectionVm { AcceptsItems: true } } target
            ? target
            : null;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var target = e.DataTransfer.Contains(DragFormat) ? SectionTarget(e) : null;
        e.DragEffects = target is null ? DragDropEffects.None : DragDropEffects.Move;
        SetDropTarget(target);
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropTarget(null);
        if (DataContext is not ItemWikiViewModel vm || e.DataTransfer.TryGetValue(DragFormat) is not { } id) return;
        if (SectionTarget(e) is { DataContext: WikiSectionVm section }) vm.DropItem(id, section);
        e.Handled = true;
    }

    private void SetDropTarget(ListBoxItem? target)
    {
        if (_dropTarget == target) return;
        _dropTarget?.Classes.Remove("droptarget");
        _dropTarget = target;
        _dropTarget?.Classes.Add("droptarget");
    }
}
