using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.Tests;

public class WhiteboardTests
{
    private static BoardItem Stroke(params (double X, double Y)[] points) => new()
    {
        Kind = BoardItemKind.Stroke, Thickness = 4, Points = points.Select(p => new BoardPoint(p.X, p.Y)).ToList(),
    };

    [Fact]
    public void Stroke_hit_test_follows_the_line()
    {
        var s = Stroke((0, 0), (100, 0));
        Assert.True(s.HitTest(new BoardPoint(50, 3), 2));
        Assert.False(s.HitTest(new BoardPoint(50, 10), 2));
        Assert.Equal(new BoardRect(0, 0, 100, 0), s.Bounds);
    }

    [Fact]
    public void Topmost_item_is_hit_first()
    {
        var cell = new BoardCell();
        var below = new BoardItem { Kind = BoardItemKind.Rectangle, X = 0, Y = 0, Width = 100, Height = 100 };
        var above = new BoardItem { Kind = BoardItemKind.Image, X = 50, Y = 50, Width = 100, Height = 100 };
        cell.Items.AddRange(new[] { below, above });
        Assert.Same(above, cell.HitTest(new BoardPoint(60, 60), 0));
        Assert.Same(below, cell.HitTest(new BoardPoint(10, 10), 0));
        Assert.Null(cell.HitTest(new BoardPoint(500, 500), 0));
    }

    [Fact]
    public void Cells_form_a_grid_without_gaps()
    {
        var board = new Whiteboard { Columns = 3, Rows = 2, CellSize = 100 }.Normalize();
        Assert.Equal(6, board.Cells.Count);
        Assert.Equal(new BoardRect(100, 100, 100, 100), board.CellRect(4));
        Assert.Equal(new BoardRect(0, 0, 300, 200), board.Extent());
        Assert.Equal(0, board.CellAt(new BoardPoint(0, 0)));
        Assert.Equal(5, board.CellAt(new BoardPoint(250, 150)));
        Assert.Equal(-1, board.CellAt(new BoardPoint(-1, 50)));
        Assert.Equal(-1, board.CellAt(new BoardPoint(300, 50)));

        var token = new BoardItem { Kind = BoardItemKind.Image, X = 10, Y = 10, Width = 20, Height = 20 };
        board.Cells[4].Items.Add(token);
        Assert.Equal((4, token), board.HitTest(new BoardPoint(115, 115), 0));
        Assert.Null(board.HitTest(new BoardPoint(15, 15), 0)); // same place, cell 0
    }

    [Fact]
    public void Items_move_between_cells_and_cells_swap()
    {
        var board = new Whiteboard { Columns = 2, Rows = 1, CellSize = 100 }.Normalize();
        var token = new BoardItem { Kind = BoardItemKind.Image, X = 70, Y = 40, Width = 20, Height = 20 };
        board.Cells[0].Items.Add(token);
        // Dragged right, half over the border: lands in cell 1, nudged inside it.
        token.Move(20, 0);
        board.MoveItemToCell(token, 1);
        Assert.Empty(board.Cells[0].Items);
        Assert.Same(token, Assert.Single(board.Cells[1].Items));
        Assert.Equal(new BoardRect(0, 40, 20, 20), token.Bounds);

        var note = new BoardItem { Kind = BoardItemKind.Text, Text = "A" };
        board.Cells[0].Items.Add(note);
        board.SwapCells(0, 1);
        Assert.Same(token, board.Cells[0].Items[0]);
        Assert.Same(note, board.Cells[1].Items[0]);
    }

    [Fact]
    public void Resizing_the_grid_keeps_cells_in_place()
    {
        var board = new Whiteboard { Columns = 2, Rows = 2 }.Normalize();
        var bottomRight = new BoardItem { Kind = BoardItemKind.Text, Text = "B" };
        board.Cells[3].Items.Add(bottomRight); // row 1, column 1
        Assert.Equal(0, board.ItemsOutside(3, 3));
        board.Resize(3, 3);
        Assert.Equal(9, board.Cells.Count);
        Assert.Same(bottomRight, board.Cells[4].Items[0]); // still row 1, column 1
        Assert.Equal(1, board.ItemsOutside(3, 1));
        board.Resize(1, 3);
        Assert.Equal(3, board.Cells.Count);
        Assert.Empty(board.AllItems);
    }

    [Fact]
    public void Old_boards_move_into_the_first_cell()
    {
        var json = """{"Items":[{"Kind":"Rectangle","X":-100,"Y":-50,"Width":1000,"Height":200}],"ShowGrid":true}""";
        var board = Whiteboard.FromJson(json);
        Assert.Null(board.LegacyItems);
        Assert.True(board.ShowGrid);
        Assert.Equal(Whiteboard.DefaultRows * Whiteboard.DefaultColumns, board.Cells.Count);
        var item = Assert.Single(board.Cells[0].Items);
        Assert.Equal(new BoardRect(40, 40, 1000, 200), item.Bounds);
        Assert.Equal(1080, board.CellSize);
        var root = System.Text.Json.JsonDocument.Parse(board.ToJson()).RootElement;
        Assert.False(root.TryGetProperty("Items", out _));
        Assert.True(root.TryGetProperty("Cells", out _));

        var boxed = Whiteboard.FromJson("""{"Boxes":[{"Title":"Map","Items":[{"Kind":"Text","Text":"A"}]},{},{}],"Columns":2}""");
        Assert.Null(boxed.LegacyBoxes);
        Assert.Equal((2, 2, 4), (boxed.Columns, boxed.Rows, boxed.Cells.Count));
        Assert.Equal("A", boxed.Cells[0].Items[0].Text);
    }

    [Fact]
    public void Move_and_resize()
    {
        var s = Stroke((0, 0), (10, 20));
        s.Move(5, 5);
        Assert.Equal(new BoardRect(5, 5, 10, 20), s.Bounds);
        s.Resize(s.Bounds, new BoardRect(5, 5, 20, 40));
        Assert.Equal(new BoardRect(5, 5, 20, 40), s.Bounds);

        var text = new BoardItem { Kind = BoardItemKind.Text, Text = "Hi", FontSize = 20, Width = 30, Height = 25 };
        text.Resize(text.Bounds, new BoardRect(0, 0, 60, 30));
        Assert.Equal(40, text.FontSize);
        Assert.Equal(60, text.Width);
        Assert.Equal(50, text.Height);
    }

    [Fact]
    public void Board_round_trips_through_the_store()
    {
        var dir = Path.Combine(Path.GetTempPath(), "prs-board-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WhiteboardStore(dir);
            Assert.Empty(store.Load().AllItems);

            var image = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(image, new byte[] { 1, 2, 3 });
            var copied = store.ImportImage(image);
            File.Delete(image);
            File.WriteAllBytes(Path.Combine(store.ImagesDirectory, "unused.png"), new byte[] { 1 });

            var board = new Whiteboard { ShowGrid = true, Columns = 4, Rows = 3 }.Normalize();
            board.Cells[0].Items.Add(Stroke((1, 2), (3, 4)));
            board.Cells[0].Items.Add(new BoardItem { Kind = BoardItemKind.Image, ImageFile = copied, Width = 10, Height = 10 });
            board.Cells[11].Items.Add(new BoardItem { Kind = BoardItemKind.Image, Pokemon = "Pikachu", Width = 10, Height = 10 });
            board.Cells[11].Items.Add(new BoardItem { Kind = BoardItemKind.Ellipse, Filled = true, Color = "#e03131" });
            store.Save(board);

            var loaded = store.Load();
            Assert.True(loaded.ShowGrid);
            Assert.Equal((4, 3, 12), (loaded.Columns, loaded.Rows, loaded.Cells.Count));
            Assert.Equal(new[] { BoardItemKind.Stroke, BoardItemKind.Image, BoardItemKind.Image, BoardItemKind.Ellipse }, loaded.AllItems.Select(i => i.Kind));
            Assert.Equal(new BoardPoint(3, 4), loaded.Cells[0].Items[0].Points[1]);
            Assert.Equal("Pikachu", loaded.Cells[11].Items[0].Pokemon);
            Assert.True(loaded.Cells[11].Items[1].Filled);
            Assert.Contains("\"Ellipse\"", File.ReadAllText(store.BoardFile));

            store.DeleteUnusedImages(loaded);
            Assert.True(File.Exists(store.ImagePath(copied)));
            Assert.False(File.Exists(Path.Combine(store.ImagesDirectory, "unused.png")));
            Assert.Null(store.ImagePath("../board.json"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
