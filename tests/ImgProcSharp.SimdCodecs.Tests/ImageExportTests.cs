using Sdcb.SimdCodecs.Abstractions;
using Sdcb.SimdCodecs.Png;
using Xunit;

namespace ImgProcSharp.SimdCodecs.Tests;


public sealed class ImageExportTests
{
    [Fact]
    public void EncodePngMatchesStraightConversion()
    {
        using Canvas source = SemitransparentCanvas();

        // 期望值：调用方自己摊成直通像素再交给 PngWriter。
        byte[] straight = new byte[source.Width * source.Height * 4];
        source.CopyToStraight(straight, source.Width * 4, assumeOpaque: false);
        byte[] expected = PngWriter.Encode(straight, source.Width, source.Height);

        // 实际：把预乘缓冲直接交给注册的编码器，转换在编码器内部完成。
        byte[] actual = source.Encode(ImageFormat.Png);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EncodedPngKeepsPixels()
    {
        using Canvas source = SemitransparentCanvas();
        using Image image = Image.Load(source.Encode(ImageFormat.Png));

        Assert.Equal(ImageFormat.Png, image.SourceFormat);

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
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
    public void EncodeJpegDecodesBack()
    {
        using Canvas source = Canvas.Create(8, 5, Color.FromRgb(100, 149, 237));
        source.SetColor(0, 0, Color.Red);

        using Image image = Image.Load(source.Encode(ImageFormat.Jpeg));

        Assert.Equal(ImageFormat.Jpeg, image.SourceFormat);
        Assert.Equal(8, image.Width);
        Assert.Equal(5, image.Height);
    }

    [Fact]
    public void SaveInfersFormatFromExtension()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"imgprocsharp-{Guid.NewGuid():N}.png");

        try
        {
            using Canvas source = Canvas.Create(4, 3, Color.FromRgb(0, 255, 0));
            source.Save(path);

            Assert.True(File.Exists(path));

            using Image image = Image.Load(path);
            Assert.Equal(ImageFormat.Png, image.SourceFormat);
            Assert.Equal(4, image.Width);
            Assert.Equal(3, image.Height);
            Assert.Equal(Color.FromRgb(0, 255, 0), image.Canvas.GetColor(2, 1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveUsesExplicitFormatForUnknownExtension()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"imgprocsharp-{Guid.NewGuid():N}.imgdata");

        try
        {
            using Canvas source = Canvas.Create(2, 2, Color.Blue);
            source.Save(path, ImageFormat.Png);

            using Image image = Image.Load(path);
            Assert.Equal(ImageFormat.Png, image.SourceFormat);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveRejectsUnknownExtension()
    {
        using Canvas source = Canvas.Create(2, 2, Color.Blue);

        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => source.Save("out.unknown"));

        Assert.Contains("ImageFormat.Png", error.Message);
    }

    [Fact]
    public void EncodeWithoutEncoderExplainsHowToRegister()
    {
        using Canvas source = Canvas.Create(2, 2, Color.Blue);

        // WebP 编码器没注册（测试只登记 PNG / JPEG）。
        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => source.Encode(ImageFormat.Webp));

        Assert.Contains("EncoderRegistry.Register", error.Message);
        Assert.Contains("Webp", error.Message);
    }

    [Fact]
    public void StridedCanvasEncodesRows()
    {
        // 4 像素宽、stride 多留 8 字节 padding：编码器必须按 stride 跳行。
        const int width = 4;
        const int height = 3;
        const int stride = width * Canvas.BytesPerPixel + 8;

        byte[] buffer = new byte[stride * height];
        using Canvas source = Canvas.Wrap(buffer, width, height, stride);
        source.SetColor(1, 2, Color.Red);

        using Image image = Image.Load(source.Encode(ImageFormat.Png));

        Assert.Equal(Color.Red, image.Canvas.GetColor(1, 2));
    }

    [Fact]
    public void ImageAndCanvasEncodeAlikeWhenNoMetadata()
    {
        using Image image = Image.Create(3, 2, Color.FromRgb(255, 165, 0));

        Assert.Equal(image.Canvas.Encode(ImageFormat.Png), image.Encode(ImageFormat.Png));
    }

    private static Canvas SemitransparentCanvas()
    {
        Canvas canvas = Canvas.Create(3, 2);
        canvas.SetColor(0, 0, Color.FromRgba(10, 20, 30, 255));
        canvas.SetColor(1, 0, Color.FromRgba(200, 100, 50, 128));
        canvas.SetColor(2, 0, Color.FromRgba(0, 255, 0, 0));
        canvas.SetColor(0, 1, Color.FromRgba(255, 255, 255, 64));
        canvas.SetColor(1, 1, Color.Red);
        canvas.SetColor(2, 1, Color.Blue);
        return canvas;
    }
}
