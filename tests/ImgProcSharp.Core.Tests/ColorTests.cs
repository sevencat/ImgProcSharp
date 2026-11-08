using Xunit;

namespace ImgProcSharp.Tests;

public class ColorTests
{
    [Fact]
    public void DefaultIsTransparent()
    {
        Assert.Equal(new Color(0, 0, 0, 0), default(Color));
        Assert.Equal(Color.Transparent, default(Color));
    }

    [Fact]
    public void FromRgbIsOpaque()
    {
        Assert.Equal(255, Color.FromRgb(1, 2, 3).A);
        Assert.Equal(Color.FromRgba(1, 2, 3, 255), Color.FromRgb(1, 2, 3));
    }

    [Fact]
    public void WithAlphaKeepsChannels()
    {
        Color c = Color.FromRgb(10, 20, 30).WithAlpha(64);
        Assert.Equal(new Color(10, 20, 30, 64), c);
    }

    [Fact]
    public void BlendTransparentSourceKeepsDestination()
    {
        Color dst = Color.FromRgba(10, 20, 30, 100);
        Assert.Equal(dst, dst.Blend(Color.Transparent));
    }

    [Fact]
    public void BlendOpaqueSourceWins()
    {
        Color dst = Color.FromRgba(10, 20, 30, 100);
        Color src = Color.FromRgb(200, 100, 50);
        Assert.Equal(src, dst.Blend(src));
    }

    [Fact]
    public void BlendHalfAlphaWhiteOverBlackIsMidGray()
    {
        Color result = Color.Black.Blend(new Color(255, 255, 255, 128));
        Assert.Equal(new Color(128, 128, 128, 255), result);
    }

    [Fact]
    public void BlendHalfAlphaBlackOverWhiteIsMidGray()
    {
        Color result = Color.White.Blend(new Color(0, 0, 0, 128));
        Assert.Equal(new Color(127, 127, 127, 255), result);
    }

    [Fact]
    public void BlendOverTransparentKeepsSourceAlpha()
    {
        Color result = Color.Transparent.Blend(new Color(255, 0, 0, 64));
        Assert.Equal(64, result.A);
        Assert.Equal(255, result.R);
    }

    [Theory]
    [InlineData(0u, 0)]
    [InlineData(1u, 0)]
    [InlineData(127u, 0)]
    [InlineData(128u, 1)]
    [InlineData(255u, 1)]
    [InlineData(65025u, 255)]
    public void Div255RoundsToNearest(uint value, int expected)
    {
        Assert.Equal(expected, PixelOps.Div255(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(17)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(200)]
    [InlineData(255)]
    public void PremultiplyUnpremultiplyRoundtripsWithinOne(byte alpha)
    {
        var straight = new byte[] { 200, 90, 40, alpha };
        Span<byte> premultiplied = stackalloc byte[4];
        PixelOps.Premultiply(straight, premultiplied);

        Span<byte> back = stackalloc byte[4];
        PixelOps.Unpremultiply(premultiplied, back);

        Assert.Equal(alpha, back[3]);
        if (alpha < 8)
        {
            Assert.Equal(0, back[0]);
            return;
        }

        // 预乘在 8 位下的量化步长约为 255 / alpha，低 alpha 必然粗糙（misc §F）
        int tolerance = (255 / alpha) + 1;
        Assert.InRange(Math.Abs(back[0] - straight[0]), 0, tolerance);
        Assert.InRange(Math.Abs(back[1] - straight[1]), 0, tolerance);
        Assert.InRange(Math.Abs(back[2] - straight[2]), 0, tolerance);
    }

    [Fact]
    public void PremultiplyKeepsAlphaUntouched()
    {
        var straight = new byte[] { 10, 20, 30, 77 };
        Span<byte> premultiplied = stackalloc byte[4];
        PixelOps.Premultiply(straight, premultiplied);
        Assert.Equal(77, premultiplied[3]);
        Assert.Equal(PixelOps.Mul255(10, 77), premultiplied[0]);
    }

    [Fact]
    public void PremultiplyInPlaceSkipsOpaquePixels()
    {
        var pixels = new byte[] { 10, 20, 30, 255, 10, 20, 30, 0 };
        PixelOps.PremultiplyInPlace(pixels);
        Assert.Equal(new byte[] { 10, 20, 30, 255, 0, 0, 0, 0 }, pixels);
    }

    [Fact]
    public void PremultiplyRejectsBadLength()
    {
        Assert.Throws<ArgumentException>(() => PixelOps.Premultiply(new byte[3], new byte[3]));
        Assert.Throws<ArgumentException>(() => PixelOps.Premultiply(new byte[4], new byte[8]));
    }
}
