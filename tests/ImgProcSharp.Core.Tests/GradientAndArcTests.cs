using System.Numerics;
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

    [Fact]
    public void RadialGradientIsSymmetricAroundCenter()
    {
        using Canvas canvas = Canvas.Create(200, 200, Color.White);

        // 圆心放在像素中心 (100.5, 100.5)，四个方向的对称点才是整数像素下标
        var brush = new RadialGradientBrush(
            new PointF(100.5f, 100.5f),
            60f,
            new ColorStop(0f, new Color(255, 0, 0)),
            new ColorStop(1f, new Color(0, 0, 255)));

        new DrawingContext(canvas).Fill(brush, new RectangularPolygon(0, 0, 200, 200));

        // 圆形渐变关于圆心旋转对称：同一半径上的四个方向必须是同一个颜色。
        // 行游标的二阶差分只沿 x 推进，ny 项若被错误地当成随 x 变化，这里会立刻不对称。
        foreach (int d in new[] { 10, 25, 40 })
        {
            Color right = canvas.GetColor(100 + d, 100);
            Color left = canvas.GetColor(100 - d, 100);
            Color down = canvas.GetColor(100, 100 + d);
            Color up = canvas.GetColor(100, 100 - d);

            Assert.InRange(Math.Abs(right.R - down.R), 0, 1);
            Assert.InRange(Math.Abs(right.R - left.R), 0, 1);
            Assert.InRange(Math.Abs(right.R - up.R), 0, 1);
        }

        // 半径 30 处 t = 0.5：红 → 蓝的中点
        Color middle = canvas.GetColor(130, 100);
        Assert.InRange(middle.R, 122, 133);
        Assert.InRange(middle.B, 122, 133);
    }

    [Fact]
    public void RadialGradientFollowsRotation()
    {
        // 宽高不同的椭圆旋转 90° 后形状变了，因此能验证「逆矩阵 + 二次差分」这条路径
        var brush = new RadialGradientBrush(
            new PointF(100, 100),
            new SizeF(80f, 20f),
            new ColorStop(0f, new Color(255, 0, 0)),
            new ColorStop(1f, new Color(0, 0, 255)));

        using Canvas canvas = Canvas.Create(200, 200, Color.White);
        var context = new DrawingContext(canvas);
        context.PushTransform(Matrix3x2.CreateRotation(MathF.PI / 2f, new Vector2(100f, 100f)));
        context.Fill(brush, new RectangularPolygon(0, 0, 200, 200));

        // 用户 (160,100) 在椭圆长轴上，t = 60/80 = 0.75；旋转后落在画布 (100,160)
        Assert.InRange(canvas.GetColor(100, 160).R, 55, 72);

        // 用户 (140,100)：t = 40/80 = 0.5 → 中段紫色
        Color middle = canvas.GetColor(100, 140);
        Assert.InRange(middle.R, 118, 134);
        Assert.InRange(middle.B, 118, 134);
    }

    [Fact]
    public void CustomGradientBrushFallsBackToParameterAt()
    {
        // 外部派生类没有行游标，必须走逐像素 ParameterAt 的慢路径（行为与 0.2 之前一致）
        using Canvas canvas = Canvas.Create(80, 40, Color.White);
        new DrawingContext(canvas).Fill(new HorizontalBrush(60f), new RectangularPolygon(0, 0, 80, 40));

        Assert.InRange(canvas.GetColor(0, 5).R, 0, 12);
        Assert.InRange(canvas.GetColor(30, 5).R, 118, 138);
        Assert.InRange(canvas.GetColor(60, 5).R, 248, 255);

        // 同一列不同行必须完全一致（参数只依赖 x）
        Assert.Equal(canvas.GetColor(30, 5), canvas.GetColor(30, 35));
    }

    [Fact]
    public void IncrementalGradientMatchesPerPixelPathWithinOneLsb()
    {
        // 行游标是差分累计，逐像素路径每像素重算几何：两者参数只差最后一个 float ulp，
        // 映射到 8 位颜色后不应超过 1 LSB。更大的差说明差分推错了，不是浮点噪声。
        // 只比「不跟随变换」的情形：自定义派生类不跟随变换（§Y），带变换时两条路径看到的坐标系不同。
        ColorStop[] stops =
        [
            new(0f, Color.Red),
            new(0.5f, Color.White),
            new(1f, new Color(0, 0, 255)),
        ];

        var from = new PointF(10f, 20f);
        var to = new PointF(170f, 120f);
        Compare(new LinearGradientBrush(from, to, stops), new LinearMirrorBrush(from, to, stops));

        // 径向渐变走二阶差分 + 每像素开方，误差特征与线性不同，单独比一次
        var center = new PointF(90f, 70f);
        var radius = new SizeF(70f, 50f);
        Compare(new RadialGradientBrush(center, radius, stops), new RadialMirrorBrush(center, radius, stops));

        void Compare(GradientBrush fastBrush, GradientBrush slowBrush)
        {
            using Canvas fast = Canvas.Create(180, 140, Color.White);
            using Canvas slow = Canvas.Create(180, 140, Color.White);
            var region = new RectangularPolygon(0, 0, 180, 140);

            new DrawingContext(fast).Fill(fastBrush, region);
            new DrawingContext(slow).Fill(slowBrush, region);

            int max = 0;
            for (int y = 0; y < 140; y++)
            {
                ReadOnlySpan<byte> a = fast.GetRow(y);
                ReadOnlySpan<byte> b = slow.GetRow(y);
                for (int i = 0; i < a.Length; i++)
                {
                    max = Math.Max(max, Math.Abs(a[i] - b[i]));
                }
            }

            Assert.InRange(max, 0, 1);
        }
    }

    /// <summary>自定义渐变：参数就是 x / width，用来覆盖「没有行游标」的兜底路径。</summary>
    private sealed class HorizontalBrush(float width)
        : GradientBrush([new ColorStop(0f, Color.Black), new ColorStop(1f, Color.White)])
    {
        protected override float ParameterAt(float x, float y) => x / width;
    }

    /// <summary>与 <see cref="LinearGradientBrush"/> 同几何的自定义派生类：逐像素重算参数，用作行游标的对照。</summary>
    private sealed class LinearMirrorBrush(PointF from, PointF to, ColorStop[] stops) : GradientBrush(stops)
    {
        protected override float ParameterAt(float x, float y)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float lengthSquared = (dx * dx) + (dy * dy);
            return lengthSquared <= 1e-12f ? 1f : (((x - from.X) * dx) + ((y - from.Y) * dy)) / lengthSquared;
        }
    }

    /// <summary>与 <see cref="RadialGradientBrush"/> 同几何的自定义派生类：校验「二阶差分 + 开方」。</summary>
    private sealed class RadialMirrorBrush(PointF center, SizeF radius, ColorStop[] stops) : GradientBrush(stops)
    {
        protected override float ParameterAt(float x, float y)
        {
            float ax = MathF.Abs(radius.Width);
            float ay = MathF.Abs(radius.Height);
            if (ax <= 1e-6f || ay <= 1e-6f)
            {
                return 1f;
            }

            float nx = (x - center.X) / ax;
            float ny = (y - center.Y) / ay;
            return MathF.Sqrt((nx * nx) + (ny * ny));
        }
    }
}
