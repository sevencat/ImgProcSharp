using Xunit;

namespace ImgProcSharp.Tests;

public class ResizeTests
{
    public static TheoryData<string> AllSamplers => new()
    {
        nameof(KnownResamplers.NearestNeighbour),
        nameof(KnownResamplers.Bilinear),
        nameof(KnownResamplers.Bicubic),
        nameof(KnownResamplers.Lanczos3),
    };

    private static IResampler Sampler(string name) => name switch
    {
        nameof(KnownResamplers.NearestNeighbour) => KnownResamplers.NearestNeighbour,
        nameof(KnownResamplers.Bilinear) => KnownResamplers.Bilinear,
        nameof(KnownResamplers.Bicubic) => KnownResamplers.Bicubic,
        _ => KnownResamplers.Lanczos3,
    };

    [Theory]
    [InlineData(2, 1)]
    [InlineData(8, 4)]
    [InlineData(1, 7)]
    public void StretchProducesRequestedSize(int width, int height)
    {
        using Canvas source = Canvas.Create(4, 2, Color.Red);
        using Canvas resized = source.Resize(width, height);

        Assert.Equal(width, resized.Width);
        Assert.Equal(height, resized.Height);
        Assert.Equal(255, resized.GetColor(width - 1, height - 1).A);
    }

    [Fact]
    public void SameSizeIsLossless()
    {
        using Canvas source = Canvas.Create(3, 2);
        source.SetColor(0, 0, new Color(10, 20, 30, 200));
        source.SetColor(2, 1, new Color(200, 100, 50, 128));

        using Canvas resized = source.Resize(3, 2);

        Assert.Equal(source.Buffer, resized.Buffer);
    }

    [Theory]
    [MemberData(nameof(AllSamplers))]
    public void SolidColorSurvivesResampling(string sampler)
    {
        using Canvas source = Canvas.Create(16, 9, new Color(30, 120, 200, 255));
        using Canvas resized = source.Resize(7, 5, Sampler(sampler));

        for (int y = 0; y < resized.Height; y++)
        {
            for (int x = 0; x < resized.Width; x++)
            {
                Color c = resized.GetColor(x, y);
                Assert.Equal(255, c.A);
                Assert.InRange(Math.Abs(c.R - 30), 0, 1);
                Assert.InRange(Math.Abs(c.G - 120), 0, 1);
                Assert.InRange(Math.Abs(c.B - 200), 0, 1);
            }
        }
    }

    [Fact]
    public void NearestNeighbourPicksWholeSourcePixels()
    {
        using Canvas source = Canvas.Create(2, 1);
        source.SetColor(0, 0, Color.Black);
        source.SetColor(1, 0, Color.White);

        using Canvas resized = source.Resize(4, 1, KnownResamplers.NearestNeighbour);

        Assert.Equal(Color.Black, resized.GetColor(0, 0));
        Assert.Equal(Color.Black, resized.GetColor(1, 0));
        Assert.Equal(Color.White, resized.GetColor(2, 0));
        Assert.Equal(Color.White, resized.GetColor(3, 0));
    }

    [Theory]
    [MemberData(nameof(AllSamplers))]
    public void TransparentAreaDoesNotBleedColour(string sampler)
    {
        using Canvas source = Canvas.Create(8, 8);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                source.SetColor(x, y, new Color(255, 0, 0, 255));
            }
        }

        using Canvas resized = source.Resize(4, 4, Sampler(sampler));

        // 下半部分原本全透明，缩放后必须是「预乘为 0」，不能出现红色残影
        for (int y = 2; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                ReadOnlySpan<byte> pixel = resized.GetRow(y).Slice(x * 4, 4);
                if (pixel[3] == 0)
                {
                    Assert.Equal(0, pixel[0]);
                    Assert.Equal(0, pixel[1]);
                    Assert.Equal(0, pixel[2]);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllSamplers))]
    public void OutputStaysValidPremultiplied(string sampler)
    {
        var random = new Random(7 + sampler.Length);
        using Canvas source = Canvas.Create(13, 9);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                source.SetColor(x, y, new Color(
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    (byte)random.Next(256)));
            }
        }

        using Canvas resized = source.Resize(6, 11, Sampler(sampler));
        for (int y = 0; y < resized.Height; y++)
        {
            ReadOnlySpan<byte> row = resized.GetRow(y);
            for (int i = 0; i < row.Length; i += 4)
            {
                byte a = row[i + 3];
                Assert.True(row[i] <= a, $"R {row[i]} > A {a}");
                Assert.True(row[i + 1] <= a, $"G {row[i + 1]} > A {a}");
                Assert.True(row[i + 2] <= a, $"B {row[i + 2]} > A {a}");
            }
        }
    }

    [Fact]
    public void MaxModeFitsInsideBoxKeepingAspect()
    {
        using Canvas source = Canvas.Create(100, 50, Color.Red);
        using Canvas resized = source.Resize(new ResizeOptions
        {
            Size = new Size(40, 40),
            Mode = ResizeMode.Max,
        });

        Assert.Equal(40, resized.Width);
        Assert.Equal(20, resized.Height);
    }

    [Fact]
    public void MinModeCoversBoxKeepingAspect()
    {
        using Canvas source = Canvas.Create(100, 50, Color.Red);
        using Canvas resized = source.Resize(new ResizeOptions
        {
            Size = new Size(40, 40),
            Mode = ResizeMode.Min,
        });

        Assert.Equal(80, resized.Width);
        Assert.Equal(40, resized.Height);
    }

    [Fact]
    public void CropModeCropsCentre()
    {
        using Canvas source = Canvas.Create(100, 50, Color.Blue);
        for (int y = 20; y < 30; y++)
        {
            for (int x = 45; x < 55; x++)
            {
                source.SetColor(x, y, Color.Red);
            }
        }

        using Canvas resized = source.Resize(new ResizeOptions
        {
            Size = new Size(40, 40),
            Mode = ResizeMode.Crop,
            Sampler = KnownResamplers.NearestNeighbour,
        });

        Assert.Equal(40, resized.Width);
        Assert.Equal(40, resized.Height);
        Assert.Equal(Color.Red, resized.GetColor(20, 20));
    }

    [Fact]
    public void PadModeFillsPadColour()
    {
        using Canvas source = Canvas.Create(100, 50, Color.Red);
        using Canvas resized = source.Resize(new ResizeOptions
        {
            Size = new Size(40, 40),
            Mode = ResizeMode.Pad,
            PadColor = Color.Blue,
            Sampler = KnownResamplers.NearestNeighbour,
        });

        Assert.Equal(40, resized.Width);
        Assert.Equal(40, resized.Height);
        Assert.Equal(Color.Blue, resized.GetColor(20, 0));
        Assert.Equal(Color.Red, resized.GetColor(20, 20));
        Assert.Equal(Color.Blue, resized.GetColor(20, 39));
    }

    [Fact]
    public void BoxPadNeverUpscales()
    {
        using Canvas source = Canvas.Create(20, 10, Color.Red);
        using Canvas resized = source.Resize(new ResizeOptions
        {
            Size = new Size(40, 40),
            Mode = ResizeMode.BoxPad,
            PadColor = Color.Blue,
            Sampler = KnownResamplers.NearestNeighbour,
        });

        Assert.Equal(40, resized.Width);
        Assert.Equal(40, resized.Height);
        Assert.Equal(Color.Blue, resized.GetColor(0, 0));

        // 20x10 居中：x ∈ [10, 30)、y ∈ [15, 25)
        Assert.Equal(Color.Red, resized.GetColor(10, 15));
        Assert.Equal(Color.Red, resized.GetColor(29, 24));
        Assert.Equal(Color.Blue, resized.GetColor(9, 15));
        Assert.Equal(Color.Blue, resized.GetColor(30, 24));
    }

    [Fact]
    public void CompandIsRejectedLoudly()
    {
        using Canvas source = Canvas.Create(4, 4);
        Assert.Throws<NotSupportedException>(() => source.Resize(new ResizeOptions
        {
            Size = new Size(2, 2),
            Compand = true,
        }));
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        using Canvas source = Canvas.Create(4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Resize(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Resize(new ResizeOptions { Size = new Size(-1, 4) }));
        Assert.Throws<ArgumentException>(() => source.Resize(new ResizeOptions
        {
            Size = new Size(2, 2),
            Sampler = null!,
        }));
    }

    [Fact]
    public void DisposedCanvasIsRejected()
    {
        Canvas source = Canvas.Create(4, 4);
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.Resize(2, 2));
    }

    [Fact]
    public void SourceIsNotModified()
    {
        using Canvas source = Canvas.Create(6, 6, Color.Red);
        using Canvas resized = source.Resize(3, 3);

        Assert.Equal(6, source.Width);
        Assert.Equal(6, source.Height);
        Assert.Equal(Color.Red, source.GetColor(5, 5));
        Assert.Equal(3, resized.Width);
    }
}
