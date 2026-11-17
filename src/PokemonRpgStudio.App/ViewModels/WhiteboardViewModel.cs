using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Controls;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.ViewModels;

public enum BoardTool { Select, Pen, Eraser, Line, Arrow, Rectangle, Ellipse, Text }

public sealed record BoardToolOption(BoardTool Tool, string Glyph, string Label, string Hint);

public sealed record BoardColorOption(string Hex)
{
    public IBrush Brush { get; } = new SolidColorBrush(Color.Parse(Hex));
}

public sealed record CellSizeOption(double Size, string Label)
{
    public override string ToString() => Label;
}

public sealed record BoardSizeOption(double Thickness, string Label)
{
    public double DotSize => Math.Min(18, Thickness + 4);
    /// <summary>Font size of the texts written with this size.</summary>
    public double FontSize => 14 + Thickness * 4;
}

/// <summary>
/// Whiteboard of the Tools section: a zoomable grid of square cells, each with its own free drawing, shapes,
/// texts, images and Pokémon tokens (items can be dragged from one cell to another, and whole cells swapped).
/// Saved automatically after every change.
/// </summary>
public sealed partial class WhiteboardViewModel : PageViewModel
{
    private const int MaxUndo = 100;
    private readonly AppServices _services;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public override string Title => "Whiteboard";
    public override string Glyph => "🖍";

    public Whiteboard Board { get; }
    public WhiteboardStore Store { get; }

    public IReadOnlyList<BoardToolOption> Tools { get; } = new BoardToolOption[]
    {
        new(BoardTool.Select, "↖", "Select", "Click an item to select it, drag it (into another cell too) to move it, drag the corner square to resize it, double-click a text to edit it. Drag the empty part of a cell onto another cell to swap them."),
        new(BoardTool.Pen, "✏", "Pen", "Drag to draw freely."),
        new(BoardTool.Eraser, "⌫", "Eraser", "Click or drag over items to delete them."),
        new(BoardTool.Line, "╱", "Line", "Drag to draw a line (Shift: horizontal, vertical or diagonal)."),
        new(BoardTool.Arrow, "➜", "Arrow", "Drag to draw an arrow (Shift: horizontal, vertical or diagonal)."),
        new(BoardTool.Rectangle, "▭", "Rectangle", "Drag to draw a rectangle (Shift: square)."),
        new(BoardTool.Ellipse, "◯", "Ellipse", "Drag to draw an ellipse (Shift: circle)."),
        new(BoardTool.Text, "T", "Text", "Click where the text goes."),
    };

    public IReadOnlyList<BoardColorOption> Colors { get; } = new[]
    {
        "#1d1d1d", "#868e96", "#ffffff", "#e03131", "#f08c00", "#fab005", "#2f9e44", "#1971c2", "#7048e8", "#8d5524",
    }.Select(h => new BoardColorOption(h)).ToList();

    public IReadOnlyList<CellSizeOption> CellSizes { get; } = new CellSizeOption[]
    {
        new(250, "Small cells"), new(500, "Medium cells"), new(800, "Large cells"), new(1200, "Huge cells"),
    };

    public IReadOnlyList<BoardSizeOption> Sizes { get; } = new BoardSizeOption[] { new(2, "Thin"), new(4, "Medium"), new(8, "Thick") };

    [ObservableProperty] private BoardToolOption _selectedTool;
    [ObservableProperty] private BoardColorOption _selectedColor;
    [ObservableProperty] private BoardSizeOption _selectedSize;
    [ObservableProperty] private bool _filled;
    [ObservableProperty] private string? _pokemonToAdd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand), nameof(BringToFrontCommand), nameof(SendToBackCommand))]
    private BoardItem? _selected;

    public bool HasSelection => Selected is not null;

    /// <summary>Index of the current cell (highlighted; where added images and tokens go).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCellCommand))]
    private int _activeCell;

    public decimal? ColumnCount
    {
        get => Board.Columns;
        set => _ = ResizeGridAsync(Board.Rows, (int)(value ?? 1));
    }

    public decimal? RowCount
    {
        get => Board.Rows;
        set => _ = ResizeGridAsync((int)(value ?? 1), Board.Columns);
    }

    public CellSizeOption? SelectedCellSize
    {
        get => CellSizes.FirstOrDefault(o => o.Size == Board.CellSize);
        set
        {
            if (value is null || value.Size == Board.CellSize) return;
            Checkpoint();
            Board.CellSize = value.Size;
            OnPropertyChanged();
            Commit();
            FitViewRequested?.Invoke();
        }
    }

    /// <summary>Changes the number of rows / columns (asks first when cells with items would go).</summary>
    private async Task ResizeGridAsync(int rows, int columns)
    {
        rows = Math.Clamp(rows, 1, Whiteboard.MaxCells);
        columns = Math.Clamp(columns, 1, Whiteboard.MaxCells);
        if (rows != Board.Rows || columns != Board.Columns)
        {
            int lost = Board.ItemsOutside(rows, columns);
            if (lost == 0 || await _services.Dialogs.ConfirmAsync("Shrink the grid",
                    $"The removed cells hold {lost} item(s). Remove them? (Undo can bring them back.)", "Remove"))
            {
                Checkpoint();
                if (Selected is { } s && Board.IndexOfCellWith(s) is var i && (i / Board.Columns >= rows || i % Board.Columns >= columns))
                    Selected = null;
                Board.Resize(rows, columns);
                ActiveCell = Math.Clamp(ActiveCell, 0, Board.Cells.Count - 1);
                Commit();
                FitViewRequested?.Invoke();
            }
        }
        // Refreshes the row / column fields (also when the change was cancelled).
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(ColumnCount));
    }

    public bool ShowGrid
    {
        get => Board.ShowGrid;
        set
        {
            if (Board.ShowGrid == value) return;
            Board.ShowGrid = value;
            OnPropertyChanged();
            Save();
            Redraw();
        }
    }

    public IReadOnlyList<string> PokemonNames { get; private set; }

    /// <summary>Asks the canvas to redraw.</summary>
    public event Action? Changed;

    /// <summary>Set by the canvas: zooms to show the whole grid.</summary>
    public Action? FitViewRequested { get; set; }

    public WhiteboardViewModel(AppServices services)
    {
        _services = services;
        Store = new WhiteboardStore(services.Library.Paths.Whiteboard);
        Board = Store.Load();
        try { Store.DeleteUnusedImages(Board); }
        catch (Exception) { /* cleanup only */ }
        _selectedTool = Tools[1];
        _selectedColor = Colors[0];
        _selectedSize = Sizes[1];
        PokemonNames = services.Data.Pokemon.Select(p => p.Name).ToList();
        services.GameDataChanged += () =>
        {
            PokemonNames = _services.Data.Pokemon.Select(p => p.Name).ToList();
            OnPropertyChanged(nameof(PokemonNames));
            Redraw();
        };
    }

    public void Redraw() => Changed?.Invoke();

    /// <summary>Remembers the board before a change (for Undo).</summary>
    public void Checkpoint()
    {
        _undo.Push(Board.ToJson());
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Take(MaxUndo).Reverse().ToList();
            _undo.Clear();
            foreach (var s in keep) _undo.Push(s);
        }
        _redo.Clear();
        UpdateUndoState();
    }

    /// <summary>Saves and redraws after a change.</summary>
    public void Commit()
    {
        Save();
        Redraw();
    }

    /// <summary>Adds <paramref name="item"/> (cell coordinates) to the cell <paramref name="cell"/>.</summary>
    public void AddItem(int cell, BoardItem item, bool select = false)
    {
        if (cell < 0 || cell >= Board.Cells.Count) return;
        Checkpoint();
        Board.Cells[cell].Items.Add(item);
        ActiveCell = cell;
        if (select) Selected = item;
        Commit();
    }

    public void RemoveItem(BoardItem item)
    {
        foreach (var cell in Board.Cells) cell.Items.Remove(item);
        if (Selected == item) Selected = null;
    }

    private void Save()
    {
        try { Store.Save(Board); }
        catch (Exception ex) { _services.Dialogs.Notify("Could not save the whiteboard: " + ex.Message, isError: true); }
    }

    private void UpdateUndoState()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndo() => _undo.Count > 0;
    private bool CanRedo() => _redo.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo() => Restore(_undo, _redo);

    [RelayCommand(CanExecute = nameof(CanRedo))]
    public void Redo() => Restore(_redo, _undo);

    private void Restore(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0) return;
        to.Push(Board.ToJson());
        Board.CopyFrom(Whiteboard.FromJson(from.Pop()));
        Selected = null;
        ActiveCell = Math.Clamp(ActiveCell, 0, Board.Cells.Count - 1);
        OnPropertyChanged(nameof(ColumnCount));
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(SelectedCellSize));
        OnPropertyChanged(nameof(ShowGrid));
        UpdateUndoState();
        Commit();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void DeleteSelected()
    {
        if (Selected is not { } item) return;
        Checkpoint();
        RemoveItem(item);
        Commit();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void BringToFront() => Reorder(toFront: true);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SendToBack() => Reorder(toFront: false);

    private void Reorder(bool toFront)
    {
        if (Selected is not { } item || Board.IndexOfCellWith(item) is not (>= 0 and var index)) return;
        var items = Board.Cells[index].Items;
        Checkpoint();
        items.Remove(item);
        if (toFront) items.Add(item);
        else items.Insert(0, item);
        Commit();
    }

    /// <summary>Moves an item dragged out of its cell into the cell <paramref name="target"/>.</summary>
    public void MoveItemToCell(BoardItem item, int target)
    {
        Board.MoveItemToCell(item, target);
        ActiveCell = target;
    }

    /// <summary>Swaps the contents of two cells (empty part of a cell dragged onto another).</summary>
    public void SwapCells(int a, int b)
    {
        if (a == b) return;
        Checkpoint();
        Board.SwapCells(a, b);
        ActiveCell = b;
        Commit();
    }

    private bool CanClearCell() => ActiveCell >= 0 && ActiveCell < Board.Cells.Count;

    [RelayCommand(CanExecute = nameof(CanClearCell))]
    private void ClearCell()
    {
        if (!CanClearCell() || Board.Cells[ActiveCell].Items is not { Count: > 0 } items) return;
        Checkpoint();
        if (Selected is { } s && items.Contains(s)) Selected = null;
        items.Clear();
        Commit();
    }

    partial void OnActiveCellChanged(int value) => Redraw();

    [RelayCommand]
    private void FitView() => FitViewRequested?.Invoke();

    [RelayCommand]
    private async Task Clear()
    {
        if (!Board.AllItems.Any()) return;
        if (!await _services.Dialogs.ConfirmAsync("Clear the whiteboard", "Remove everything from every cell? (Undo can bring it back.)", "Clear")) return;
        Checkpoint();
        foreach (var cell in Board.Cells) cell.Items.Clear();
        Selected = null;
        Commit();
    }

    [RelayCommand]
    private async Task AddImage()
    {
        var file = await _services.Dialogs.OpenFileAsync("Add an image to the whiteboard", DialogService.ImageFiles);
        if (file is null) return;
        try
        {
            var name = Store.ImportImage(file);
            var bitmap = BitmapCache.Load(Store.ImagePath(name), 0);
            if (bitmap is null)
            {
                _services.Dialogs.Notify("This image can't be read.", isError: true);
                return;
            }
            // Large images fit in the cell, proportions kept.
            double w = bitmap.Size.Width, h = bitmap.Size.Height;
            double scale = Math.Min(1, Board.CellSize * 0.95 / Math.Max(w, h));
            AddCentered(new BoardItem { Kind = BoardItemKind.Image, ImageFile = name }, w * scale, h * scale);
        }
        catch (Exception ex)
        {
            _services.Dialogs.Notify("Could not add the image: " + ex.Message, isError: true);
        }
    }

    [RelayCommand]
    private void AddPokemon()
    {
        var species = _services.Data.FindPokemon(PokemonToAdd);
        if (species is null)
        {
            _services.Dialogs.Notify("Pick a Pokémon first.");
            return;
        }
        double size = Math.Min(Board.GridSize * 2, Board.CellSize * 0.9);
        AddCentered(new BoardItem { Kind = BoardItemKind.Image, Pokemon = species.Name }, size, size);
    }

    /// <summary>Adds an item in the middle of the current cell.</summary>
    private void AddCentered(BoardItem item, double width, double height)
    {
        int cell = CanClearCell() ? ActiveCell : 0;
        item.X = (Board.CellSize - width) / 2;
        item.Y = (Board.CellSize - height) / 2;
        item.Width = width;
        item.Height = height;
        SelectedTool = Tools[0];
        AddItem(cell, item, select: true);
    }

    public async Task AddTextAsync(int cell, BoardPoint at)
    {
        var text = await _services.Dialogs.PromptAsync("Add a text", "Text", confirmText: "Add");
        if (text is null) return;
        var item = new BoardItem
        {
            Kind = BoardItemKind.Text, Text = text, X = at.X, Y = at.Y,
            Color = SelectedColor.Hex, FontSize = SelectedSize.FontSize,
        };
        WhiteboardRenderer.FitText(item);
        AddItem(cell, item);
    }

    public async Task EditTextAsync(BoardItem item)
    {
        var text = await _services.Dialogs.PromptAsync("Edit the text", "Text", item.Text ?? "", "Save");
        if (text is null || text == item.Text) return;
        Checkpoint();
        item.Text = text;
        WhiteboardRenderer.FitText(item);
        Commit();
    }

    partial void OnSelectedToolChanged(BoardToolOption value)
    {
        if (value.Tool != BoardTool.Select) Selected = null;
        Redraw();
    }

    // Changing the colour, size or fill also changes the selected item.
    partial void OnSelectedColorChanged(BoardColorOption value) =>
        ChangeSelected(i => i.Kind != BoardItemKind.Image && i.Color != value.Hex, i => i.Color = value.Hex);

    partial void OnSelectedSizeChanged(BoardSizeOption value) =>
        ChangeSelected(i => i.Kind is not (BoardItemKind.Image or BoardItemKind.Text) && i.Thickness != value.Thickness,
            i => i.Thickness = value.Thickness);

    partial void OnFilledChanged(bool value) =>
        ChangeSelected(i => i.Kind is BoardItemKind.Rectangle or BoardItemKind.Ellipse && i.Filled != value, i => i.Filled = value);

    private void ChangeSelected(Func<BoardItem, bool> applies, Action<BoardItem> change)
    {
        if (Selected is not { } item || !applies(item)) return;
        Checkpoint();
        change(item);
        Commit();
    }

    [RelayCommand]
    private async Task ExportPng()
    {
        try
        {
            if (!await _services.Dialogs.SaveFileAsync("Export the whiteboard", "whiteboard.png", DialogService.PngFiles, path =>
                {
                    using var bitmap = WhiteboardRenderer.RenderPng(Board, Store);
                    bitmap.Save(path);
                })) return;
            _services.Dialogs.Notify("Whiteboard exported.");
        }
        catch (Exception ex)
        {
            _services.Dialogs.Notify("Could not export the whiteboard: " + ex.Message, isError: true);
        }
    }

    public override void OnDeactivated() => Save();
}
