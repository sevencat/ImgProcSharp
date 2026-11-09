using System.Numerics;
using Sdcb.SimdCodecs;
using Sdcb.SimdCodecs.Abstractions;
using Sdcb.SimdCodecs.Jpeg;
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
    public void EncodeFromPremultipliedCanvasKeepsPixels()
    {
        using Canvas source = Canvas.Create(3, 2);
        source.SetColor(0, 0, Color.FromRgba(10, 20, 30, 255));
        source.SetColor(1, 0, Color.FromRgba(200, 100, 50, 128));
        source.SetColor(2, 0, Color.FromRgba(0, 255, 0, 0));
        source.SetColor(0, 1, Color.FromRgba(255, 255, 255, 64));
        source.SetColor(1, 1, Color.Red);
        source.SetColor(2, 1, Color.Blue);

        // 带透明画布的推荐保存方式：预乘缓冲直接喂编码门面，不摊一张直通数组
        byte[] png = ImageCodecs.Encode(
            source.Buffer,
            source.Width,
            source.Height,
            PixelFormat.Rgba32Premultiplied,
            ImageFormat.Png,
            stride: source.Stride,
            cancellationToken: TestContext.Current.CancellationToken);

        using Image image = Image.Load(png);

        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Color expected = source.GetColor(x, y);
                Color actual = image.Canvas.GetColor(x, y);

                Assert.InRange(Math.Abs(expected.R - actual.R), 0, 1);
                Assert.InRange(Math.Abs(expected.G - actual.G), 0, 1);
                Assert.InRange(Math.Abs(expected.B - actual.B), 0, 1);
                Assert.Equal(expected.A, actual.A);
            }
        }
    }

    [Fact]
    public void OpaqueCanvasEncodesWithoutStraightCopy()
    {
        using Canvas source = Canvas.Create(4, 2, Color.Red);
        source.SetColor(1, 0, Color.Blue);

        // 不透明画布的推荐保存方式：预乘缓冲即直通缓冲，直接用 + DiscardAlpha 丢掉 alpha 通道
        byte[] png = PngWriter.Encode(
            source.Buffer,
            source.Width,
            source.Height,
            new PngWriteOptions { DiscardAlpha = true });

        using Image image = Image.Load(png);

        Assert.True(image.Canvas.IsFullyOpaque());
        Assert.Equal(source.GetColor(1, 0), image.Canvas.GetColor(1, 0));
        Assert.Equal(source.GetColor(3, 1), image.Canvas.GetColor(3, 1));
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

    [Fact]
    public void DirectDecodeMatchesRasterDecode()
    {
        using Canvas source = Canvas.Create(7, 5);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                source.SetColor(x, y, Color.FromRgba((byte)(x * 31), (byte)(y * 47), (byte)(x * y * 7), (byte)((x + y) * 23)));
            }
        }

        byte[] png = EncodePng(source);

        using Image direct = Image.Load(png);
        using Canvas raster = ImageDecoderBridge.DecodeThroughRaster(png);

        Assert.Equal(raster.Width, direct.Width);
        Assert.Equal(raster.Height, direct.Height);
        Assert.Equal(raster.Buffer, direct.Canvas.Buffer);
        Assert.True(direct.IccProfile.IsEmpty);
    }

    [Fact]
    public void DirectDecodeMatchesRasterDecodeFor16BitPng()
    {
        ushort[] samples = new ushort[4 * 3 * 4];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (ushort)(i * 4099 % 65536);
        }

        byte[] png = PngWriter.EncodeRgba16(samples, 4, 3, new PngWriteOptions());

        using Image direct = Image.Load(png);
        using Canvas raster = ImageDecoderBridge.DecodeThroughRaster(png);

        Assert.Equal(raster.Buffer, direct.Canvas.Buffer);
    }

    [Fact]
    public void LoadFromFileMatchesLoadFromMemory()
    {
        using Canvas source = Canvas.Create(5, 4);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                source.SetColor(x, y, Color.FromRgba((byte)(x * 51), (byte)(y * 63), 7, (byte)(255 - (x * 17))));
            }
        }

        byte[] icc = [1, 2, 3, 4, 5];
        byte[] png = PngWriter.Encode(CopyToStraight(source), 5, 4, new PngWriteOptions { IccProfile = icc });

        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"imgproc-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, png);

        try
        {
            using Image fromFile = Image.Load(path);
            using Image fromMemory = Image.Load(png);

            // 文件路径走 ImageDecoder.Open 的内存映射，结果必须和读进 byte[] 完全一致。
            Assert.Equal(fromMemory.Canvas.Buffer, fromFile.Canvas.Buffer);
            Assert.Equal(ImageFormat.Png, fromFile.SourceFormat);
            Assert.Equal(icc, fromFile.IccProfile.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PngIccComesFromHeader()
    {
        byte[] straight = new byte[2 * 2 * 4];
        byte[] icc = [1, 2, 3, 4, 5];
        byte[] png = PngWriter.Encode(straight, 2, 2, new PngWriteOptions { IccProfile = icc });

        using Image image = Image.Load(png);

        // 2.0 的 TryReadMetadata 只扫 iCCP 段，PNG 的 ICC 不再需要完整解码。
        Assert.Equal(icc, image.IccProfile.ToArray());
    }

    [Fact]
    public void PngIccSurvivesAlphaPremultiply()
    {
        using Canvas source = Canvas.Create(2, 2);
        source.SetColor(0, 0, Color.FromRgba(200, 100, 50, 128));
        byte[] icc = [9, 8, 7];
        byte[] png = PngWriter.Encode(CopyToStraight(source), 2, 2, new PngWriteOptions { IccProfile = icc });

        using Image image = Image.Load(png);

        Assert.Equal(icc, image.IccProfile.ToArray());

        // 带 alpha 的图仍是「直写 + 本地预乘」，ICC 不该影响像素。
        Color expected = source.GetColor(0, 0);
        Color actual = image.Canvas.GetColor(0, 0);

        Assert.Equal(expected.A, actual.A);
        Assert.InRange(Math.Abs(expected.R - actual.R), 0, 1);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, 1);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, 1);
    }

    [Fact]
    public void DefaultDecodeTakesIccFromJpegHeader()
    {
        byte[] straight = new byte[16 * 16 * 3];
        byte[] icc = [1, 2, 3, 4, 5];
        byte[] jpeg = JpegEncoder.Encode(straight, 16, 16, 3, new JpegEncodeOptions { IccProfile = icc });

        using Image image = Image.Load(jpeg);

        Assert.Equal(icc, image.IccProfile.ToArray());
    }

    [Fact]
    public void OpaqueJpegMatchesRasterDecode()
    {
        byte[] rgb = new byte[16 * 16 * 3];
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (byte)(i * 37);
        }

        byte[] jpeg = JpegEncoder.Encode(rgb, 16, 16, 3, new JpegEncodeOptions());

        using Image direct = Image.Load(jpeg);
        using Canvas raster = ImageDecoderBridge.DecodeThroughRaster(jpeg);

        Assert.Equal(raster.Buffer, direct.Canvas.Buffer);
        Assert.True(direct.Canvas.IsFullyOpaque());
    }

    [Fact]
    public void OpaqueRgbPngMatchesRasterDecode()
    {
        byte[] rgb = new byte[16 * 16 * 3];
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (byte)(i * 37);
        }

        byte[] png = PngWriter.EncodeRgb8(rgb, 16, 16, new PngWriteOptions());

        using Image direct = Image.Load(png);
        using Canvas raster = ImageDecoderBridge.DecodeThroughRaster(png);

        Assert.Equal(raster.Buffer, direct.Canvas.Buffer);
        Assert.True(direct.Canvas.IsFullyOpaque());
    }

    private static byte[] EncodePng(Canvas canvas) =>
        PngWriter.Encode(CopyToStraight(canvas), canvas.Width, canvas.Height);

    private static byte[] CopyToStraight(Canvas canvas)
    {
        int stride = canvas.Width * Canvas.BytesPerPixel;
        byte[] straight = new byte[stride * canvas.Height];
        canvas.CopyToStraight(straight, stride);
        return straight;
    }
}
