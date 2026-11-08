using Xunit;

namespace ImgProcSharp.Tests;

public class GradientAndArcTests
{
    private static double AlphaArea(Canvas canvas)
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

        return sum / 255.0;
    }

    private static bool HasInk(Canvas canvas, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                if (canvas.GetColor(x, y).A > 8)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Fact]
    public void ArcToHalfDiscCoversHalfCircle()
    {
        using Canvas canvas = Canvas.Create(120, 100);
        var path = new PathBuilder()
            .MoveTo(10, 50)
            .ArcTo(110, 50, 50)
            .CloseFigure()
            .Build();

        new DrawingContext(canvas).Fill(Color.Black, path);

        double expected = Math.PI * 50 * 50 / 2;
        double actual = AlphaArea(canvas);
        Assert.InRange(actual, expected * 0.98, expected * 1.02);
    }

    [Fact]
    public void ArcToSweepDirectionPicksUpperOrLowerHalf()
    {
        using Canvas upper = Canvas.Create(120, 100);
        using Canvas lower = Canvas.Create(120, 100);

        var context = new DrawingContext(upper);
        context.Fill(Color.Black, new PathBuilder().MoveTo(10, 50).ArcTo(110, 50, 50, false, true).CloseFigure().Build());
        new DrawingContext(lower).Fill(
            Color.Black,
            new PathBuilder().MoveTo(10, 50).ArcTo(110, 50, 50, false, false).CloseFigure().Build());

        Assert.True(HasInk(upper, 0, 0, 120, 50), "顺时针弧应在上半部分");
        Assert.False(HasInk(upper, 0, 52, 120, 100), "顺时针弧不应越过基线");

        Assert.True(HasInk(lower, 0, 52, 120, 100), "逆时针弧应在下半部分");
        Assert.False(HasInk(lower, 0, 0, 120, 48), "逆时针弧不应越过基线");
    }

    [Fact]
    public void ArcToZeroRadiusDegeneratesToLine()
    {
        using Canvas canvas = Canvas.Create(60, 60);
        var triangle = new PathBuilder()
            .MoveTo(10, 10)
            .ArcTo(50, 10, 0f)
            .LineTo(10, 50)
            .CloseFigure()
            .Build();

        new DrawingContext(canvas).Fill(Color.Black, triangle);

        Assert.InRange(AlphaArea(canvas), 40 * 40 / 2 * 0.97, 40 * 40 / 2 * 1.03);
    }

    [Fact]
    public void ArcToLargeArcFlagProducesLongerSweep()
    {
        // 两端点相距 60、半径 50：小弧 ≈ 73.7°，大弧 = 360° - 73.7°
        var small = new PathBuilder().MoveTo(10, 50).ArcTo(70, 50, 50, false, true).CloseFigure().Build();
        var large = new PathBuilder().MoveTo(10, 50).ArcTo(70, 50, 50, true, true).CloseFigure().Build();

        Assert.True(large.Bounds.Width > small.Bounds.Width);
        Assert.True(large.Bounds.Height > small.Bounds.Height);
    }

    [Fact]
    public void RoundRectangleAreaMatchesFormula()
    {
        const float w = 80f;
        const float h = 40f;
        const float r = 15f;

        using Canvas canvas = Canvas.Create(100, 60);
        new DrawingContext(canvas).Fill(Color.Black, new RoundRectanglePolygon(10, 10, w, h, r));

        double expected = (w * h) - (4 * r * r * (1 - (Math.PI / 4)));
        Assert.InRange(AlphaArea(canvas), expected * 0.98, expected * 1.02);
    }

    [Fact]
    public void RoundRectangleTrimsCornersButKeepsEdges()
    {
        using Canvas canvas = Canvas.Create(100, 60);
        new DrawingContext(canvas).Fill(Color.Black, new RoundRectanglePolygon(10, 10, 80, 40, 15));

        Assert.Equal(0, canvas.GetColor(11, 11).A);      // 左上角被切掉
        Assert.Equal(0, canvas.GetColor(89, 49).A);      // 右下角被切掉
        Assert.Equal(255, canvas.GetColor(50, 30).A);    // 中心实心
        Assert.Equal(255, canvas.GetColor(50, 11).A);    // 上边中点仍在
        Assert.Equal(255, canvas.GetColor(11, 30).A);    // 左边中点仍在
    }

    [Fact]
    public void RoundRectangleClampsRadiusToHalfOfShortSide()
    {
        // 半径超大时应退化成胶囊（面积 = 矩形 - 两端被切掉的角），而不是自交成怪形状
        using Canvas canvas = Canvas.Create(120, 60);
        new DrawingContext(canvas).Fill(Color.Black, new RoundRectanglePolygon(10, 10, 100, 40, 100f));

        double expected = (100 * 40) - (4 * 20 * 20 * (1 - (Math.PI / 4)));
        Assert.InRange(AlphaArea(canvas), expected * 0.97, expected * 1.03);
    }

    [Fact]
    public void LinearGradientInterpolatesAcrossAxis()
    {
        using Canvas canvas = Canvas.Create(100, 10);
        var brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(100, 0),
            new ColorStop(0f, Color.Red),
            new ColorStop(1f, Color.Blue));

        new DrawingContext(canvas).Fill(brush, new RectangularPolygon(0, 0, 100, 10));

        Color left = canvas.GetColor(0, 5);
        Color middle = canvas.GetColor(50, 5);
        Color right = canvas.GetColor(99, 5);

        Assert.InRange(left.R, 240, 255);
        Assert.InRange(left.B, 0, 15);

        Assert.InRange(middle.R, 116, 140);
        Assert.InRange(middle.B, 116, 140);

        Assert.InRange(right.R, 0, 15);
        Assert.InRange(right.B, 240, 255);
    }

    [Fact]
    public void LinearGradientIsUniformAlongPerpendicular()
    {
        using Canvas canvas = Canvas.Create(80, 30);
        var brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(80, 0),
            new ColorStop(0f, Color.Black),
            new ColorStop(1f, Color.White));

        new DrawingContext(canvas).Fill(brush, new RectangularPolygon(0, 0, 80, 30));

        Color first = canvas.GetColor(37, 0);
        for (int y = 1; y < canvas.Height; y++)
        {
            Assert.Equal(first, canvas.GetColor(37, y));
        }
    }

    [Fact]
    public void LinearGradientSortsUnsortedStops()
    {
        using Canvas canvas = Canvas.Create(60, 8);
        var brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(60, 0),
            new ColorStop(1f, Color.Blue),
            new ColorStop(0f, Color.Red));

        new DrawingContext(canvas).Fill(brush, new RectangularPolygon(0, 0, 60, 8));

        Assert.InRange(canvas.GetColor(0, 4).R, 240, 255);
        Assert.InRange(canvas.GetColor(59, 4).B, 240, 255);
        Assert.Equal(2, brush.Stops.Count);
        Assert.Equal(0f, brush.Stops[0].Offset);
    }

    [Fact]
    public void TransparentToOpaqueGradientInterpolatesPremultiplied()
    {
        using Canvas canvas = Canvas.Create(100, 8);
        var brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(100, 0),
            new ColorStop(0f, Color.Transparent),
            new ColorStop(1f, Color.White));

        new DrawingContext(canvas).Fill(brush, new RectangularPolygon(0, 0, 100, 8));

        Color middle = canvas.GetColor(50, 4);

        // 预乘插值：中段仍是「白色半透明」；直通插值会得到「灰色半透明」，也就是最常见的那道灰边
        Assert.InRange(middle.A, 120, 136);
        Assert.InRange(middle.R, 245, 255);
        Assert.InRange(middle.G, 245, 255);
        Assert.InRange(middle.B, 245, 255);
    }

    [Fact]
    public void RadialGradientGoesFromCenterToEdge()
    {
        using Canvas canvas = Canvas.Create(80, 80);
        var brush = new RadialGradientBrush(
            new PointF(40, 40),
            30f,
            new ColorStop(0f, Color.White),
            new ColorStop(1f, Color.Black));

        new DrawingContext(canvas).Fill(brush, new RectangularPolygon(0, 0, 80, 80));

        Assert.InRange(canvas.GetColor(40, 40).R, 245, 255);   // 圆心
        Assert.InRange(canvas.GetColor(40, 69).R, 0, 20);      // 半径上
        Assert.InRange(canvas.GetColor(0, 0).R, 0, 20);        // 半径外被夹到终点色
    }

    [Fact]
    public void RadialGradientSupportsEllipse()
    {
        using Canvas canvas = Canvas.Create(120, 60);
        var brush = new RadialGradientBrush(
            new PointF(60, 30),
            new SizeF(50f, 20f),
            new ColorStop(0f, Color.White),
            new ColorStop(1f, Color.Black));

        new DrawingContext(canvas).Fill(brush, new RectangularPolygon(0, 0, 120, 60));

        // 椭圆边界上两点应为终点色，中心为起点色
        Assert.InRange(canvas.GetColor(60, 30).R, 245, 255);
        Assert.InRange(canvas.GetColor(109, 30).R, 0, 25);
        Assert.InRange(canvas.GetColor(60, 49).R, 0, 25);
    }

    [Fact]
    public void GradientWithIdenticalStopsStillHitsSolidFastPath()
    {
        var brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(10, 0),
            new ColorStop(0f, Color.Red),
            new ColorStop(1f, Color.Red));

        Assert.True(brush.TryGetSolidColor(out Color color));
        Assert.Equal(Color.Red, color);

        using Canvas canvas = Canvas.Create(20, 8);
        new DrawingContext(canvas).Fill(brush, new RectangularPolygon(0, 0, 20, 8));

        Assert.Equal(Color.Red, canvas.GetColor(10, 4));
    }

    [Fact]
    public void NonUniformGradientDoesNotReportSolidColor()
    {
        var brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(10, 0),
            new ColorStop(0f, Color.Red),
            new ColorStop(1f, Color.Blue));

        Assert.False(brush.TryGetSolidColor(out _));
    }

    [Fact]
    public void DitherIsDeterministicAndActuallyPerturbs()
    {
        var brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(100, 0),
            new ColorStop(0f, Color.Black),
            new ColorStop(1f, Color.White))
        {
            Dither = true,
        };

        var plain = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(100, 0),
            new ColorStop(0f, Color.Black),
            new ColorStop(1f, Color.White));

        using Canvas first = Canvas.Create(100, 8);
        using Canvas second = Canvas.Create(100, 8);
        using Canvas noDither = Canvas.Create(100, 8);
        new DrawingContext(first).Fill(brush, new RectangularPolygon(0, 0, 100, 8));
        new DrawingContext(second).Fill(brush, new RectangularPolygon(0, 0, 100, 8));
        new DrawingContext(noDither).Fill(plain, new RectangularPolygon(0, 0, 100, 8));

        Assert.Equal(first.Buffer, second.Buffer);
        Assert.NotEqual(first.Buffer, noDither.Buffer);
    }

    [Fact]
    public void PatternBrushTilesWithWrap()
    {
        using Canvas tile = Canvas.Create(2, 2);
        tile.SetColor(0, 0, Color.Red);
        tile.SetColor(1, 0, Color.Blue);
        tile.SetColor(0, 1, Color.Blue);
        tile.SetColor(1, 1, Color.Red);

        using Canvas canvas = Canvas.Create(4, 4);
        new DrawingContext(canvas).Fill(new PatternBrush(tile, new PointF(0, 0)), new RectangularPolygon(0, 0, 4, 4));

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Color expected = (x + y) % 2 == 0 ? Color.Red : Color.Blue;
                Assert.Equal(expected, canvas.GetColor(x, y));
            }
        }
    }

    [Fact]
    public void PatternBrushOriginShiftsPhase()
    {
        using Canvas tile = Canvas.Create(2, 1);
        tile.SetColor(0, 0, Color.Red);
        tile.SetColor(1, 0, Color.Blue);

        using Canvas canvas = Canvas.Create(3, 1);
        new DrawingContext(canvas).Fill(new PatternBrush(tile, new PointF(1, 0)), new RectangularPolygon(0, 0, 3, 1));

        Assert.Equal(Color.Blue, canvas.GetColor(0, 0));
        Assert.Equal(Color.Red, canvas.GetColor(1, 0));
        Assert.Equal(Color.Blue, canvas.GetColor(2, 0));
    }

    [Fact]
    public void PatternBrushWithUniformTileReportsSolidColor()
    {
        using Canvas tile = Canvas.Create(3, 3, Color.FromRgb(10, 20, 30));
        var brush = new PatternBrush(tile, new PointF(0, 0));

        Assert.True(brush.TryGetSolidColor(out Color color));
        Assert.Equal(Color.FromRgb(10, 20, 30), color);

        using Canvas mixed = Canvas.Create(2, 2);
        mixed.SetColor(1, 1, Color.White);
        Assert.False(new PatternBrush(mixed, new PointF(0, 0)).TryGetSolidColor(out _));

        // 半透明瓦片反预乘有精度损失，因此不当纯色快路径处理
        using Canvas translucent = Canvas.Create(3, 3, Color.FromRgba(10, 20, 30, 200));
        Assert.False(new PatternBrush(translucent, new PointF(0, 0)).TryGetSolidColor(out _));
    }

    [Fact]
    public void PatternBrushBlendsSemiTransparentTile()
    {
        using Canvas tile = Canvas.Create(2, 2, Color.FromRgba(255, 255, 255, 128));
        using Canvas canvas = Canvas.Create(4, 4, Color.Black);

        new DrawingContext(canvas).Fill(new PatternBrush(tile, new PointF(0, 0)), new RectangularPolygon(0, 0, 4, 4));

        Color blended = canvas.GetColor(2, 2);
        Assert.InRange(blended.R, 126, 130);
        Assert.Equal(255, blended.A);
    }

    [Fact]
    public void GradientRespectsCoverageOnAntialiasedEdges()
    {
        using Canvas canvas = Canvas.Create(60, 60);
        var brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(60, 0),
            new ColorStop(0f, Color.Black),
            new ColorStop(1f, Color.Black));

        new DrawingContext(canvas).Fill(brush, new CirclePolygon(30, 30, 20f));

        Assert.InRange(AlphaArea(canvas), Math.PI * 400 * 0.97, Math.PI * 400 * 1.03);
        Assert.True(canvas.GetColor(0, 0).A == 0);
    }
}
