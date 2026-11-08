using System.Numerics;
using Xunit;

namespace ImgProcSharp.Tests;


public class BrushTransformTests
{
    [Fact]
    public void LinearGradientFollowsScale()
    {
        using Canvas plain = Canvas.Create(400, 100, Color.White);
        new DrawingContext(plain).Fill(CreateLinear(200f), new RectangularPolygon(0, 0, 200, 100));

        using Canvas scaled = Canvas.Create(400, 100, Color.White);
        var scaledContext = new DrawingContext(scaled);
        scaledContext.PushTransform(Matrix3x2.CreateScale(2f, 1f));
        scaledContext.Fill(CreateLinear(200f), new RectangularPolygon(0, 0, 200, 100));

        // 几何放大 2 倍时渐变也放大 2 倍：画布 x=2u 应等于未缩放时 x=u 的颜色
        foreach (int x in new[] { 10, 50, 99, 150 })
        {
            AssertColorNear(plain.GetColor(x, 50), scaled.GetColor(x * 2, 50), 3);
        }

        // 若笔刷不跟随变换，画布 x=200 会是纯蓝（t=1）；跟随变换时 t=0.5，应当是紫色
        Color middle = scaled.GetColor(200, 50);
        Assert.InRange(middle.R, 118, 138);
        Assert.InRange(middle.B, 118, 138);
    }

    [Fact]
    public void LinearGradientFollowsRotation()
    {
        using Canvas canvas = Canvas.Create(200, 200, Color.White);
        var context = new DrawingContext(canvas);

        // 顺时针 90°：用户 (x, y) → 画布 (-y, x)，渐变参数 t 变成「画布 y / 200」
        context.PushTransform(Matrix3x2.CreateRotation(MathF.PI / 2f));
        context.Fill(CreateLinear(200f), new RectangularPolygon(0, -200, 200, 200));

        Color top = canvas.GetColor(10, 10);
        Color bottom = canvas.GetColor(10, 190);
        Assert.True(top.R > 200 && top.B < 60, $"顶部应当偏红：{top}");
        Assert.True(bottom.B > 200 && bottom.R < 60, $"底部应当偏蓝：{bottom}");
    }

    [Fact]
    public void RadialGradientFollowsTranslation()
    {
        using Canvas canvas = Canvas.Create(300, 300, Color.White);
        var context = new DrawingContext(canvas);
        var brush = new RadialGradientBrush(
            new PointF(0, 0),
            50f,
            new ColorStop(0f, Color.White),
            new ColorStop(1f, Color.Black));

        context.PushTransform(Matrix3x2.CreateTranslation(100, 100));
        context.Fill(brush, new RectangularPolygon(0, 0, 200, 200));

        // 圆心跟着平移到画布 (100,100)：若笔刷不跟随，这里会是半径 141 处的黑色
        // （像素中心离圆心 0.7 px，t≈0.014，所以留 6 的容差）
        AssertColorNear(Color.White, canvas.GetColor(100, 100), 6);

        Color nearCenter = canvas.GetColor(100, 140);   // 用户 (0,40) → t=0.8
        Assert.InRange(nearCenter.R, 30, 90);
        Assert.Equal(Color.Black, canvas.GetColor(200, 100));
    }

    [Fact]
    public void PatternBrushFollowsTranslation()
    {
        using Canvas tile = Canvas.Create(2, 2);
        tile.SetColor(0, 0, Color.Red);
        tile.SetColor(1, 0, Color.Blue);
        tile.SetColor(0, 1, Color.Blue);
        tile.SetColor(1, 1, Color.Red);
        var brush = new PatternBrush(tile, new PointF(0, 0));

        using Canvas canvas = Canvas.Create(32, 32, Color.White);
        var context = new DrawingContext(canvas);
        context.PushTransform(Matrix3x2.CreateTranslation(5, 5));
        context.Fill(brush, new RectangularPolygon(0, 0, 16, 16));

        // 平铺相位跟着平移：(5,5) 是瓦片 (0,0)，(6,5) 是瓦片 (1,0)
        AssertColorNear(Color.Red, canvas.GetColor(5, 5), 2);
        AssertColorNear(Color.Blue, canvas.GetColor(6, 5), 2);
        AssertColorNear(Color.Blue, canvas.GetColor(5, 6), 2);
        AssertColorNear(Color.Red, canvas.GetColor(7, 7), 2);
    }

    [Fact]
    public void BrushStaysUntransformedAfterUse()
    {
        var brush = CreateLinear(100f);

        using Canvas transformed = Canvas.Create(200, 100, Color.White);
        var transformedContext = new DrawingContext(transformed);
        transformedContext.PushTransform(Matrix3x2.CreateTranslation(100, 0));
        transformedContext.Fill(brush, new RectangularPolygon(-100, 0, 200, 100));

        // 同一个笔刷在恒等变换下必须还是原来的样子（WithInverseTransform 返回副本，不能改原对象）
        using Canvas plain = Canvas.Create(100, 100, Color.White);
        new DrawingContext(plain).Fill(brush, new RectangularPolygon(0, 0, 100, 100));

        Color middle = plain.GetColor(50, 50);
        Assert.InRange(middle.R, 118, 138);
        Assert.InRange(middle.B, 118, 138);
    }

    private static LinearGradientBrush CreateLinear(float length = 100f) => new(
        new PointF(0, 0),
        new PointF(length, 0),
        new ColorStop(0f, Color.Red),
        new ColorStop(1f, Color.Blue));

    private static void AssertColorNear(Color expected, Color actual, int tolerance)
    {
        Assert.InRange((int)actual.R, expected.R - tolerance, expected.R + tolerance);
        Assert.InRange((int)actual.G, expected.G - tolerance, expected.G + tolerance);
        Assert.InRange((int)actual.B, expected.B - tolerance, expected.B + tolerance);
        Assert.InRange((int)actual.A, expected.A - tolerance, expected.A + tolerance);
    }
}
