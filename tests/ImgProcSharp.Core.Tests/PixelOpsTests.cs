using Xunit;

namespace ImgProcSharp.Tests;

public class PixelOpsTests
{
    [Fact]
    public void ToBgra32SwapsRedAndBlue()
    {
        using Canvas canvas = Canvas.Create(2, 1);
        canvas.SetColor(0, 0, Color.Red);
        canvas.SetColor(1, 0, Color.Blue);

        byte[] bgra = PixelOps.ToBgra32(canvas);

        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, bgra);
    }

    [Fact]
    public void ToRgb24DropsAlpha()
    {
        using Canvas canvas = Canvas.Create(1, 1);
        canvas.SetColor(0, 0, new Color(10, 20, 30, 255));

        Assert.Equal(new byte[] { 10, 20, 30 }, PixelOps.ToRgb24(canvas));
    }

    [Fact]
    public void ToGray8UsesRec601()
    {
        using Canvas canvas = Canvas.Create(3, 1);
        canvas.SetColor(0, 0, Color.Red);
        canvas.SetColor(1, 0, Color.Green);
        canvas.SetColor(2, 0, Color.Blue);

        byte[] gray = PixelOps.ToGray8(canvas);

        Assert.True(gray[0] is >= 76 and <= 78, $"red luminance was {gray[0]}");
        Assert.True(gray[1] is >= 149 and <= 151, $"green luminance was {gray[1]}");
        Assert.True(gray[2] is >= 28 and <= 30, $"blue luminance was {gray[2]}");
    }

    [Fact]
    public void ExportsAreStraightNotPremultiplied()
    {
        using Canvas canvas = Canvas.Create(1, 1);
        canvas.SetColor(0, 0, new Color(200, 100, 50, 128));

        byte[] bgra = PixelOps.ToBgra32(canvas);
        Assert.InRange(Math.Abs(bgra[2] - 200), 0, 1);
        Assert.InRange(Math.Abs(bgra[1] - 100), 0, 1);
        Assert.InRange(Math.Abs(bgra[0] - 50), 0, 1);
        Assert.Equal(128, bgra[3]);
    }

    [Fact]
    public void ToBgra32RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => PixelOps.ToBgra32(null!));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(1000)]
    public void PremultiplyInPlaceMatchesScalarReference(int pixels)
    {
        var random = new Random(17 + pixels);
        var source = new byte[pixels * 4];
        random.NextBytes(source);

        // 覆盖 alpha 的两个边界：全透明不该改变 RGB、全不透明必须原样保留
        source[3] = 0;
        source[source.Length - 1] = 255;

        byte[] actual = (byte[])source.Clone();
        PixelOps.PremultiplyInPlace(actual);

        byte[] expected = ScalarPremultiply(source);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PremultiplyIntoDestinationMatchesInPlace()
    {
        var random = new Random(4242);
        var source = new byte[64 * 4];
        random.NextBytes(source);

        byte[] inPlace = (byte[])source.Clone();
        PixelOps.PremultiplyInPlace(inPlace);

        var destination = new byte[source.Length];
        PixelOps.Premultiply(source, destination);

        Assert.Equal(inPlace, destination);
    }

    private static byte[] ScalarPremultiply(byte[] straight)
    {
        var result = new byte[straight.Length];
        for (int i = 0; i < straight.Length; i += 4)
        {
            int a = straight[i + 3];
            for (int c = 0; c < 3; c++)
            {
                int t = (straight[i + c] * a) + 0x80;
                result[i + c] = (byte)((t + (t >> 8)) >> 8);
            }

            result[i + 3] = straight[i + 3];
        }

        return result;
    }
}
