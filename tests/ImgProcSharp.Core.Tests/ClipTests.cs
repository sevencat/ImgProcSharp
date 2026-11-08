using System.Numerics;
using Xunit;

namespace ImgProcSharp.Tests;


public class ClipTests
{
    [Fact]
    public void PushClipLimitsFill()
    {
        using Canvas canvas = Canvas.Create(100, 100, Color.White);
        var context = new DrawingContext(canvas);

        context.PushClip(new RectangularPolygon(0, 0, 50, 100));
        context.Fill(Color.Black, new RectangularPolygon(0, 0, 100, 100));

        Assert.True(context.HasClip);
        Assert.Equal(Color.Black, canvas.GetColor(0, 0));
        Assert.Equal(Color.Black, canvas.GetColor(49, 99));
        Assert.Equal(Color.White, canvas.GetColor(51, 50));
        Assert.Equal(Color.White, canvas.GetColor(99, 99));
    }

    [Fact]
    public void PopClipRestoresFullCanvas()
    {
        using Canvas canvas = Canvas.Create(100, 100, Color.White);
        var context = new DrawingContext(canvas);

        context.PushClip(new RectangularPolygon(0, 0, 50, 100));
        context.PopClip();
        context.Fill(Color.Black, new RectangularPolygon(0, 0, 100, 100));

        Assert.False(context.HasClip);
        Assert.Equal(0, context.ClipDepth);
        for (int x = 0; x < 100; x += 10)
        {
            Assert.Equal(Color.Black, canvas.GetColor(x, 50));
        }
    }

    [Fact]
    public void NestedClipsIntersect()
    {
        using Canvas canvas = Canvas.Create(100, 100, Color.White);
        var context = new DrawingContext(canvas);

        context.PushClip(new RectangularPolygon(0, 0, 60, 60));
        context.PushClip(new RectangularPolygon(30, 30, 100, 100));
        context.Fill(Color.Black, new RectangularPolygon(0, 0, 100, 100));

        Assert.Equal(2, context.ClipDepth);
        Assert.Equal(Color.Black, canvas.GetColor(30, 30));
        Assert.Equal(Color.Black, canvas.GetColor(59, 59));
        Assert.Equal(Color.White, canvas.GetColor(20, 40));
        Assert.Equal(Color.White, canvas.GetColor(40, 20));
        Assert.Equal(Color.White, canvas.GetColor(70, 40));
        Assert.Equal(Color.White, canvas.GetColor(40, 70));
    }

    [Fact]
    public void ClipAppliesToStroke()
    {
        using Canvas canvas = Canvas.Create(200, 100, Color.White);
        var context = new DrawingContext(canvas);
        IPath line = new PathBuilder().MoveTo(20, 50).LineTo(180, 50).Build();

        context.PushClip(new RectangularPolygon(0, 0, 100, 100));
        context.Draw(new Pen(Color.Black, 10f), line);

        Assert.Equal(Color.Black, canvas.GetColor(80, 50));
        Assert.Equal(Color.White, canvas.GetColor(120, 50));
    }

    [Fact]
    public void ClipAppliesToTextFastPath()
    {
        using Canvas canvas = Canvas.Create(400, 120, Color.White);
        var context = new DrawingContext(canvas);

        context.PushClip(new RectangularPolygon(0, 0, 70, 120));
        context.DrawText("HHHH", new PointF(8, 20), new TextOptions { Size = 48, Color = Color.Black });

        Assert.True(CountInk(canvas, 0, 0, 70, 120) > 0);
        Assert.Equal(0, CountInk(canvas, 70, 0, 330, 120));
    }

    [Fact]
    public void ClipAppliesToGradientFill()
    {
        using Canvas canvas = Canvas.Create(100, 100, Color.White);
        var context = new DrawingContext(canvas);
        var brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(100, 0),
            new ColorStop(0f, Color.Red),
            new ColorStop(1f, Color.Blue));

        context.PushClip(new RectangularPolygon(0, 0, 50, 100));
        context.Fill(brush, new RectangularPolygon(0, 0, 100, 100));

        Assert.Equal(Color.White, canvas.GetColor(60, 50));
        Color inside = canvas.GetColor(1, 50);
        Assert.True(inside.R > 240 && inside.B < 15, $"裁剪内应当是渐变起点附近的红：{inside}");
    }

    [Fact]
    public void ClipEdgeIsAntialiased()
    {
        using Canvas canvas = Canvas.Create(100, 100, Color.White);
        var context = new DrawingContext(canvas);
        IPath triangle = new PathBuilder()
            .MoveTo(10, 10)
            .LineTo(90, 40)
            .LineTo(10, 90)
            .CloseFigure()
            .Build();

        context.PushClip(triangle);
        context.Fill(Color.Black, new RectangularPolygon(0, 0, 100, 100));

        // 斜边上必然出现 0 < 通道 < 255 的中间值，说明裁剪掩码本身带 AA
        int intermediate = 0;
        for (int y = 0; y < 100; y++)
        {
            for (int x = 0; x < 100; x++)
            {
                byte r = canvas.GetColor(x, y).R;
                if (r is > 0 and < 255)
                {
                    intermediate++;
                }
            }
        }

        Assert.True(intermediate > 20, $"斜边上的中间值太少：{intermediate}");
    }

    [Fact]
    public void ClipFollowsCurrentTransform()
    {
        using Canvas canvas = Canvas.Create(100, 100, Color.White);
        var context = new DrawingContext(canvas);

        context.PushTransform(Matrix3x2.CreateTranslation(50, 0));
        context.PushClip(new RectangularPolygon(0, 0, 50, 100));
        context.Fill(Color.Black, new RectangularPolygon(-100, -100, 400, 400));

        // 用户坐标 x∈[0,50) 平移后落在画布 x∈[50,100)
        Assert.Equal(Color.White, canvas.GetColor(25, 50));
        Assert.Equal(Color.Black, canvas.GetColor(55, 50));
        Assert.Equal(Color.Black, canvas.GetColor(98, 50));
    }

    [Fact]
    public void ClipOutsideCanvasDrawsNothing()
    {
        using Canvas canvas = Canvas.Create(100, 100, Color.White);
        var context = new DrawingContext(canvas);

        context.PushClip(new RectangularPolygon(200, 200, 50, 50));
        context.Fill(Color.Black, new RectangularPolygon(0, 0, 100, 100));

        Assert.Equal(0, CountInk(canvas, 0, 0, 100, 100));
    }

    [Fact]
    public void PopClipWithoutPushThrows()
    {
        using Canvas canvas = Canvas.Create(10, 10);
        var context = new DrawingContext(canvas);

        Assert.Throws<InvalidOperationException>(context.PopClip);
    }

    [Fact]
    public void PushClipRejectsNull()
    {
        using Canvas canvas = Canvas.Create(10, 10);
        var context = new DrawingContext(canvas);

        Assert.Throws<ArgumentNullException>(() => context.PushClip(null!));
    }

    private static int CountInk(Canvas canvas, int x, int y, int width, int height)
    {
        int ink = 0;
        for (int row = y; row < y + height; row++)
        {
            for (int column = x; column < x + width; column++)
            {
                if (canvas.GetColor(column, row) != Color.White)
                {
                    ink++;
                }
            }
        }

        return ink;
    }
}
