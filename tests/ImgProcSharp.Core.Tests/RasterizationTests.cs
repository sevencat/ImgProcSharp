using Xunit;

namespace ImgProcSharp.Tests;

public class RasterizationTests
{
    [Fact]
    public void FilledRectangleCoversExactPixels()
    {
        using Canvas canvas = Canvas.Create(20, 20);
        var context = new DrawingContext(canvas);

        context.Fill(Color.Red, new RectangularPolygon(2, 3, 4, 5));

        Assert.Equal(Color.Red, canvas.GetColor(2, 3));
        Assert.Equal(Color.Red, canvas.GetColor(5, 7));
        Assert.Equal(0, canvas.GetColor(6, 7).A);
        Assert.Equal(0, canvas.GetColor(2, 8).A);
    }

    [Theory]
    [InlineData(0, 0, 5, 5)]
    [InlineData(10, 4, 3, 7)]
    [InlineData(2.5f, 3.25f, 5.5f, 4.75f)]
    [InlineData(-3.5f, -2.5f, 8.5f, 6.5f)]
    public void RectangleFastPathMatchesGenericPath(float x, float y, float width, float height)
    {
        using Canvas fast = Canvas.Create(16, 16);
        using Canvas generic = Canvas.Create(16, 16);
        var color = new Color(200, 120, 40, 160);

        new DrawingContext(fast).Fill(color, new RectangularPolygon(x, y, width, height));

        // 同样的轮廓用 PathBuilder 构造 → 命中通用扫描线
        IPath path = new PathBuilder()
            .MoveTo(x, y)
            .LineTo(x + width, y)
            .LineTo(x + width, y + height)
            .LineTo(x, y + height)
            .CloseFigure()
            .Build();

        new DrawingContext(generic).Fill(color, path);

        Assert.Equal(generic.Buffer, fast.Buffer);
    }

    [Theory]
    [InlineData(0, 0, 16, 16)]
    [InlineData(3, 2, 7, 9)]
    [InlineData(2.5f, 3.25f, 9.5f, 7.75f)]
    [InlineData(-4.5f, -3.5f, 12.5f, 10.5f)]
    public void OpaqueRectangleFastPathMatchesGenericPath(float x, float y, float width, float height)
    {
        // 底色半透明：不透明直写分支若写错（比如漏掉覆盖语义），逐字节对拍立刻暴露
        var background = new Color(10, 20, 30, 64);
        using Canvas fast = Canvas.Create(16, 16, background);
        using Canvas generic = Canvas.Create(16, 16, background);
        var color = new Color(200, 120, 40, 255);

        new DrawingContext(fast).Fill(color, new RectangularPolygon(x, y, width, height));

        IPath path = new PathBuilder()
            .MoveTo(x, y)
            .LineTo(x + width, y)
            .LineTo(x + width, y + height)
            .LineTo(x, y + height)
            .CloseFigure()
            .Build();

        new DrawingContext(generic).Fill(color, path);

        Assert.Equal(generic.Buffer, fast.Buffer);
    }

    [Fact]
    public void TriangleCoverageMatchesItsArea()
    {
        using Canvas canvas = Canvas.Create(24, 24);
        var context = new DrawingContext(canvas);

        IPath triangle = new PathBuilder()
            .MoveTo(2, 2)
            .LineTo(12, 2)
            .LineTo(2, 12)
            .CloseFigure()
            .Build();

        context.Fill(Color.Black, triangle);

        int sum = 0;
        for (int y = 0; y < canvas.Height; y++)
        {
            ReadOnlySpan<byte> row = canvas.GetRow(y);
            for (int x = 0; x < canvas.Width; x++)
            {
                sum += row[(x * 4) + 3];
            }
        }

        // 直角边 10 的等腰直角三角形，面积 50；覆盖率总和约 50 * 255
        double area = sum / 255.0;
        Assert.InRange(area, 48.0, 52.0);
    }

    [Fact]
    public void NonZeroFillsOverlapEvenOddDoesNot()
    {
        IPath overlapping = new PathBuilder()
            .MoveTo(2, 2).LineTo(10, 2).LineTo(10, 10).LineTo(2, 10).CloseFigure()
            .MoveTo(6, 2).LineTo(14, 2).LineTo(14, 10).LineTo(6, 10).CloseFigure()
            .Build();

        using Canvas nonZero = Canvas.Create(16, 16);
        using Canvas evenOdd = Canvas.Create(16, 16);

        new DrawingContext(nonZero).Fill(Color.Red, overlapping, FillRule.NonZero);
        new DrawingContext(evenOdd).Fill(Color.Red, overlapping, FillRule.EvenOdd);

        Assert.Equal(255, nonZero.GetColor(8, 6).A);
        Assert.Equal(255, nonZero.GetColor(4, 6).A);
        Assert.Equal(0, evenOdd.GetColor(8, 6).A);
        Assert.Equal(255, evenOdd.GetColor(4, 6).A);
    }

    [Fact]
    public void SameDirectionNestedContoursStayFilledWithNonZero()
    {
        IPath nested = new PathBuilder()
            .MoveTo(0, 0).LineTo(16, 0).LineTo(16, 16).LineTo(0, 16).CloseFigure()
            .MoveTo(4, 4).LineTo(12, 4).LineTo(12, 12).LineTo(4, 12).CloseFigure()
            .Build();

        using Canvas nonZero = Canvas.Create(16, 16);
        using Canvas evenOdd = Canvas.Create(16, 16);

        new DrawingContext(nonZero).Fill(Color.Red, nested, FillRule.NonZero);
        new DrawingContext(evenOdd).Fill(Color.Red, nested, FillRule.EvenOdd);

        Assert.Equal(255, nonZero.GetColor(8, 8).A);
        Assert.Equal(0, evenOdd.GetColor(8, 8).A);
    }

    [Fact]
    public void CircleIsAntialiased()
    {
        using Canvas canvas = Canvas.Create(40, 40);
        var context = new DrawingContext(canvas);

        context.Fill(Color.Blue, new CirclePolygon(20, 20, 12));

        Assert.Equal(255, canvas.GetColor(20, 20).A);
        Assert.Equal(0, canvas.GetColor(0, 0).A);

        bool hasPartial = false;
        for (int y = 0; y < canvas.Height && !hasPartial; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                int alpha = canvas.GetColor(x, y).A;
                if (alpha > 20 && alpha < 235)
                {
                    hasPartial = true;
                    break;
                }
            }
        }

        Assert.True(hasPartial, "圆形边缘没有出现半覆盖像素，抗锯齿可能失效");
    }

    [Fact]
    public void EmptyPathPaintsNothing()
    {
        using Canvas canvas = Canvas.Create(8, 8);
        var context = new DrawingContext(canvas);

        context.Fill(Color.Red, new PathBuilder().Build());

        Assert.Equal(0, canvas.GetColor(4, 4).A);
    }

    [Fact]
    public void TransparentColorDoesNotChangeCanvas()
    {
        using Canvas canvas = Canvas.Create(8, 8);
        var context = new DrawingContext(canvas);

        context.Fill(Color.Transparent, new RectangularPolygon(2, 2, 4, 4));

        Assert.Equal(0, canvas.GetColor(3, 3).A);
    }

    [Fact]
    public void ShapesAreClippedToCanvas()
    {
        using Canvas canvas = Canvas.Create(10, 10);
        var context = new DrawingContext(canvas);

        context.Fill(Color.Green, new RectangularPolygon(-5, -5, 20, 20));

        Assert.Equal(Color.Green, canvas.GetColor(0, 0));
        Assert.Equal(Color.Green, canvas.GetColor(9, 9));
    }
}
