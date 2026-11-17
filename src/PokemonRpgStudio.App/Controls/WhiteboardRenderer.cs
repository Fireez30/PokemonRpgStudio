using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.Controls;

/// <summary>Draws whiteboard items (on screen and in the PNG export).</summary>
public static class WhiteboardRenderer
{
    public static readonly IBrush BoardBrush = new ImmutableSolidColorBrush(Color.Parse("#fbfaf7"));
    private static readonly ImmutableSolidColorBrush GridBrush = new(Color.Parse("#1f000000"));
    public static readonly IBrush GutterBrush = new ImmutableSolidColorBrush(Color.Parse("#e4e1d9"));
    private static readonly ImmutableSolidColorBrush CellLineBrush = new(Color.Parse("#a8a296"));
    private static readonly ImmutableSolidColorBrush AccentBrush = new(Color.Parse("#7C4DDB"));
    private static readonly IBrush DropBrush = new ImmutableSolidColorBrush(Color.Parse("#207C4DDB"));
    private static readonly IBrush MissingBrush = new ImmutableSolidColorBrush(Color.Parse("#30888888"));
    private static readonly Dictionary<string, IBrush> Brushes = new();

    public static IBrush BrushFor(string hex, byte? alpha = null)
    {
        var key = hex + alpha;
        if (Brushes.TryGetValue(key, out var brush)) return brush;
        var color = Color.TryParse(hex, out var c) ? c : Colors.Black;
        if (alpha is { } a) color = new Color(a, color.R, color.G, color.B);
        return Brushes[key] = new ImmutableSolidColorBrush(color);
    }

    private static IPen PenFor(BoardItem item) =>
        new ImmutablePen((IImmutableBrush)BrushFor(item.Color), item.Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    private static FormattedText TextOf(BoardItem item) =>
        new(item.Text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, item.FontSize, BrushFor(item.Color));

    /// <summary>Sets the size of a text item from its text and font size.</summary>
    public static void FitText(BoardItem item)
    {
        var text = TextOf(item);
        item.Width = Math.Max(4, text.WidthIncludingTrailingWhitespace);
        item.Height = Math.Max(4, text.Height);
    }

    public static Rect ToRect(BoardRect r) => new(r.X, r.Y, r.Width, r.Height);

    /// <summary>Extra things drawn on screen only (current cell, item being drawn or moved).</summary>
    public sealed record Overlay(int ActiveCell = -1, BoardItem? Draft = null, int DraftCell = -1, BoardItem? Floating = null, int DropTarget = -1);

    /// <summary>Draws every cell of <paramref name="board"/> that touches <paramref name="area"/> (board units), then the grid lines.</summary>
    public static void Draw(DrawingContext ctx, Whiteboard board, WhiteboardStore store, Rect area, double zoom, Overlay? overlay = null)
    {
        overlay ??= new Overlay();
        ctx.FillRectangle(GutterBrush, area);
        var extent = ToRect(board.Extent());
        ctx.FillRectangle(BoardBrush, extent);
        for (int i = 0; i < board.Cells.Count; i++)
        {
            var rect = ToRect(board.CellRect(i));
            if (!rect.Intersects(area)) continue;
            using (ctx.PushClip(rect))
            using (ctx.PushTransform(Matrix.CreateTranslation(rect.X, rect.Y)))
            {
                var local = area.Translate(new Vector(-rect.X, -rect.Y)).Intersect(new Rect(rect.Size));
                DrawGrid(ctx, board, local, zoom);
                foreach (var item in board.Cells[i].Items)
                    if (item != overlay.Floating) DrawItem(ctx, item, store);
                if (overlay.Draft != null && overlay.DraftCell == i) DrawItem(ctx, overlay.Draft, store);
            }
        }
        // Cell borders.
        var linePen = new ImmutablePen(CellLineBrush, 1.5 / zoom);
        for (int c = 0; c <= board.Columns; c++)
            ctx.DrawLine(linePen, new Point(c * board.CellSize, 0), new Point(c * board.CellSize, extent.Bottom));
        for (int r = 0; r <= board.Rows; r++)
            ctx.DrawLine(linePen, new Point(0, r * board.CellSize), new Point(extent.Right, r * board.CellSize));
        if (overlay.ActiveCell >= 0 && overlay.ActiveCell < board.Cells.Count)
            ctx.DrawRectangle(null, new ImmutablePen(AccentBrush, 3 / zoom), ToRect(board.CellRect(overlay.ActiveCell)));
        if (overlay.DropTarget >= 0 && overlay.DropTarget < board.Cells.Count)
        {
            var target = ToRect(board.CellRect(overlay.DropTarget));
            ctx.FillRectangle(DropBrush, target);
            ctx.DrawRectangle(null, new ImmutablePen(AccentBrush, 4 / zoom, new ImmutableDashStyle(new double[] { 3, 2 }, 0)), target);
        }
        // The item being moved is drawn above every cell, so it can be carried from one to another.
        if (overlay.Floating is { } floating && board.IndexOfCellWith(floating) is var fi and >= 0)
        {
            var c = board.CellRect(fi);
            using (ctx.PushOpacity(0.8))
            using (ctx.PushTransform(Matrix.CreateTranslation(c.X, c.Y)))
                DrawItem(ctx, floating, store);
        }
    }

    private static void DrawGrid(DrawingContext ctx, Whiteboard board, Rect area, double zoom)
    {
        if (!board.ShowGrid || board.GridSize * zoom < 6 || area.Width <= 0 || area.Height <= 0) return;
        var pen = new ImmutablePen(GridBrush, 1 / zoom);
        double g = board.GridSize;
        for (double x = Math.Ceiling(area.X / g) * g; x <= area.Right; x += g)
            ctx.DrawLine(pen, new Point(x, area.Y), new Point(x, area.Bottom));
        for (double y = Math.Ceiling(area.Y / g) * g; y <= area.Bottom; y += g)
            ctx.DrawLine(pen, new Point(area.X, y), new Point(area.Right, y));
    }

    public static void DrawItem(DrawingContext ctx, BoardItem item, WhiteboardStore store)
    {
        switch (item.Kind)
        {
            case BoardItemKind.Stroke:
                DrawStroke(ctx, item);
                break;
            case BoardItemKind.Line:
            case BoardItemKind.Arrow:
                if (item.Points.Count < 2) return;
                var pen = PenFor(item);
                Point a = new(item.Points[0].X, item.Points[0].Y), b = new(item.Points[1].X, item.Points[1].Y);
                ctx.DrawLine(pen, a, b);
                if (item.Kind == BoardItemKind.Arrow) DrawArrowHead(ctx, item, a, b);
                break;
            case BoardItemKind.Rectangle:
                ctx.DrawRectangle(item.Filled ? BrushFor(item.Color, 0x60) : null, PenFor(item), ToRect(item.Bounds));
                break;
            case BoardItemKind.Ellipse:
                var r = ToRect(item.Bounds);
                ctx.DrawEllipse(item.Filled ? BrushFor(item.Color, 0x60) : null, PenFor(item), r.Center, r.Width / 2, r.Height / 2);
                break;
            case BoardItemKind.Text:
                ctx.DrawText(TextOf(item), new Point(item.X, item.Y));
                break;
            case BoardItemKind.Image:
                var rect = ToRect(item.Bounds);
                Bitmap? bitmap = item.ImageFile != null
                    ? BitmapCache.Load(store.ImagePath(item.ImageFile), 0)
                    : BitmapCache.ForPokemon(item.Pokemon);
                if (bitmap is null) ctx.FillRectangle(MissingBrush, rect);
                else ctx.DrawImage(bitmap, rect);
                break;
        }
    }

    private static void DrawStroke(DrawingContext ctx, BoardItem item)
    {
        if (item.Points.Count == 0) return;
        if (item.Points.Count == 1)
        {
            var p = item.Points[0];
            ctx.DrawEllipse(BrushFor(item.Color), null, new Point(p.X, p.Y), item.Thickness / 2, item.Thickness / 2);
            return;
        }
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(item.Points[0].X, item.Points[0].Y), false);
            for (int i = 1; i < item.Points.Count; i++) g.LineTo(new Point(item.Points[i].X, item.Points[i].Y));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, PenFor(item), geometry);
    }

    private static void DrawArrowHead(DrawingContext ctx, BoardItem item, Point from, Point to)
    {
        var d = to - from;
        double length = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        if (length < 0.001) return;
        var u = d / length;
        var n = new Vector(-u.Y, u.X);
        double size = Math.Max(12, item.Thickness * 4);
        var back = to - u * size;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(to, true);
            g.LineTo(back + n * size * 0.5);
            g.LineTo(back - n * size * 0.5);
            g.EndFigure(true);
        }
        ctx.DrawGeometry(BrushFor(item.Color), PenFor(item), geometry);
    }

    /// <summary>Renders the whole grid, with a margin.</summary>
    public static RenderTargetBitmap RenderPng(Whiteboard board, WhiteboardStore store)
    {
        const double margin = 24;
        var extent = board.Extent();
        var area = ToRect(extent).Inflate(margin);
        // Very large boards are scaled down to stay below 8000 pixels.
        double scale = Math.Min(1, 8000 / Math.Max(area.Width, area.Height));
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(area.Width * scale)), Math.Max(1, (int)Math.Ceiling(area.Height * scale)));
        var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateTranslation(-area.X, -area.Y) * Matrix.CreateScale(scale, scale)))
            Draw(ctx, board, store, area, scale);
        return bitmap;
    }
}
