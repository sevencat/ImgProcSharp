using System.Numerics;
using Xunit;

namespace ImgProcSharp.Tests;


public class StrokeAndTransformTests
{
    [Fact]
    public void StrokeHorizontalLineCoversFullThickness()
    {
        using Canvas canvas = Canvas.Create(200, 100, Color.White);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(50, 50).LineTo(150, 50).Build();

        context.Draw(new Pen(Color.Black, 10f), line);

        // 半宽 5：y∈(45,55) 全覆盖
        Assert.Equal(Color.Black, canvas.GetColor(100, 50));
        Assert.Equal(Color.Black, canvas.GetColor(100, 45));
        Assert.Equal(Color.Black, canvas.GetColor(100, 54));
        Assert.Equal(Color.White, canvas.GetColor(100, 44));
        Assert.Equal(Color.White, canvas.GetColor(100, 55));

        // 端点带圆头：x=46 时仍在半径 5 以内
        Assert.Equal(Color.Black, canvas.GetColor(46, 50));
        Assert.Equal(Color.White, canvas.GetColor(38, 50));
    }

    [Fact]
    public void StrokeCapsAreRoundNotButt()
    {
        using Canvas canvas = Canvas.Create(200, 100, Color.White);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(50, 50).LineTo(150, 50).Build();

        context.Draw(new Pen(Color.Black, 20f), line);

        // 方头端点会在 (44,44) 留下空白；圆头端点距 (50,50) 只有 8.49 < 10
        Assert.Equal(Color.Black, canvas.GetColor(44, 44));
        Assert.Equal(Color.White, canvas.GetColor(38, 38));
    }

    [Fact]
    public void StrokeVertexGetsRoundJoin()
    {
        using Canvas canvas = Canvas.Create(200, 200, Color.White);
        var context = new DrawingContext(canvas);
        IPath chevron = new PathBuilder().MoveTo(50, 50).LineTo(100, 100).LineTo(150, 50).Build();

        context.Draw(new Pen(Color.Black, 20f), chevron);

        // 尖角外侧：距顶点 8 < 半径 10
        Assert.Equal(Color.Black, canvas.GetColor(100, 108));
        Assert.Equal(Color.White, canvas.GetColor(100, 112));
        Assert.Equal(Color.Black, canvas.GetColor(100, 100));
    }

    [Fact]
    public void StrokeClosedSquareIsHollow()
    {
        using Canvas canvas = Canvas.Create(200, 200, Color.White);
        var context = new DrawingContext(canvas);
        IPath square = new PathBuilder()
            .MoveTo(50, 50)
            .LineTo(150, 50)
            .LineTo(150, 150)
            .LineTo(50, 150)
            .CloseFigure()
            .Build();

        context.Draw(new Pen(Color.Black, 10f), square);

        Assert.Equal(Color.White, canvas.GetColor(100, 100));
        Assert.Equal(Color.Black, canvas.GetColor(100, 50));
        Assert.Equal(Color.Black, canvas.GetColor(50, 100));
        Assert.Equal(Color.White, canvas.GetColor(100, 40));
    }

    [Fact]
    public void StrokeThicknessIsHonouredAcrossManyWidths()
    {
        // 线落在 y=60 的整数格上，偶数宽一定覆盖整行
        foreach (float width in new[] { 2f, 4f, 7f, 16f })
        {
            using Canvas canvas = Canvas.Create(120, 120, Color.White);
            var context = new DrawingContext(canvas);
            IPath line = new PathBuilder().MoveTo(10, 60).LineTo(110, 60).Build();

            context.Draw(new Pen(Color.Black, width), line);

            float half = width * 0.5f;
            int outside = (int)MathF.Floor(60f + half) + 2;

            Assert.Equal(Color.Black, canvas.GetColor(60, 60));
            Assert.Equal(Color.White, canvas.GetColor(60, outside));
        }
    }

    [Fact]
    public void StrokePathWithoutSegmentsDrawsNothing()
    {
        using Canvas canvas = Canvas.Create(20, 20);
        var context = new DrawingContext(canvas);
        IPath dot = new PathBuilder().MoveTo(10, 10).Build();

        context.Draw(new Pen(Color.Black, 4f), dot);

        // 展平后没有任何点：不画（要画点请用 EllipsePolygon 或自己画一小段线）
        Assert.Equal(0, CountInk(canvas, 0, 0, 20, 20));
    }

    [Fact]
    public void DrawRejectsNonPositiveWidth()
    {
        using Canvas canvas = Canvas.Create(20, 20);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(0, 0).LineTo(10, 10).Build();

        Assert.Throws<ArgumentOutOfRangeException>(() => context.Draw(new BrokenPen(0f), line));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.Draw(Color.Black, -1f, line));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(Color.Black, 0f));
    }

    [Fact]
    public void PushTransformShiftsSubsequentFill()
    {
        using Canvas canvas = Canvas.Create(40, 40, Color.White);
        var context = new DrawingContext(canvas);

        context.PushTransform(Matrix3x2.CreateTranslation(10, 20));
        context.Fill(Color.Red, new RectangularPolygon(0, 0, 5, 5));

        Assert.Equal(Color.Red, canvas.GetColor(12, 22));
        Assert.Equal(Color.Red, canvas.GetColor(10, 20));
        Assert.Equal(Color.White, canvas.GetColor(16, 22));
        Assert.Equal(Color.White, canvas.GetColor(2, 2));
    }

    [Fact]
    public void NestedTransformsCompose()
    {
        using Canvas canvas = Canvas.Create(100, 100, Color.White);
        var context = new DrawingContext(canvas);

        context.PushTransform(Matrix3x2.CreateTranslation(50, 50));
        context.PushTransform(Matrix3x2.CreateRotation(MathF.PI / 2f));
        context.Fill(Color.Red, new RectangularPolygon(10, 0, 10, 5));

        // 旋转先行：x∈[10,20], y∈[0,5] → (-y, x) → 再加 50 → x∈[45,50], y∈[60,70]
        Assert.Equal(Color.Red, canvas.GetColor(47, 65));
        Assert.Equal(Color.White, canvas.GetColor(55, 65));
        Assert.Equal(Color.White, canvas.GetColor(65, 47));
    }

    [Fact]
    public void ResetTransformRestoresIdentity()
    {
        using Canvas canvas = Canvas.Create(40, 40, Color.White);
        var context = new DrawingContext(canvas);

        context.PushTransform(Matrix3x2.CreateTranslation(10, 10));
        context.ResetTransform();
        context.Fill(Color.Red, new RectangularPolygon(0, 0, 5, 5));

        Assert.True(context.Transform.IsIdentity);
        Assert.Equal(Color.Red, canvas.GetColor(2, 2));
        Assert.Equal(Color.White, canvas.GetColor(12, 12));
    }

    [Fact]
    public void TransformScalesStrokeWidth()
    {
        using Canvas canvas = Canvas.Create(200, 200, Color.White);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(25, 25).LineTo(75, 25).Build();

        context.PushTransform(Matrix3x2.CreateScale(2f, 2f));
        context.Draw(new Pen(Color.Black, 5f), line);

        // 变换后：(50,50)-(150,50)，线宽 5 × 2 = 10
        Assert.Equal(Color.Black, canvas.GetColor(100, 46));
        Assert.Equal(Color.White, canvas.GetColor(100, 44));
        Assert.Equal(Color.White, canvas.GetColor(20, 50));
    }

    [Fact]
    public void TransformAppliesToText()
    {
        using Canvas canvas = Canvas.Create(200, 200);
        var context = new DrawingContext(canvas);
        var options = new TextOptions { Size = 24f };

        context.PushTransform(Matrix3x2.CreateScale(2f, 2f));
        context.DrawText("H", new PointF(10, 30), options);

        // 无变换时同一段文字的墨迹在 (12,34)-(24,51)，2 倍缩放后应落在 (24,68)-(48,102) 附近
        Assert.True(CountInk(canvas, 20, 65, 45, 50) > 0);
        Assert.Equal(0, CountInk(canvas, 0, 0, 200, 60));
        Assert.Equal(0, CountInk(canvas, 0, 0, 20, 200));
    }

    [Fact]
    public void GetTextPathMatchesDrawTextExtent()
    {
        using Canvas canvas = Canvas.Create(200, 100);
        var context = new DrawingContext(canvas);
        var options = new TextOptions { Size = 32f };

        Path path = TextDrawing.GetTextPath("Hi", new PointF(10, 40), options);
        context.Draw(new Pen(Color.Black, 1f), path);

        Assert.True(CountInk(canvas, 0, 0, 200, 100) > 0);
        Assert.Equal(0, CountInk(canvas, 150, 0, 50, 100));
    }

    private static int CountInk(Canvas canvas, int x, int y, int width, int height)
    {
        int right = Math.Min(canvas.Width, x + width);
        int bottom = Math.Min(canvas.Height, y + height);
        int count = 0;

        for (int row = Math.Max(0, y); row < bottom; row++)
        {
            for (int column = Math.Max(0, x); column < right; column++)
            {
                if (canvas.GetColor(column, row).A > 16)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private sealed class BrokenPen(float width) : IPen
    {
        public Color Color => Color.Black;

        public float Width => width;
    }
}
