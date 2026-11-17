using System.Text.Json;
using System.Text.Json.Serialization;
using PokemonRpgStudio.Core.Json;

namespace PokemonRpgStudio.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<BoardItemKind>))]
public enum BoardItemKind { Stroke, Line, Arrow, Rectangle, Ellipse, Text, Image }

public readonly record struct BoardPoint(double X, double Y);

public readonly record struct BoardRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Contains(BoardPoint p, double tolerance = 0) =>
        p.X >= X - tolerance && p.X <= Right + tolerance && p.Y >= Y - tolerance && p.Y <= Bottom + tolerance;

    public static BoardRect FromPoints(BoardPoint a, BoardPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public BoardRect Union(BoardRect other)
    {
        double x = Math.Min(X, other.X), y = Math.Min(Y, other.Y);
        return new BoardRect(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
    }
}

/// <summary>
/// One element of the whiteboard. Strokes, lines and arrows use <see cref="Points"/> (lines and arrows: start and end);
/// rectangles, ellipses, texts and images use <see cref="X"/>, <see cref="Y"/>, <see cref="Width"/>, <see cref="Height"/>.
/// </summary>
public sealed class BoardItem
{
    public BoardItemKind Kind { get; set; }
    public string Color { get; set; } = "#1d1d1d";
    public double Thickness { get; set; } = 3;
    public bool Filled { get; set; }
    public List<BoardPoint> Points { get; set; } = new();
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? Text { get; set; }
    public double FontSize { get; set; } = 24;
    /// <summary>Image file name in the whiteboard's <c>images</c> folder.</summary>
    public string? ImageFile { get; set; }
    /// <summary>Pokémon whose sprite is shown (token), when <see cref="ImageFile"/> is not set.</summary>
    public string? Pokemon { get; set; }

    public bool UsesPoints => Kind is BoardItemKind.Stroke or BoardItemKind.Line or BoardItemKind.Arrow;

    public BoardRect Bounds
    {
        get
        {
            if (!UsesPoints) return new BoardRect(X, Y, Width, Height);
            if (Points.Count == 0) return default;
            double minX = Points.Min(p => p.X), minY = Points.Min(p => p.Y);
            return new BoardRect(minX, minY, Points.Max(p => p.X) - minX, Points.Max(p => p.Y) - minY);
        }
    }

    public void Move(double dx, double dy)
    {
        if (UsesPoints)
            for (int i = 0; i < Points.Count; i++) Points[i] = new BoardPoint(Points[i].X + dx, Points[i].Y + dy);
        else
        {
            X += dx;
            Y += dy;
        }
    }

    /// <summary>Stretches the item from <paramref name="from"/> to <paramref name="to"/> (texts keep their proportions).</summary>
    public void Resize(BoardRect from, BoardRect to)
    {
        double sx = from.Width > 0 ? to.Width / from.Width : 1;
        double sy = from.Height > 0 ? to.Height / from.Height : 1;
        if (UsesPoints)
        {
            for (int i = 0; i < Points.Count; i++)
                Points[i] = new BoardPoint(to.X + (Points[i].X - from.X) * sx, to.Y + (Points[i].Y - from.Y) * sy);
            return;
        }
        X = to.X;
        Y = to.Y;
        if (Kind == BoardItemKind.Text)
        {
            double s = Math.Max(0.05, Math.Max(sx, sy));
            FontSize = Math.Clamp(FontSize * s, 6, 400);
            Width = from.Width * s;
            Height = from.Height * s;
            return;
        }
        Width = Math.Max(4, to.Width);
        Height = Math.Max(4, to.Height);
    }

    /// <summary>True when <paramref name="p"/> touches the item (strokes and lines: near the line; others: inside).</summary>
    public bool HitTest(BoardPoint p, double tolerance)
    {
        if (!UsesPoints) return Bounds.Contains(p, tolerance);
        double reach = Thickness / 2 + tolerance;
        if (Points.Count == 1) return Distance(p, Points[0]) <= reach;
        for (int i = 1; i < Points.Count; i++)
            if (DistanceToSegment(p, Points[i - 1], Points[i]) <= reach) return true;
        return false;
    }

    private static double Distance(BoardPoint a, BoardPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public static double DistanceToSegment(BoardPoint p, BoardPoint a, BoardPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 == 0) return Distance(p, a);
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return Distance(p, new BoardPoint(a.X + t * dx, a.Y + t * dy));
    }
}

/// <summary>One cell of the whiteboard grid: its items use coordinates relative to the cell's top-left corner.</summary>
public sealed class BoardCell
{
    /// <summary>Drawn bottom to top.</summary>
    public List<BoardItem> Items { get; set; } = new();

    /// <summary>Topmost item touched by <paramref name="p"/> (cell coordinates).</summary>
    public BoardItem? HitTest(BoardPoint p, double tolerance)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
            if (Items[i].HitTest(p, tolerance)) return Items[i];
        return null;
    }
}

/// <summary>
/// The whiteboard of the Tools section (battle maps, positions, notes…): a grid of <see cref="Rows"/> ×
/// <see cref="Columns"/> square cells of <see cref="CellSize"/>, stored row by row. What is drawn in a cell stays
/// inside it.
/// </summary>
public sealed class Whiteboard
{
    public const int MaxCells = 12;
    public const int DefaultColumns = 3, DefaultRows = 2;
    public const double DefaultCellSize = 500;

    public List<BoardCell> Cells { get; set; } = new();
    public int Columns { get; set; } = DefaultColumns;
    public int Rows { get; set; } = DefaultRows;
    public double CellSize { get; set; } = DefaultCellSize;
    /// <summary>Fine grid drawn inside the cells (squares of <see cref="GridSize"/>).</summary>
    public bool ShowGrid { get; set; }
    public double GridSize { get; set; } = 50;

    /// <summary>Items of the boards saved before the cells existed (moved into the first cell by <see cref="Normalize"/>).</summary>
    [JsonPropertyName("Items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BoardItem>? LegacyItems { get; set; }

    /// <summary>Boxes of the short-lived boxed layout (they become cells, row by row).</summary>
    [JsonPropertyName("Boxes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BoardCell>? LegacyBoxes { get; set; }

    public IEnumerable<BoardItem> AllItems => Cells.SelectMany(c => c.Items);

    public BoardRect CellRect(int index) =>
        new((index % Columns) * CellSize, (index / Columns) * CellSize, CellSize, CellSize);

    public BoardPoint ToLocal(int index, BoardPoint p)
    {
        var c = CellRect(index);
        return new BoardPoint(p.X - c.X, p.Y - c.Y);
    }

    /// <summary>Index of the cell under <paramref name="p"/> (board coordinates), or -1 outside the grid.</summary>
    public int CellAt(BoardPoint p)
    {
        if (p.X < 0 || p.Y < 0) return -1;
        int col = (int)(p.X / CellSize), row = (int)(p.Y / CellSize);
        return col < Columns && row < Rows ? row * Columns + col : -1;
    }

    public int IndexOfCellWith(BoardItem item) => Cells.FindIndex(c => c.Items.Contains(item));

    /// <summary>Topmost item touched by <paramref name="p"/> (board coordinates) and the index of its cell.</summary>
    public (int Cell, BoardItem Item)? HitTest(BoardPoint p, double tolerance)
    {
        int index = CellAt(p);
        if (index < 0) return null;
        return Cells[index].HitTest(ToLocal(index, p), tolerance) is { } item ? (index, item) : null;
    }

    /// <summary>
    /// Moves <paramref name="item"/> into the cell <paramref name="target"/>, keeping its place on the board as far
    /// as possible, then nudging it inside the cell.
    /// </summary>
    public void MoveItemToCell(BoardItem item, int target)
    {
        int source = IndexOfCellWith(item);
        if (source < 0 || target < 0 || target >= Cells.Count || source == target) return;
        var from = CellRect(source);
        var to = CellRect(target);
        Cells[source].Items.Remove(item);
        item.Move(from.X - to.X, from.Y - to.Y);
        var b = item.Bounds;
        double dx = b.Width >= CellSize ? -b.X : Math.Clamp(b.X, 0, CellSize - b.Width) - b.X;
        double dy = b.Height >= CellSize ? -b.Y : Math.Clamp(b.Y, 0, CellSize - b.Height) - b.Y;
        item.Move(dx, dy);
        Cells[target].Items.Add(item);
    }

    /// <summary>Swaps the contents of two cells.</summary>
    public void SwapCells(int a, int b)
    {
        if (a == b || a < 0 || b < 0 || a >= Cells.Count || b >= Cells.Count) return;
        (Cells[a], Cells[b]) = (Cells[b], Cells[a]);
    }

    /// <summary>Number of items in the cells that a grid of <paramref name="rows"/> × <paramref name="columns"/> would drop.</summary>
    public int ItemsOutside(int rows, int columns)
    {
        int count = 0;
        for (int i = 0; i < Cells.Count; i++)
            if (i / Columns >= rows || i % Columns >= columns) count += Cells[i].Items.Count;
        return count;
    }

    /// <summary>Changes the size of the grid; every cell keeps its row and column (cells outside are dropped).</summary>
    public void Resize(int rows, int columns)
    {
        rows = Math.Clamp(rows, 1, MaxCells);
        columns = Math.Clamp(columns, 1, MaxCells);
        var cells = new List<BoardCell>(rows * columns);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                cells.Add(r < Rows && c < Columns && r * Columns + c < Cells.Count ? Cells[r * Columns + c] : new BoardCell());
        (Cells, Rows, Columns) = (cells, rows, columns);
    }

    /// <summary>Area covered by the grid.</summary>
    public BoardRect Extent() => new(0, 0, Columns * CellSize, Rows * CellSize);

    /// <summary>Fixes the grid values and moves the items of an older board into its first cell.</summary>
    public Whiteboard Normalize()
    {
        if (!(CellSize >= 50)) CellSize = DefaultCellSize;
        if (!(GridSize > 0)) GridSize = 50;
        Cells ??= new List<BoardCell>();
        Cells.RemoveAll(c => c is null);
        foreach (var cell in Cells) cell.Items ??= new List<BoardItem>();
        if (LegacyItems is { Count: > 0 } legacy)
        {
            const double margin = 40;
            BoardRect? extent = null;
            foreach (var item in legacy) extent = extent is { } a ? a.Union(item.Bounds) : item.Bounds;
            var e = extent!.Value;
            foreach (var item in legacy) item.Move(margin - e.X, margin - e.Y);
            CellSize = Math.Max(CellSize, Math.Max(e.Width, e.Height) + 2 * margin);
            Cells.Insert(0, new BoardCell { Items = legacy });
        }
        LegacyItems = null;
        if (LegacyBoxes is { Count: > 0 } boxes && Cells.Count == 0)
        {
            Cells = boxes.Where(b => b != null).ToList();
            foreach (var cell in Cells) cell.Items ??= new List<BoardItem>();
            Columns = Math.Clamp(Columns, 1, MaxCells);
            Rows = (Cells.Count + Columns - 1) / Columns;
            CellSize = 800; // width of those boxes
        }
        LegacyBoxes = null;
        int rows = Math.Clamp(Rows, 1, MaxCells), columns = Math.Clamp(Columns, 1, MaxCells);
        while (Cells.Count < rows * columns) Cells.Add(new BoardCell());
        // Extra cells (damaged file) get more rows, so nothing is lost.
        while (Cells.Count > rows * columns && rows < MaxCells) rows++;
        while (Cells.Count < rows * columns) Cells.Add(new BoardCell());
        if (Cells.Count > rows * columns) Cells.RemoveRange(rows * columns, Cells.Count - rows * columns);
        (Rows, Columns) = (rows, columns);
        return this;
    }

    /// <summary>Replaces this board's content with <paramref name="other"/>'s (Undo / Redo).</summary>
    public void CopyFrom(Whiteboard other)
    {
        Cells = other.Cells;
        Columns = other.Columns;
        Rows = other.Rows;
        CellSize = other.CellSize;
        ShowGrid = other.ShowGrid;
        GridSize = other.GridSize;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonDefaults.Compact);

    public static Whiteboard FromJson(string json) =>
        (JsonSerializer.Deserialize<Whiteboard>(json, JsonDefaults.Options) ?? new Whiteboard()).Normalize();
}
