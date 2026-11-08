using ImgProcSharp.Text;
using Xunit;

namespace ImgProcSharp.Tests;

public class TextTests
{
    private static int CountInk(Canvas canvas, int threshold = 8)
    {
        int count = 0;
        for (int y = 0; y < canvas.Height; y++)
        {
            ReadOnlySpan<byte> row = canvas.GetRow(y);
            for (int x = 0; x < canvas.Width; x++)
            {
                if (row[(x * 4) + 3] >= threshold)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static long InkSum(Canvas canvas)
    {
        long sum = 0;
        for (int y = 0; y < canvas.Height; y++)
        {
            ReadOnlySpan<byte> row = canvas.GetRow(y);
            for (int x = 0; x < canvas.Width; x++)
            {
                sum += row[(x * 4) + 3];
            }
        }

        return sum;
    }

    [Fact]
    public void BuiltInFontLoadsAndMeasures()
    {
        TextFont font = TextFont.BuiltIn;

        Assert.NotEmpty(font.FamilyName);
        Assert.True(font.Measure("abc", 16f) > 0f);
        Assert.True(font.Measure("中文", 16f) > 0f);
    }

    [Fact]
    public void DrawTextPaintsLatinInk()
    {
        using Canvas canvas = Canvas.Create(160, 48);
        var context = new DrawingContext(canvas);

        context.DrawText("Hello", new PointF(4, 32), new TextOptions { Size = 24f, Color = Color.Black });

        Assert.True(CountInk(canvas) > 100, "英文没有绘制出墨水");
    }

    [Fact]
    public void DrawTextPaintsChineseInk()
    {
        using Canvas canvas = Canvas.Create(160, 48);
        var context = new DrawingContext(canvas);

        context.DrawText("中文测试", new PointF(4, 32), new TextOptions { Size = 24f });

        Assert.True(CountInk(canvas) > 100, "中文没有绘制出墨水");
    }

    [Fact]
    public void DrawTextUsesTemplateBrushColor()
    {
        using Canvas canvas = Canvas.Create(120, 48);
        var context = new DrawingContext(canvas);

        context.DrawText("A", new PointF(4, 34), new TextOptions { Size = 32f, Color = Color.Red });

        Color found = default;
        bool any = false;
        for (int y = 0; y < canvas.Height && !any; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                Color c = canvas.GetColor(x, y);
                if (c.A > 200)
                {
                    found = c;
                    any = true;
                    break;
                }
            }
        }

        Assert.True(any);
        Assert.Equal(255, found.R);
        Assert.True(found.G < 40 && found.B < 40);
    }

    [Fact]
    public void TranslateAlphaIsAppliedToInk()
    {
        using Canvas canvas = Canvas.Create(120, 48);
        var context = new DrawingContext(canvas);

        context.DrawText("O", new PointF(4, 34), new TextOptions { Size = 32f, Color = new Color(0, 0, 0, 128) });

        int maxAlpha = 0;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                maxAlpha = Math.Max(maxAlpha, canvas.GetColor(x, y).A);
            }
        }

        Assert.InRange(maxAlpha, 120, 136);
    }

    [Fact]
    public void MeasureTextEqualsSumOfAdvances()
    {
        TextFont font = TextFont.BuiltIn;
        const float size = 20f;

        float expected = 0f;
        foreach (char c in "abc")
        {
            expected += font.GetGlyphMask(c, size).Advance;
        }

        SizeF measured = font.MeasureText("abc", size);

        Assert.Equal(expected, measured.Width, 2);
        Assert.True(measured.Height > 0f);
    }

    [Fact]
    public void NewlineAddsAnotherLine()
    {
        TextFont font = TextFont.BuiltIn;

        TextLayout one = font.Layout("a", 16f);
        TextLayout two = font.Layout("a\nb", 16f);

        Assert.Equal(1, one.LineCount);
        Assert.Equal(2, two.LineCount);
        Assert.Equal(one.Height * 2f, two.Height, 3);
    }

    [Fact]
    public void MaxWidthWrapsLongText()
    {
        TextFont font = TextFont.BuiltIn;

        TextLayout layout = font.Layout("ab ab ab ab", 16f, new TextLayoutOptions { MaxWidth = 50f });

        Assert.True(layout.LineCount > 1, "超过 MaxWidth 却没有换行");
        Assert.True(layout.Width <= 50f);
        Assert.All(layout.LineWidths, w => Assert.True(w <= 50f + 0.001f));
    }

    [Fact]
    public void CenterAlignShiftsGlyphsTowardMiddle()
    {
        TextFont font = TextFont.BuiltIn;

        TextLayout left = font.Layout("中文", 20f);
        TextLayout centered = font.Layout("中文", 20f, new TextLayoutOptions { MaxWidth = 200f, Align = TextAlign.Center });

        int expectedShift = (int)MathF.Round((200f - left.Width) * 0.5f);
        Assert.Equal(left.Glyphs[0].X + expectedShift, centered.Glyphs[0].X);
        Assert.All(centered.Glyphs, g => Assert.Equal(0, g.Line));
    }

    [Fact]
    public void RightAlignPutsTextAtRightEdge()
    {
        TextFont font = TextFont.BuiltIn;

        TextLayout left = font.Layout("ab", 20f);
        TextLayout right = font.Layout("ab", 20f, new TextLayoutOptions { MaxWidth = 120f, Align = TextAlign.Right });

        int expectedShift = (int)MathF.Round(120f - left.Width);
        Assert.Equal(left.Glyphs[0].X + expectedShift, right.Glyphs[0].X);
    }

    [Fact]
    public void TextIsClippedAtCanvasEdges()
    {
        using Canvas canvas = Canvas.Create(60, 30);
        var context = new DrawingContext(canvas);

        context.DrawText("Clipping", new PointF(-20f, -12f), new TextOptions { Size = 24f });

        Assert.True(CountInk(canvas) > 0, "越界文本应只画出画布内的部分");

        using Canvas offscreen = Canvas.Create(20, 20);
        new DrawingContext(offscreen).DrawText("off", new PointF(200f, 200f), new TextOptions { Size = 16f });

        Assert.Equal(0, CountInk(offscreen));
    }

    [Fact]
    public void EmptyTextDrawsNothing()
    {
        using Canvas canvas = Canvas.Create(32, 32);

        new DrawingContext(canvas).DrawText(string.Empty, new PointF(0, 16));

        Assert.Equal(0, CountInk(canvas));
    }

    [Fact]
    public void FillTextSlowPathMatchesFastPath()
    {
        using Canvas fast = Canvas.Create(120, 40);
        using Canvas slow = Canvas.Create(120, 40);

        var options = new TextOptions { Size = 24f, Color = Color.Black };

        Path collection = GlyphPathBridge.ToPath(TextFont.BuiltIn.GetTextPaths("Aa", 24f));
        Assert.True(collection.Bounds.Width < 60f, $"轮廓包围盒异常：{collection.Bounds}");

        new DrawingContext(fast).DrawText("Aa", new PointF(4, 30), options);
        new DrawingContext(slow).FillText(new SolidBrush(Color.Black), "Aa", new PointF(4, 30), options);

        long fastInk = InkSum(fast);
        long slowInk = InkSum(slow);

        Assert.True(fastInk > 0 && slowInk > 0);

        // 字形掩码是经过 hinting 的整像素位图，轮廓是没有 hinting 的几何：
        // 竖笔会被 hinting 拉到整像素，墨水量天然有几成差异，这里只守住「同量级」。
        double ratio = slowInk / (double)fastInk;
        Assert.InRange(ratio, 0.6, 1.5);

        (int Left, int Top, int Right, int Bottom) fastBox = InkBounds(fast);
        (int Left, int Top, int Right, int Bottom) slowBox = InkBounds(slow);

        Assert.InRange(Math.Abs(fastBox.Left - slowBox.Left), 0, 3);
        Assert.InRange(Math.Abs(fastBox.Top - slowBox.Top), 0, 3);
        Assert.InRange(Math.Abs(fastBox.Right - slowBox.Right), 0, 3);
    }

    [Fact]
    public void GlyphPathsRoundTripThroughCorePath()
    {
        TextFont font = TextFont.BuiltIn;

        GlyphPath glyph = font.GetGlyphPath('A', 32f);
        Assert.False(glyph.IsEmpty);

        Path path = GlyphPathBridge.ToPath(glyph);

        Assert.True(path.Bounds.Width > 0f);
        Assert.True(path.Bounds.Height > 0f);
        Assert.Equal(glyph.MinX, path.Bounds.Left, 1);
        Assert.Equal(glyph.MaxX, path.Bounds.Right, 1);
    }

    [Fact]
    public void CustomFontSizeScalesAdvance()
    {
        TextFont font = TextFont.BuiltIn;

        float small = font.Measure("W", 10f);
        float large = font.Measure("W", 40f);

        Assert.True(large > small);

        // 前进宽度按整像素取整，4 倍字号的比例会有量化误差
        Assert.InRange(large / small, 3.5f, 4.5f);
        Assert.Equal(4f, font.GetAdvance('W', 40f) / font.GetAdvance('W', 10f), 1);
    }

    private static (int Left, int Top, int Right, int Bottom) InkBounds(Canvas canvas, int threshold = 8)
    {
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = -1;
        int bottom = -1;

        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (canvas.GetColor(x, y).A >= threshold)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return (left, top, right, bottom);
    }
}
