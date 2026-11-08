using System.Numerics;
using Sdcb.SimdCodecs.Abstractions;
using Sdcb.SimdCodecs.Png;
using Xunit;

namespace ImgProcSharp.SimdCodecs.Tests;


public sealed class ImageTests
{
    [Fact]
    public void LoadPngPreservesPixels()
    {
        using Canvas source = Canvas.Create(3, 2);
        source.SetColor(0, 0, Color.FromRgba(10, 20, 30, 255));
        source.SetColor(1, 0, Color.FromRgba(200, 100, 50, 128));
        source.SetColor(2, 0, Color.FromRgba(0, 255, 0, 0));
        source.SetColor(0, 1, Color.FromRgba(255, 255, 255, 64));
        source.SetColor(1, 1, Color.Red);
        source.SetColor(2, 1, Color.Blue);

        using Image image = Image.Load(EncodePng(source));

        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(ImageFormat.Png, image.SourceFormat);

        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Color expected = source.GetColor(x, y);
                Color actual = image.Canvas.GetColor(x, y);

                // 直通 → 预乘 → 直通，半透明像素允许 ±1 的量化误差
                Assert.InRange(Math.Abs(expected.R - actual.R), 0, 1);
                Assert.InRange(Math.Abs(expected.G - actual.G), 0, 1);
                Assert.InRange(Math.Abs(expected.B - actual.B), 0, 1);
                Assert.Equal(expected.A, actual.A);
            }
        }
    }

    [Fact]
    public void LoadRejectsNonImageData()
    {
        Assert.Throws<InvalidDataException>(() => Image.Load(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    [Fact]
    public void LoadFromStreamHonoursLeaveOpen()
    {
        byte[] png;

        using (Canvas canvas = Canvas.Create(2, 2, Color.Red))
        {
            png = EncodePng(canvas);
        }

        var keeping = new MemoryStream(png);
        using (Image image = Image.Load(keeping, leaveOpen: true))
        {
            Assert.Equal(Color.Red, image.Canvas.GetColor(1, 1));
        }

        Assert.True(keeping.CanRead);

        var closing = new MemoryStream(png);
        using (Image image = Image.Load(closing))
        {
            Assert.Equal(2, image.Width);
        }

        Assert.False(closing.CanRead);
    }

    [Fact]
    public void CreateFillsBackground()
    {
        using Image image = Image.Create(4, 3, Color.FromRgba(0, 0, 255, 128));

        Assert.Equal(4, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(ImageFormat.Unknown, image.SourceFormat);
        Assert.Equal(Color.FromRgba(0, 0, 255, 128), image.Canvas.GetColor(3, 2));
    }

    [Fact]
    public void MutateChainsAndReturnsSelf()
    {
        using Image image = Image.Create(16, 16, Color.White);
        var path = new RectangularPolygon(0, 0, 8, 16);

        Image returned = image.Mutate(x => x.Fill(Color.Red, path).Fill(Color.Blue, new RectangularPolygon(0, 0, 4, 4)));

        Assert.Same(image, returned);
        Assert.Equal(Color.Blue, image.Canvas.GetColor(1, 1));
        Assert.Equal(Color.Red, image.Canvas.GetColor(6, 1));
        Assert.Equal(Color.White, image.Canvas.GetColor(12, 1));
    }

    [Fact]
    public void MutateDrawsWithPenAndTransform()
    {
        using Image image = Image.Create(100, 100, Color.White);
        IPath line = new PathBuilder().MoveTo(-30, 0).LineTo(30, 0).Build();

        image.Mutate(x => x
            .PushTransform(Matrix3x2.CreateTranslation(50, 50))
            .Draw(new Pen(Color.Red, 10f), line));

        Assert.Equal(Color.Red, image.Canvas.GetColor(50, 50));
        Assert.Equal(Color.Red, image.Canvas.GetColor(78, 50));
        Assert.Equal(Color.White, image.Canvas.GetColor(50, 30));
    }

    [Fact]
    public void MutateClipRestrictsDrawing()
    {
        using Image image = Image.Create(100, 100, Color.White);

        image.Mutate(x => x
            .PushClip(new RectangularPolygon(0, 0, 50, 100))
            .Fill(Color.Red, new RectangularPolygon(0, 0, 100, 100))
            .PopClip());

        Assert.Equal(Color.Red, image.Canvas.GetColor(10, 50));
        Assert.Equal(Color.White, image.Canvas.GetColor(80, 50));
    }

    [Fact]
    public void ResizeKeepsTransform()
    {
        using Image image = Image.Create(100, 100, Color.White);
        Matrix3x2 transform = Matrix3x2.CreateTranslation(10, 0);

        image.Mutate(x =>
        {
            x.PushTransform(transform);
            x.Resize(40, 40);

            Assert.Equal(transform, x.Transform);
        });
    }

    [Fact]
    public void ResizeDropsClip()
    {
        using Image image = Image.Create(100, 100, Color.White);

        // 裁剪掩码与画布尺寸绑定，Resize 后随旧上下文一起丢弃，深度归零
        Assert.Throws<InvalidOperationException>(() => image.Mutate(x =>
        {
            x.PushClip(new RectangularPolygon(0, 0, 50, 100));
            x.Resize(40, 40);
            x.PopClip();
        }));
    }

    [Fact]
    public void MutateResizeReplacesCanvas()
    {
        using Image image = Image.Create(20, 10, Color.White);

        Image result = image.Mutate(x => x.Resize(5, 30));

        Assert.Same(image, result);
        Assert.Equal(5, image.Width);
        Assert.Equal(30, image.Height);
        Assert.Equal(new Size(5, 30), new Size(image.Width, image.Height));
        Assert.Equal(Color.White, image.Canvas.GetColor(2, 15));
    }

    [Fact]
    public void CloneDoesNotSharePixels()
    {
        using Image image = Image.Create(8, 8, Color.White);
        using Image clone = image.Clone();

        clone.Canvas.SetColor(0, 0, Color.Black);

        Assert.Equal(Color.White, image.Canvas.GetColor(0, 0));
        Assert.Equal(Color.Black, clone.Canvas.GetColor(0, 0));
    }

    [Fact]
    public void ApplyOrientationRotate90IsClockwise()
    {
        using Canvas canvas = Canvas.Create(2, 3);
        canvas.SetColor(0, 0, Color.Red);
        canvas.SetColor(1, 0, Color.Green);
        canvas.SetColor(0, 2, Color.Blue);

        using var image = new Image(canvas, ImageFormat.Jpeg, ReadOnlyMemory<byte>.Empty, ImageOrientation.Rotate90);
        image.ApplyOrientation();

        // dst(dx, dy) = src(dy, H - 1 - dx)，H = 3：左上角转到右上角，左下角转到左上角
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(Color.Red, image.Canvas.GetColor(2, 0));
        Assert.Equal(Color.Green, image.Canvas.GetColor(2, 1));
        Assert.Equal(Color.Blue, image.Canvas.GetColor(0, 0));
    }

    [Fact]
    public void ApplyOrientationTransposeMirrorsMainDiagonal()
    {
        using Canvas canvas = Canvas.Create(3, 2);
        canvas.SetColor(0, 0, Color.Red);
        canvas.SetColor(2, 1, Color.Blue);

        using var image = new Image(canvas, ImageFormat.Tiff, ReadOnlyMemory<byte>.Empty, ImageOrientation.Transpose);
        image.ApplyOrientation();

        Assert.Equal(2, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(Color.Red, image.Canvas.GetColor(0, 0));
        Assert.Equal(Color.Blue, image.Canvas.GetColor(1, 2));
    }

    [Fact]
    public void ApplyOrientationIsIdempotent()
    {
        using Canvas canvas = Canvas.Create(2, 3);
        canvas.SetColor(0, 0, Color.Red);

        using var image = new Image(canvas, ImageFormat.Jpeg, ReadOnlyMemory<byte>.Empty, ImageOrientation.Rotate90);
        image.ApplyOrientation();
        image.ApplyOrientation();

        Assert.Equal(ImageOrientation.Normal, image.Orientation);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(Color.Red, image.Canvas.GetColor(2, 0));
    }

    [Fact]
    public void ApplyOrientationNormalIsNoOp()
    {
        using Canvas canvas = Canvas.Create(2, 2, Color.Red);
        using var image = new Image(canvas, ImageFormat.Png, ReadOnlyMemory<byte>.Empty, ImageOrientation.Normal);

        image.ApplyOrientation();

        Assert.Equal(2, image.Width);
        Assert.Equal(Color.Red, image.Canvas.GetColor(1, 1));
    }

    [Fact]
    public void MutateDrawsGradientAndText()
    {
        using Image image = Image.Create(80, 40, Color.White);

        image.Mutate(x => x
            .Fill(
                new LinearGradientBrush(
                    new PointF(0, 0),
                    new PointF(80, 0),
                    new ColorStop(0f, Color.Red),
                    new ColorStop(1f, Color.Blue)),
                new RectangularPolygon(0, 0, 80, 40))
            .DrawText(new TextOptions { Size = 16f }, "Hi", new PointF(4, 4)));

        Color left = image.Canvas.GetColor(1, 20);
        Color right = image.Canvas.GetColor(78, 20);

        Assert.True(left.R > 240 && left.B < 16, $"左端应接近红：{left}");
        Assert.True(right.B > 240 && right.R < 16, $"右端应接近蓝：{right}");

        bool inked = false;
        for (int y = 0; y < 40 && !inked; y++)
        {
            for (int x = 0; x < 80; x++)
            {
                if (image.Canvas.GetColor(x, y).R > 200)
                {
                    inked = true;
                    break;
                }
            }
        }

        Assert.True(inked, "DrawText 没有在渐变上留下墨迹");
    }

    private static byte[] EncodePng(Canvas canvas)
    {
        byte[] straight = new byte[canvas.Width * canvas.Height * Canvas.BytesPerPixel];
        canvas.CopyToStraight(straight, canvas.Width * Canvas.BytesPerPixel);
        return PngWriter.Encode(straight, canvas.Width, canvas.Height);
    }
}
