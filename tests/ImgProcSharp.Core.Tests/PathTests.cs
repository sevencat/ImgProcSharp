using System.Numerics;
using Xunit;

namespace ImgProcSharp.Tests;

public class PathTests
{
    [Fact]
    public void RectangularPolygonHasExactBounds()
    {
        var rect = new RectangularPolygon(10, 20, 100, 50);
        Assert.Equal(new RectF(10, 20, 100, 50), rect.Bounds);
    }

    [Fact]
    public void PolygonBoundsCoverAllPoints()
    {
        var polygon = new Polygon(
            new PointF(0, 0),
            new PointF(30, 10),
            new PointF(20, 40),
            new PointF(-5, 15));

        Assert.Equal(new RectF(-5, 0, 35, 40), polygon.Bounds);
    }

    [Fact]
    public void EllipseBoundsMatchTheBox()
    {
        var ellipse = new EllipsePolygon(50, 50, 100, 60);
        RectF bounds = ellipse.Bounds;

        Assert.InRange(bounds.X, -0.5f, 0.5f);
        Assert.InRange(bounds.Y, 19.5f, 20.5f);
        Assert.InRange(bounds.Width, 99.5f, 100.5f);
        Assert.InRange(bounds.Height, 59.5f, 60.5f);
    }

    [Fact]
    public void CircleUsesDiameter()
    {
        var circle = new CirclePolygon(new PointF(10, 10), 5);
        Assert.InRange(circle.Bounds.Width, 9.5f, 10.5f);
        Assert.InRange(circle.Bounds.Height, 9.5f, 10.5f);
    }

    [Fact]
    public void LinesFlattenToCornerPoints()
    {
        var path = (Path)new PathBuilder()
            .MoveTo(0, 0)
            .LineTo(10, 0)
            .LineTo(10, 10)
            .CloseFigure()
            .Build();

        FlattenedPath flattened = path.Flattened;

        Assert.Equal(2, flattened.ContourStarts.Length);
        Assert.Equal(4, flattened.Points.Length);
        Assert.Equal(new PointF(0, 0), flattened.Points[0]);
        Assert.Equal(new PointF(10, 10), flattened.Points[2]);
        Assert.Equal(new PointF(0, 0), flattened.Points[3]);
    }

    [Fact]
    public void OpenSubPathIsKeptForImplicitClosing()
    {
        var path = (Path)new PathBuilder()
            .MoveTo(0, 0)
            .LineTo(10, 0)
            .LineTo(10, 10)
            .Build();

        FlattenedPath flattened = path.Flattened;

        Assert.Equal(3, flattened.Points.Length);
        Assert.Equal(3, flattened.ContourStarts[^1] - flattened.ContourStarts[0]);
    }

    [Fact]
    public void CubicIsSubdividedUntilWithinTolerance()
    {
        var path = (Path)new PathBuilder()
            .MoveTo(0, 0)
            .CubicTo(0, 100, 100, 100, 100, 0)
            .Build();

        FlattenedPath flattened = path.Flattened;

        Assert.True(flattened.Points.Length > 8, $"只展平出 {flattened.Points.Length} 个点");

        // 曲线最高点在 y = 75；每个折线点到曲线的距离允许 1 像素误差（采样近似）
        Assert.InRange(flattened.Bounds.Height, 74f, 76f);
        foreach (PointF point in flattened.Points)
        {
            Assert.InRange(point.Y, -0.5f, 76f);
        }
    }

    [Fact]
    public void QuadraticMatchesEquivalentCubic()
    {
        Path quadratic = new PathBuilder().MoveTo(0, 0).QuadraticTo(5, 10, 10, 0).Build();
        Path cubic = new PathBuilder().MoveTo(0, 0).CubicTo(10f / 3f, 20f / 3f, 25f / 3f, 20f / 3f, 10, 0).Build();

        RectF a = quadratic.Bounds;
        RectF b = cubic.Bounds;

        Assert.InRange(MathF.Abs(a.Height - b.Height), 0f, 0.5f);
        Assert.InRange(MathF.Abs(a.Width - b.Width), 0f, 0.5f);
    }

    [Fact]
    public void MultipleMoveToCreateMultipleContours()
    {
        var path = (Path)new PathBuilder()
            .MoveTo(0, 0).LineTo(10, 0).CloseFigure()
            .MoveTo(20, 20).LineTo(30, 20).CloseFigure()
            .Build();

        FlattenedPath flattened = path.Flattened;

        Assert.Equal(3, flattened.ContourStarts.Length);
        Assert.True(flattened.ContourStarts[1] - flattened.ContourStarts[0] >= 3);
        Assert.True(flattened.ContourStarts[2] - flattened.ContourStarts[1] >= 3);
    }

    [Fact]
    public void TransformScalesBounds()
    {
        IPath path = new RectangularPolygon(0, 0, 10, 20).Transform(Matrix3x2.CreateScale(2f));
        Assert.Equal(new RectF(0, 0, 20, 40), path.Bounds);
    }

    [Fact]
    public void TransformRotatesPoints()
    {
        IPath path = new PathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 20).Build()
            .Transform(Matrix3x2.CreateRotation(MathF.PI / 2f));

        RectF bounds = path.Bounds;
        Assert.InRange(bounds.X, -20.5f, -19.5f);
        Assert.InRange(bounds.Width, 19.5f, 20.5f);
        Assert.InRange(bounds.Height, 9.5f, 10.5f);
    }

    [Fact]
    public void EmptyBuilderProducesEmptyPath()
    {
        Path path = new PathBuilder().Build();
        Assert.True(path.Bounds.IsEmpty);
        Assert.Empty(path.Flattened.Points);
    }

    [Fact]
    public void CustomIPathIsRejectedByFlattener()
    {
        Assert.Throws<ArgumentException>(() => Path.Flatten(new CustomPath()));
    }

    private sealed class CustomPath : IPath
    {
        public RectF Bounds => RectF.Empty;

        public IPath Transform(Matrix3x2 transform) => this;
    }
}
