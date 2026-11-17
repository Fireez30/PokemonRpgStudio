using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using PokemonRpgStudio.App.ViewModels;
using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.App.Controls;

/// <summary>
/// Drawing surface of the whiteboard (DataContext: <see cref="WhiteboardViewModel"/>). Left button: current tool,
/// inside the cell under the cursor; right / middle button (or left button outside the grid): pan; wheel: zoom
/// around the cursor. With Select, items can be dragged into another cell, and dragging the empty part of a cell
/// onto another cell swaps them.
/// </summary>
public sealed class WhiteboardCanvas : Control
{
    private enum DragMode { None, Pan, Draw, Shape, Move, Resize, Erase, MoveCell }

    private static readonly IPen SelectionPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#7C4DDB")), 1.5, new ImmutableDashStyle(new double[] { 4, 3 }, 0));
    private static readonly IBrush GhostBrush = new ImmutableSolidColorBrush(Color.Parse("#407C4DDB"));
    private static readonly IBrush HandleBrush = new ImmutableSolidColorBrush(Color.Parse("#7C4DDB"));
    private const double HandleSize = 12;

    private WhiteboardViewModel? _vm;
    private Vector _offset = new(40, 40);
    private double _zoom = 1;

    private DragMode _drag;
    private Point _panStart;
    private Vector _panOffsetStart;
    private BoardPoint _start;
    private BoardPoint _last;
    private BoardItem? _draft;
    /// <summary>Cell of the item being drawn, or cell being dragged.</summary>
    private int _dragCell = -1;
    /// <summary>Cell under the cursor while an item or a cell is dragged.</summary>
    private int _dropTarget = -1;
    private bool _changed;

    public WhiteboardCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    private BoardPoint ToBoard(Point p) => new((p.X - _offset.X) / _zoom, (p.Y - _offset.Y) / _zoom);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
        {
            _vm.Changed -= InvalidateVisual;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.FitViewRequested = null;
        }
        _vm = DataContext as WhiteboardViewModel;
        if (_vm != null)
        {
            _vm.Changed += InvalidateVisual;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.FitViewRequested = FitView;
        }
        UpdateCursor();
        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WhiteboardViewModel.SelectedTool)) UpdateCursor();
        if (e.PropertyName == nameof(WhiteboardViewModel.Selected)) InvalidateVisual();
    }

    private void UpdateCursor() => Cursor = new Cursor(_vm?.SelectedTool.Tool switch
    {
        BoardTool.Select or null => StandardCursorType.Arrow,
        BoardTool.Text => StandardCursorType.Ibeam,
        BoardTool.Eraser => StandardCursorType.Hand,
        _ => StandardCursorType.Cross,
    });

    /// <summary>Shows the whole grid.</summary>
    private void FitView()
    {
        if (_vm?.Board.Extent() is not { } extent || Bounds.Width < 1 || Bounds.Height < 1)
        {
            _zoom = 1;
            _offset = new Vector(40, 40);
        }
        else
        {
            const double margin = 40;
            double w = Math.Max(extent.Width, 50), h = Math.Max(extent.Height, 50);
            _zoom = Math.Clamp(Math.Min((Bounds.Width - 2 * margin) / w, (Bounds.Height - 2 * margin) / h), 0.1, 4);
            _offset = new Vector(
                Bounds.Width / 2 - (extent.X + extent.Width / 2) * _zoom,
                Bounds.Height / 2 - (extent.Y + extent.Height / 2) * _zoom);
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(WhiteboardRenderer.BoardBrush, new Rect(size));
        if (_vm is null) return;
        using (context.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset)))
        {
            var visible = new Rect(-_offset.X / _zoom, -_offset.Y / _zoom, size.Width / _zoom, size.Height / _zoom);
            var overlay = new WhiteboardRenderer.Overlay(_vm.ActiveCell, _draft, _dragCell,
                _drag == DragMode.Move && _changed ? _vm.Selected : null, _dropTarget);
            WhiteboardRenderer.Draw(context, _vm.Board, _vm.Store, visible, _zoom, overlay);
            if (_drag == DragMode.MoveCell && _dragCell >= 0)
            {
                // Ghost of the dragged cell, following the cursor.
                var f = _vm.Board.CellRect(_dragCell);
                context.FillRectangle(GhostBrush, new Rect(f.X + _last.X - _start.X, f.Y + _last.Y - _start.Y, f.Width, f.Height));
            }
        }
        if (_vm.Selected is { } selected)
        {
            var r = ScreenBounds(selected);
            context.DrawRectangle(null, SelectionPen, r);
            context.FillRectangle(HandleBrush, HandleRect(r));
        }
    }

    /// <summary>Selection frame of an item, in screen coordinates.</summary>
    private Rect ScreenBounds(BoardItem item)
    {
        var b = item.Bounds;
        int index = _vm!.Board.IndexOfCellWith(item);
        if (index >= 0)
        {
            var c = _vm.Board.CellRect(index);
            b = b with { X = b.X + c.X, Y = b.Y + c.Y };
        }
        double pad = item.UsesPoints ? item.Thickness / 2 * _zoom + 4 : 4;
        return new Rect(b.X * _zoom + _offset.X, b.Y * _zoom + _offset.Y, b.Width * _zoom, b.Height * _zoom).Inflate(pad);
    }

    private static Rect HandleRect(Rect selection) =>
        new(selection.Right - HandleSize / 2, selection.Bottom - HandleSize / 2, HandleSize, HandleSize);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null || _drag != DragMode.None) return;
        Focus();
        var point = e.GetCurrentPoint(this);
        var pos = point.Position;
        var p = ToBoard(pos);
        _start = _last = p;
        _changed = false;
        e.Handled = true;

        if (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            StartPan(pos, e);
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;

        var vm = _vm;
        string color = vm.SelectedColor.Hex;
        double thickness = vm.SelectedSize.Thickness;
        int cell = vm.Board.CellAt(p);
        if (cell >= 0) vm.ActiveCell = cell;
        var tool = vm.SelectedTool.Tool;
        bool resizing = tool == BoardTool.Select && vm.Selected is { } current && HandleRect(ScreenBounds(current)).Inflate(3).Contains(pos);

        if (cell < 0 && tool is not BoardTool.Select and not BoardTool.Eraser)
        {
            // Nothing to draw on outside the grid.
            StartPan(pos, e);
            return;
        }
        // Drawing tools work in the coordinates of the cell under the cursor.
        if (cell >= 0 && tool is not BoardTool.Select and not BoardTool.Eraser)
        {
            _dragCell = cell;
            _start = _last = p = vm.Board.ToLocal(cell, p);
        }

        switch (tool)
        {
            case BoardTool.Select:
                if (resizing)
                {
                    _drag = DragMode.Resize;
                    break;
                }
                var hit = vm.Board.HitTest(p, 4 / _zoom);
                vm.Selected = hit?.Item;
                if (hit is not { Item: var item })
                {
                    if (cell < 0)
                    {
                        StartPan(pos, e);
                        return;
                    }
                    // Empty part of a cell: dragging it onto another cell swaps them.
                    _drag = DragMode.MoveCell;
                    _dragCell = cell;
                    Cursor = new Cursor(StandardCursorType.DragMove);
                    break;
                }
                if (e.ClickCount == 2 && item.Kind == BoardItemKind.Text)
                {
                    _ = vm.EditTextAsync(item);
                    return;
                }
                _drag = DragMode.Move;
                break;
            case BoardTool.Pen:
                _draft = new BoardItem { Kind = BoardItemKind.Stroke, Color = color, Thickness = thickness, Points = { p } };
                _drag = DragMode.Draw;
                break;
            case BoardTool.Eraser:
                _drag = DragMode.Erase;
                EraseAt(p);
                break;
            case BoardTool.Line:
            case BoardTool.Arrow:
                _draft = new BoardItem
                {
                    Kind = vm.SelectedTool.Tool == BoardTool.Line ? BoardItemKind.Line : BoardItemKind.Arrow,
                    Color = color, Thickness = thickness, Points = { p, p },
                };
                _drag = DragMode.Shape;
                break;
            case BoardTool.Rectangle:
            case BoardTool.Ellipse:
                _draft = new BoardItem
                {
                    Kind = vm.SelectedTool.Tool == BoardTool.Rectangle ? BoardItemKind.Rectangle : BoardItemKind.Ellipse,
                    Color = color, Thickness = thickness, Filled = vm.Filled, X = p.X, Y = p.Y,
                };
                _drag = DragMode.Shape;
                break;
            case BoardTool.Text:
                _dragCell = -1;
                _ = vm.AddTextAsync(cell, p);
                return;
        }
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    private void StartPan(Point pos, PointerPressedEventArgs e)
    {
        _drag = DragMode.Pan;
        _panStart = pos;
        _panOffsetStart = _offset;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
    }

    /// <summary>Remembers the board for Undo once, at the first change of a drag.</summary>
    private void BeginChange()
    {
        if (_changed) return;
        _changed = true;
        _vm!.Checkpoint();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm is null || _drag == DragMode.None) return;
        var pos = e.GetPosition(this);
        var p = ToBoard(pos);
        var board = _vm.Board;
        if (_drag is DragMode.Draw or DragMode.Shape && _dragCell >= 0) p = board.ToLocal(_dragCell, p);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (_drag)
        {
            case DragMode.Pan:
                _offset = _panOffsetStart + (pos - _panStart);
                break;
            case DragMode.Draw:
                var lastPoint = _draft!.Points[^1];
                if (Math.Abs(p.X - lastPoint.X) + Math.Abs(p.Y - lastPoint.Y) >= 1.5 / _zoom) _draft.Points.Add(p);
                break;
            case DragMode.Shape:
                UpdateShape(_draft!, p, shift);
                break;
            case DragMode.Move when _vm.Selected is { } moved:
                BeginChange();
                moved.Move(p.X - _last.X, p.Y - _last.Y);
                int target = board.CellAt(p);
                _dropTarget = target >= 0 && target != board.IndexOfCellWith(moved) ? target : -1;
                break;
            case DragMode.Resize when _vm.Selected is { } resized:
                BeginChange();
                int resizedCell = board.IndexOfCellWith(resized);
                Resize(resized, resizedCell >= 0 ? board.ToLocal(resizedCell, p) : p, keepRatio: shift || resized.Kind == BoardItemKind.Image);
                break;
            case DragMode.MoveCell:
                int over = board.CellAt(p);
                _dropTarget = over != _dragCell ? over : -1;
                break;
            case DragMode.Erase:
                EraseAt(p);
                break;
        }
        _last = p;
        InvalidateVisual();
    }

    private void UpdateShape(BoardItem draft, BoardPoint p, bool shift)
    {
        double dx = p.X - _start.X, dy = p.Y - _start.Y;
        if (draft.UsesPoints)
        {
            if (shift)
            {
                // Snap to horizontal, vertical or 45° diagonal.
                double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
                double length = Math.Sqrt(dx * dx + dy * dy);
                p = new BoardPoint(_start.X + Math.Cos(angle) * length, _start.Y + Math.Sin(angle) * length);
            }
            draft.Points[1] = p;
            return;
        }
        if (shift)
        {
            double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            p = new BoardPoint(_start.X + Math.Sign(dx) * side, _start.Y + Math.Sign(dy) * side);
        }
        var r = BoardRect.FromPoints(_start, p);
        (draft.X, draft.Y, draft.Width, draft.Height) = (r.X, r.Y, r.Width, r.Height);
    }

    /// <summary>Moves the bottom-right corner of <paramref name="item"/> to <paramref name="corner"/>.</summary>
    private void Resize(BoardItem item, BoardPoint corner, bool keepRatio)
    {
        var from = item.Bounds;
        double min = 8 / _zoom;
        double w = Math.Max(min, corner.X - from.X), h = Math.Max(min, corner.Y - from.Y);
        if (keepRatio && from.Width > 0 && from.Height > 0)
        {
            double s = Math.Max(w / from.Width, h / from.Height);
            (w, h) = (from.Width * s, from.Height * s);
        }
        // A horizontal / vertical line keeps a zero height / width.
        if (from.Width == 0) w = 0;
        if (from.Height == 0) h = 0;
        item.Resize(from, new BoardRect(from.X, from.Y, w, h));
    }

    private void EraseAt(BoardPoint p)
    {
        if (_vm!.Board.HitTest(p, 6 / _zoom) is not { Item: var item }) return;
        BeginChange();
        _vm.RemoveItem(item);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == DragMode.None) return;
        EndDrag(commit: true);
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag != DragMode.None) EndDrag(commit: true);
    }

    /// <summary>
    /// Ends the current drag; <paramref name="commit"/> false drops the stroke / shape being drawn and leaves a
    /// dragged item or cell where it came from.
    /// </summary>
    private void EndDrag(bool commit)
    {
        var draft = _draft;
        var mode = _drag;
        int dragCell = _dragCell, dropTarget = _dropTarget;
        _drag = DragMode.None;
        _draft = null;
        _dragCell = _dropTarget = -1;
        UpdateCursor();
        if (_vm is null)
            return;
        if (mode == DragMode.MoveCell)
        {
            if (commit && dropTarget >= 0) _vm.SwapCells(dragCell, dropTarget);
        }
        else if (commit && draft != null && IsWorthKeeping(draft))
            _vm.AddItem(dragCell, draft);
        else if (_changed)
        {
            if (mode == DragMode.Move && commit && dropTarget >= 0 && _vm.Selected is { } moved) _vm.MoveItemToCell(moved, dropTarget);
            _vm.Commit();
        }
        _changed = false;
        InvalidateVisual();
    }

    private bool IsWorthKeeping(BoardItem item)
    {
        if (item.Kind == BoardItemKind.Stroke) return item.Points.Count > 0;
        var b = item.Bounds;
        double min = 3 / _zoom;
        return item.UsesPoints ? b.Width >= min || b.Height >= min : b.Width >= min && b.Height >= min;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos = e.GetPosition(this);
        double zoom = Math.Clamp(_zoom * Math.Pow(1.15, e.Delta.Y), 0.1, 8);
        // The point under the cursor stays in place.
        _offset = (Vector)pos - ((Vector)pos - _offset) * (zoom / _zoom);
        _zoom = zoom;
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Delete or Key.Back when _vm.HasSelection:
                _vm.DeleteSelected();
                break;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                _vm.Redo();
                break;
            case Key.Z when ctrl:
                _vm.Undo();
                break;
            case Key.Escape:
                if (_drag != DragMode.None) EndDrag(commit: false);
                _vm.Selected = null;
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
