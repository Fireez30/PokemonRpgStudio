using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PokemonRpgStudio.Core.Models;

namespace PokemonRpgStudio.App.Services;

public sealed record TeamCardSlot(string PokemonName, string Label, IReadOnlyList<string> Types, string? ImagePath, string Level, string Rarity, bool IsMega);

public sealed record TeamCardInput(string TeamName, string ThemeType, string? TrainerImagePath, IReadOnlyList<TeamCardSlot?> Slots);

/// <summary>
/// Draws the 854×480 team card (exported at 2× = 1708×960), same layout as the website's
/// Pokemon Team Card / Team Manager canvas.
/// </summary>
public static class TeamCardRenderer
{
    public const int Width = 854;
    public const int Height = 480;
    public const int Scale = 2;
    public const string DefaultTeamName = "My Team";

    public static readonly string[] ThemeTypes =
    {
        "Bug", "Dark", "Dragon", "Electric", "Fairy", "Fighting", "Fire", "Flying", "Ghost", "Grass", "Ground",
        "Ice", "Normal", "Poison", "Psychic", "Rock", "Steel", "Water", "Light", "Data", "Sound",
    };

    private static readonly Dictionary<string, string[]> Themes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bug"] = new[] { "#6f9f2e", "#9fd65a", "#dff5a8" }, ["Dark"] = new[] { "#171312", "#332927", "#6a5a54" },
        ["Dragon"] = new[] { "#4f2f9f", "#7b57dc", "#c1aeff" }, ["Electric"] = new[] { "#c69312", "#f4d232", "#fff1a8" },
        ["Fairy"] = new[] { "#d58fb5", "#efbdd3", "#ffeaf3" }, ["Fighting"] = new[] { "#8f4e3d", "#c9775f", "#efb29e" },
        ["Fire"] = new[] { "#c85e1f", "#ef8c36", "#ffd1a3" }, ["Flying"] = new[] { "#9b8dd6", "#c3b7ef", "#ece8ff" },
        ["Ghost"] = new[] { "#473464", "#6c52a0", "#b9a8e0" }, ["Grass"] = new[] { "#2f8a3a", "#59b85f", "#bfe9a8" },
        ["Ground"] = new[] { "#b48a52", "#d7b17a", "#f2dfba" }, ["Ice"] = new[] { "#7ccfe3", "#a8e6f4", "#e2fbff" },
        ["Normal"] = new[] { "#7d7d7d", "#b0b0b0", "#e4e4e4" }, ["Poison"] = new[] { "#5c2468", "#8a3e9f", "#cf9de2" },
        ["Psychic"] = new[] { "#d75c9a", "#ef95bf", "#ffd7e9" }, ["Rock"] = new[] { "#9a764d", "#c59a6d", "#ead0aa" },
        ["Steel"] = new[] { "#7f8b94", "#b4c0c8", "#e5edf2" }, ["Water"] = new[] { "#2d7fcb", "#63a9e9", "#c0e2ff" },
        ["Light"] = new[] { "#e4d57a", "#f4e79f", "#fff7d1" }, ["Data"] = new[] { "#111111", "#2c2c2c", "#727272" },
        ["Sound"] = new[] { "#424242", "#6a6a6a", "#b5b5b5" },
    };

    private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface SemiBold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface ItalicSemiBold = new(FontFamily.Default, FontStyle.Italic, FontWeight.SemiBold);

    public static string[] ThemeFor(string type) => Themes.GetValueOrDefault(type) ?? new[] { "#6d2a22", "#b84b3b", "#f0d0a0" };

    public static RenderTargetBitmap Render(TeamCardInput input)
    {
        // 96 DPI target with an explicit 2× transform: a DPI-scaled target misbehaves with
        // clips/brushes and shows up zoomed in the Image control.
        var bitmap = new RenderTargetBitmap(new PixelSize(Width * Scale, Height * Scale), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateScale(Scale, Scale)))
            Draw(ctx, input);
        return bitmap;
    }

    private static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static IBrush B(byte a, byte r, byte g, byte b) => new SolidColorBrush(Color.FromArgb(a, r, g, b));
    private static byte A(double alpha) => (byte)Math.Round(alpha * 255);

    private static void Draw(DrawingContext ctx, TeamCardInput input)
    {
        var theme = ThemeFor(input.ThemeType);
        var name = string.IsNullOrWhiteSpace(input.TeamName) ? DefaultTeamName : input.TeamName.Trim();

        ctx.FillRectangle(B("#f4ede1"), new Rect(0, 0, Width, Height));
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(theme[0]), 0),
                new GradientStop(Color.Parse(theme[1]), 0.55),
                new GradientStop(Color.Parse(theme[2]), 1),
            },
        };
        ctx.FillRectangle(gradient, new Rect(0, 0, Width, Height));
        ctx.DrawEllipse(B(A(0.18), 255, 248, 238), null, new Point(745, 90), 130, 130);
        ctx.DrawEllipse(B(A(0.12), 255, 248, 238), null, new Point(90, 420), 110, 110);
        ctx.FillRectangle(B(A(0.18), 26, 17, 13), new Rect(32, 32, Width - 64, Height - 64));
        ctx.FillRectangle(B(A(0.92), 255, 251, 246), new Rect(36, 36, Width - 72, Height - 72));

        const double insetX = 36, insetY = 36;
        const double innerW = Width - 72, innerH = Height - 72;
        const double leftW = 344, gap = 24;
        const double rightX = insetX + leftW + gap;
        const double rightW = innerW - leftW - gap;

        DrawText(ctx, name, Bold, 34, B("#6a3b34"), new Point(rightX + rightW / 2, 78), rightW - 16, 1, center: true, middle: true);

        const double heroW = 296, heroH = 364;
        double heroX = insetX + (leftW - heroW) / 2;
        double heroY = insetY + (innerH - heroH) / 2;
        var heroRect = new Rect(heroX, heroY, heroW, heroH);
        ctx.FillRectangle(B("#efe2d0"), heroRect);
        var trainer = LoadFull(input.TrainerImagePath);
        if (trainer != null) DrawImage(ctx, trainer, heroRect, 22, cover: false, "#fbf8f3");
        else DrawText(ctx, "Trainer image", SemiBold, 15, B(A(0.45), 106, 59, 52), heroRect.Center, heroW - 20, 1, center: true, middle: true);
        ctx.DrawRectangle(null, new Pen(B(A(0.22), 106, 59, 52), 2), heroRect);

        const double cellW = 198, cellH = 104, gapX = 18, gapY = 12;
        double gridX = rightX + (rightW - (cellW * 2 + gapX)) / 2;
        const double gridY = 98;

        for (int i = 0; i < TeamRecord.Size; i++)
        {
            var slot = i < input.Slots.Count ? input.Slots[i] : null;
            double x = gridX + (i % 2) * (cellW + gapX);
            double y = gridY + (i / 2) * (cellH + gapY);
            var cell = new Rect(x, y, cellW, cellH);
            ctx.FillRectangle(B("#fff9f2"), cell);
            ctx.DrawRectangle(null, new Pen(B(A(0.2), 106, 59, 52), 2), cell);

            DrawText(ctx, $"#{i + 1}", Bold, 14, B("#8f3027"), new Point(x + cellW - 8, y + 16), 40, 1, right: true);

            var art = new Rect(x + 10, y + 10, 102, cellH - 20);
            double textX = x + 120, textW = cellW - 132;
            var hasPokemon = slot != null && !string.IsNullOrWhiteSpace(slot.PokemonName);
            var sprite = hasPokemon ? LoadFull(slot!.ImagePath) : null;
            if (sprite != null) DrawImage(ctx, sprite, art, 16, cover: true, "#fff9f2");
            else
            {
                ctx.FillRectangle(B("#ead7c1"), art);
                DrawText(ctx, hasPokemon ? "No image" : "Empty", SemiBold, 13, B(A(0.5), 106, 59, 52), new Point(art.X + 16, y + 60), art.Width, 1);
            }

            var display = hasPokemon ? (slot!.IsMega ? "(Mega) " : "") + slot.Label : "Open slot";
            double nameHeight = DrawText(ctx, display, Bold, 14, B("#4f2a24"), new Point(textX, y + 28), textW, 2);
            if (hasPokemon && slot!.Types.Count > 0)
                DrawText(ctx, string.Join(" / ", slot.Types.Select(t => t.Trim())), ItalicSemiBold, 11, B(A(0.82), 79, 42, 36),
                    new Point(textX, y + 28 + nameHeight + 4), textW, 2);
            if (hasPokemon && !string.IsNullOrWhiteSpace(slot!.Level))
                DrawText(ctx, $"Level {slot.Level}", SemiBold, 13, B(A(0.8), 79, 42, 36), new Point(textX, y + 88), textW, 1);
            if (!hasPokemon)
                DrawText(ctx, "No Pokemon selected", SemiBold, 13, B(A(0.72), 79, 42, 36), new Point(textX, y + 72), textW, 1);

            var rarity = (slot?.Rarity ?? "").ToLowerInvariant();
            if (hasPokemon && rarity == "shiny") DrawStar(ctx, new Point(x + 14, y + 14), 10, 5, "#f2c94c", 10);
            if (hasPokemon && rarity == "platine") DrawStar(ctx, new Point(x + 14, y + 14), 10, 3.8, "#8fdcff", 8, sparkle: true);
        }
    }

    /// <summary>Draws text with the top-left (or anchor) at <paramref name="origin"/>; returns the drawn height.
    /// The website positions text by baseline, so the origin is shifted up by the font ascent.</summary>
    private static double DrawText(DrawingContext ctx, string text, Typeface face, double size, IBrush brush, Point origin,
        double maxWidth, int maxLines, bool center = false, bool right = false, bool middle = false)
    {
        size = FitFontSize(text, face, size, maxWidth, maxLines);
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = maxLines,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = center ? TextAlignment.Center : right ? TextAlignment.Right : TextAlignment.Left,
        };
        double x = center ? origin.X - maxWidth / 2 : right ? origin.X - maxWidth : origin.X;
        double y = middle ? origin.Y - ft.Height / 2 : origin.Y - ft.Baseline;
        ctx.DrawText(ft, new Point(x, y));
        return ft.Height;
    }

    /// <summary>
    /// Shrinks the font (down to 70 %) so a single-line text fits, or so no word of a
    /// multi-line text has to be broken in the middle.
    /// </summary>
    private static double FitFontSize(string text, Typeface face, double size, double maxWidth, int maxLines)
    {
        double Width(string s, double fontSize) =>
            new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, fontSize, Brushes.Black).WidthIncludingTrailingWhitespace;

        var parts = maxLines <= 1 ? new[] { text } : text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return size;
        double min = size * 0.7;
        double current = size;
        while (current > min && parts.Any(p => Width(p, current) > maxWidth)) current -= 0.5;
        return current;
    }

    private static Bitmap? LoadFull(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try { return BitmapCache.Load(path, 0); }
        catch { return null; }
    }

    /// <summary>
    /// Draws an image into a rectangle, cropped ("cover") or letter-boxed ("contain"), then masks
    /// the corners with <paramref name="cornerColor"/> to round them. No clip or brush is used
    /// (both proved unreliable when rendering into a bitmap).
    /// </summary>
    private static void DrawImage(DrawingContext ctx, Bitmap image, Rect target, double radius, bool cover, string cornerColor)
    {
        var src = new Rect(image.Size);
        Rect dest = target;
        double srcRatio = src.Width / src.Height;
        double dstRatio = target.Width / target.Height;
        if (cover)
        {
            if (srcRatio > dstRatio)
            {
                double w = src.Height * dstRatio;
                src = new Rect((src.Width - w) / 2, 0, w, src.Height);
            }
            else
            {
                double h = src.Width / dstRatio;
                src = new Rect(0, (src.Height - h) / 2, src.Width, h);
            }
        }
        else
        {
            double scale = Math.Min(target.Width / src.Width, target.Height / src.Height);
            double w = src.Width * scale, h = src.Height * scale;
            dest = new Rect(target.X + (target.Width - w) / 2, target.Y + (target.Height - h) / 2, w, h);
        }
        ctx.DrawImage(image, src, dest);

        if (radius > 0)
        {
            var mask = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(target), new RectangleGeometry(target, radius, radius));
            ctx.DrawGeometry(B(cornerColor), null, mask);
        }
    }

    private static void DrawStar(DrawingContext ctx, Point c, double outer, double inner, string color, int points, bool sparkle = false)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i < points; i++)
            {
                double angle = Math.PI * 2 / points * i - Math.PI / 2;
                double r = i % 2 == 0 ? outer : inner;
                var p = new Point(c.X + Math.Cos(angle) * r, c.Y + Math.Sin(angle) * r);
                if (i == 0) g.BeginFigure(p, true);
                else g.LineTo(p);
            }
            g.EndFigure(true);
        }
        ctx.DrawGeometry(B(color), new Pen(B(A(0.9), 79, 42, 36), 2), geo);
        if (sparkle)
            ctx.DrawEllipse(B(A(0.8), 255, 255, 255), null, new Point(c.X - outer * 0.18, c.Y - outer * 0.18), outer * 0.18, outer * 0.18);
    }
}
