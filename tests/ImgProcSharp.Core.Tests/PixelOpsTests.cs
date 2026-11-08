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
}
