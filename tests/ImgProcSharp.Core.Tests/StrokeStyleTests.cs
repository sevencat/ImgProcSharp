using System.Numerics;
using Xunit;

namespace ImgProcSharp.Tests;


/// <summary>M6：连接方式、端点形状、MiterLimit 与虚线图案。</summary>
public class StrokeStyleTests
{
    [Fact]
    public void ButtCapEndsExactlyAtTheEndpoint()
    {
        using Canvas canvas = Canvas.Create(200, 100, Color.White);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(50, 50).LineTo(150, 50).Build();

        context.Draw(new Pen(Color.Black, 20f) { LineCap = LineCap.Butt }, line);

        Assert.Equal(Color.Black, canvas.GetColor(100, 50));
        Assert.Equal(Color.Black, canvas.GetColor(52, 50));
        Assert.Equal(Color.White, canvas.GetColor(48, 50));

        // 圆头会在 x=46（距端点 4.5）留下墨水，平头不会
        Assert.Equal(Color.White, canvas.GetColor(46, 50));
    }

    [Fact]
    public void SquareCapExtendsHalfWidthAlongTheTangent()
    {
        using Canvas canvas = Canvas.Create(200, 100, Color.White);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(50, 50).LineTo(150, 50).Build();

        context.Draw(new Pen(Color.Black, 20f) { LineCap = LineCap.Square }, line);

        // 方头把 x∈[40,50] 的整个 y∈[40,60] 方块涂满（圆头在 (42,42) 会留白）
        Assert.Equal(Color.Black, canvas.GetColor(44, 50));
        Assert.Equal(Color.Black, canvas.GetColor(42, 42));
        Assert.Equal(Color.White, canvas.GetColor(39, 50));

        // 右侧同样延伸半个线宽：150 → 160
        Assert.Equal(Color.Black, canvas.GetColor(158, 50));
        Assert.Equal(Color.White, canvas.GetColor(161, 50));
    }

    [Fact]
    public void MiterJoinFillsTheSharpCornerBeyondTheVertex()
    {
        using Canvas canvas = Canvas.Create(200, 200, Color.White);
        var context = new DrawingContext(canvas);
        IPath chevron = new PathBuilder().MoveTo(50, 50).LineTo(100, 100).LineTo(150, 50).Build();

        context.Draw(
            new Pen(Color.Black, 20f) { LineJoin = LineJoin.Miter, MiterLimit = 4f },
            chevron);

        // 尖角顶点伸出 r·√2 ≈ 14.1，bevel 只到 r ≈ 10
        Assert.Equal(Color.Black, canvas.GetColor(100, 109));
        Assert.Equal(Color.White, canvas.GetColor(100, 116));
    }

    [Fact]
    public void BevelJoinCutsTheCornerFlat()
    {
        using Canvas canvas = Canvas.Create(200, 200, Color.White);
        var context = new DrawingContext(canvas);
        IPath chevron = new PathBuilder().MoveTo(50, 50).LineTo(100, 100).LineTo(150, 50).Build();

        context.Draw(new Pen(Color.Black, 20f) { LineJoin = LineJoin.Bevel }, chevron);

        // 斜切边在 y = 100 + r ≈ 107；圆角连接（半径 10）会盖到 109.5
        Assert.Equal(Color.Black, canvas.GetColor(100, 104));
        Assert.Equal(Color.White, canvas.GetColor(100, 109));
    }

    [Fact]
    public void MiterLimitFallsBackToBevel()
    {
        using Canvas canvas = Canvas.Create(200, 200, Color.White);
        var context = new DrawingContext(canvas);
        IPath chevron = new PathBuilder().MoveTo(50, 50).LineTo(100, 100).LineTo(150, 50).Build();

        // 90° 转角需要的比值是 1/sin(45°) ≈ 1.414，限制到 1.2 就必须退回 bevel
        context.Draw(
            new Pen(Color.Black, 20f) { LineJoin = LineJoin.Miter, MiterLimit = 1.2f },
            chevron);

        Assert.Equal(Color.Black, canvas.GetColor(100, 104));
        Assert.Equal(Color.White, canvas.GetColor(100, 109));
        Assert.Equal(Color.White, canvas.GetColor(100, 112));
    }

    [Fact]
    public void DashPatternAlternatesOnAndOffInWidthMultiples()
    {
        using Canvas canvas = Canvas.Create(200, 100, Color.White);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(0, 50).LineTo(200, 50).Build();

        // 线宽 4、图案 [10, 10] → 40 px 实 / 40 px 虚
        context.Draw(
            new Pen(Color.Black, 4f) { LineCap = LineCap.Butt, DashPattern = [10f, 10f] },
            line);

        Assert.Equal(Color.Black, canvas.GetColor(10, 50));
        Assert.Equal(Color.Black, canvas.GetColor(38, 50));
        Assert.Equal(Color.White, canvas.GetColor(42, 50));
        Assert.Equal(Color.White, canvas.GetColor(70, 50));
        Assert.Equal(Color.Black, canvas.GetColor(100, 50));
        Assert.Equal(Color.White, canvas.GetColor(130, 50));
        Assert.Equal(Color.Black, canvas.GetColor(190, 50));
    }

    [Fact]
    public void DashOffsetShiftsThePattern()
    {
        using Canvas canvas = Canvas.Create(200, 100, Color.White);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(0, 50).LineTo(200, 50).Build();

        // 偏移 10 个线宽 = 40 px：前 40 px 变成虚
        context.Draw(
            new Pen(Color.Black, 4f)
            {
                LineCap = LineCap.Butt,
                DashPattern = [10f, 10f],
                DashOffset = 10f,
            },
            line);

        Assert.Equal(Color.White, canvas.GetColor(20, 50));
        Assert.Equal(Color.Black, canvas.GetColor(60, 50));
        Assert.Equal(Color.White, canvas.GetColor(100, 50));
    }

    [Fact]
    public void DashPatternScalesWithStrokeWidth()
    {
        using Canvas canvas = Canvas.Create(200, 100, Color.White);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(0, 50).LineTo(200, 50).Build();

        // 图案不变、线宽翻倍 → 实/虚段也翻倍：20 px 实 / 20 px 虚
        context.Draw(
            new Pen(Color.Black, 2f) { LineCap = LineCap.Butt, DashPattern = [10f, 10f] },
            line);

        Assert.Equal(Color.Black, canvas.GetColor(10, 50));
        Assert.Equal(Color.White, canvas.GetColor(30, 50));
        Assert.Equal(Color.Black, canvas.GetColor(50, 50));
        Assert.Equal(Color.White, canvas.GetColor(70, 50));
    }

    [Fact]
    public void DashPatternOnClosedPathKeepsGapsOnAllSides()
    {
        using Canvas canvas = Canvas.Create(200, 200, Color.White);
        var context = new DrawingContext(canvas);
        IPath square = new RectangularPolygon(20, 20, 160, 160);

        // 周长 640、图案 80，整圈刚好 8 个周期；起点不落在虚线上，但四条边都应该有缺口
        context.Draw(
            new Pen(Color.Black, 4f) { LineCap = LineCap.Butt, DashPattern = [10f, 10f] },
            square);

        // 上边：s = x - 20；右边：s = 160 + (y - 20)；下边：s = 320 + (180 - x)；左边：s = 480 + (180 - y)
        Assert.Equal(Color.Black, canvas.GetColor(30, 20));
        Assert.Equal(Color.White, canvas.GetColor(80, 20));
        Assert.Equal(Color.White, canvas.GetColor(180, 70));
        Assert.Equal(Color.White, canvas.GetColor(120, 180));
        Assert.Equal(Color.White, canvas.GetColor(20, 120));
        Assert.Equal(Color.White, canvas.GetColor(100, 100));
    }

    [Fact]
    public void DashedClosedPathMergesTheRunAcrossTheStart()
    {
        using Canvas canvas = Canvas.Create(200, 200, Color.White);
        var context = new DrawingContext(canvas);
        IPath square = new RectangularPolygon(20, 20, 160, 160);

        // 周长 640、图案 70+50=120 → 640 mod 120 = 40，起点与终点都落在实线段内。
        // 两段必须合成一条，起点处补「连接」而不是两个平头端帽，否则外角会缺一块 r×r。
        context.Draw(
            new Pen(Color.Black, 10f) { LineCap = LineCap.Butt, DashPattern = [7f, 5f] },
            square);

        Assert.Equal(Color.Black, canvas.GetColor(20, 50));
        Assert.Equal(Color.Black, canvas.GetColor(17, 17));
        Assert.Equal(Color.White, canvas.GetColor(100, 20));
    }

    [Fact]
    public void DashPatternLongerThanPerimeterMatchesSolidStroke()
    {
        using Canvas dashedCanvas = Canvas.Create(120, 120, Color.White);
        using Canvas solidCanvas = Canvas.Create(120, 120, Color.White);
        IPath square = new RectangularPolygon(30, 30, 60, 60);

        new DrawingContext(dashedCanvas).Draw(
            new Pen(Color.Black, 6f) { DashPattern = [1000f, 10f] },
            square);
        new DrawingContext(solidCanvas).Draw(new Pen(Color.Black, 6f), square);

        // 整圈都在实线段内：应退化成闭合描边（起点处补连接而不是两个端帽）
        for (int y = 0; y < 120; y++)
        {
            for (int x = 0; x < 120; x++)
            {
                Assert.Equal(solidCanvas.GetColor(x, y), dashedCanvas.GetColor(x, y));
            }
        }
    }

    [Fact]
    public void MinimalPenUsesInterfaceDefaults()
    {
        using Canvas canvas = Canvas.Create(200, 100, Color.White);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(50, 50).LineTo(150, 50).Build();

        // 只实现 Color/Width 的画笔：默认圆头端点 + 圆角连接 + 实线
        context.Draw(new MinimalPen(Color.Black, 20f), line);

        Assert.Equal(Color.Black, canvas.GetColor(100, 50));
        Assert.Equal(Color.Black, canvas.GetColor(46, 50));
        Assert.Equal(Color.Black, canvas.GetColor(44, 44));
    }

    [Fact]
    public void DrawRejectsInvalidStrokeStyles()
    {
        using Canvas canvas = Canvas.Create(40, 40);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(0, 0).LineTo(20, 20).Build();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => context.Draw(new InvalidPen { LineJoinValue = (LineJoin)99 }, line));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => context.Draw(new InvalidPen { LineCapValue = (LineCap)99 }, line));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => context.Draw(new InvalidPen { MiterLimitValue = 0.5f }, line));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => context.Draw(new InvalidPen { DashValue = [-1f, 2f] }, line));
        Assert.Throws<ArgumentException>(
            () => context.Draw(new InvalidPen { DashValue = [0f, 0f] }, line));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => context.Draw(new InvalidPen { DashOffsetValue = float.NaN }, line));
    }

    [Fact]
    public void PenValidatesAndNormalisesStyle()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new Pen(Color.Black, 1f) { MiterLimit = 0.99f });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new Pen(Color.Black, 1f) { DashPattern = [1f, -1f] });
        Assert.Throws<ArgumentException>(
            () => _ = new Pen(Color.Black, 1f) { DashPattern = [0f, 0f] });

        // 奇数长度按 SVG 语义重复一遍
        var pen = new Pen(Color.Black, 2f) { DashPattern = [6f] };

        Assert.NotNull(pen.DashPattern);
        Assert.Equal(new[] { 6f, 6f }, pen.DashPattern!);
        Assert.Equal(LineJoin.Round, pen.LineJoin);
        Assert.Equal(LineCap.Round, pen.LineCap);
        Assert.Equal(4f, pen.MiterLimit);
    }

    [Fact]
    public void DashAndJoinSurviveTransformScaling()
    {
        using Canvas canvas = Canvas.Create(200, 200, Color.White);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(0, 25).LineTo(100, 25).Build();

        // 2 倍缩放：路径变 200 px，线宽 2→4，图案 [10,10] → 40 px 实 / 40 px 虚
        context.PushTransform(Matrix3x2.CreateScale(2f, 2f));
        context.Draw(
            new Pen(Color.Black, 2f) { LineCap = LineCap.Butt, DashPattern = [10f, 10f] },
            line);

        Assert.Equal(Color.Black, canvas.GetColor(20, 50));
        Assert.Equal(Color.White, canvas.GetColor(60, 50));
        Assert.Equal(Color.Black, canvas.GetColor(100, 50));
    }

    private sealed class MinimalPen(Color color, float width) : IPen
    {
        public Color Color => color;

        public float Width => width;
    }

    private sealed class InvalidPen : IPen
    {
        public Color Color => Color.Black;

        public float Width => 4f;

        public LineJoin LineJoinValue { get; init; } = LineJoin.Round;

        public LineCap LineCapValue { get; init; } = LineCap.Round;

        public float MiterLimitValue { get; init; } = 4f;

        public float[]? DashValue { get; init; }

        public float DashOffsetValue { get; init; }

        public LineJoin LineJoin => LineJoinValue;

        public LineCap LineCap => LineCapValue;

        public float MiterLimit => MiterLimitValue;

        public float[]? DashPattern => DashValue;

        public float DashOffset => DashOffsetValue;
    }
}
