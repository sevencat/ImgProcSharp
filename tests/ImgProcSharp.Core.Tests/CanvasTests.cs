using Xunit;

namespace ImgProcSharp.Tests;

public class CanvasTests
{
    [Fact]
    public void CreateWithoutBackgroundIsTransparent()
    {
        using Canvas canvas = Canvas.Create(3, 2);
        Assert.Equal(3, canvas.Width);
        Assert.Equal(2, canvas.Height);
        Assert.Equal(12, canvas.Stride);
        Assert.True(canvas.Buffer.All(b => b == 0));
        Assert.False(canvas.IsFullyOpaque());
    }

    [Fact]
    public void CreateWithBackgroundFillsEverything()
    {
        using Canvas canvas = Canvas.Create(4, 3, Color.Red);
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                Assert.Equal(Color.Red, canvas.GetColor(x, y));
            }
        }

        Assert.True(canvas.IsFullyOpaque());
    }

    [Fact]
    public void CreateRejectsNonPositiveSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Canvas.Create(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Canvas.Create(1, -1));
    }

    [Fact]
    public void SetAndGetColorRoundtrips()
    {
        using Canvas canvas = Canvas.Create(2, 2);
        Color c = new(10, 200, 30, 128);
        canvas.SetColor(1, 1, c);

        // 128 >= 8，所以 unpremultiply 不会归零
        Color actual = canvas.GetColor(1, 1);
        Assert.Equal(128, actual.A);
        Assert.InRange(Math.Abs(actual.R - c.R), 0, 1);
        Assert.InRange(Math.Abs(actual.G - c.G), 0, 1);
        Assert.InRange(Math.Abs(actual.B - c.B), 0, 1);
    }

    [Fact]
    public void SetColorStoresPremultipliedBytes()
    {
        using Canvas canvas = Canvas.Create(1, 1);
        canvas.SetColor(0, 0, new Color(255, 0, 0, 128));
        byte[] raw = canvas.Buffer;
        Assert.Equal(128, raw[0]);
        Assert.Equal(0, raw[1]);
        Assert.Equal(128, raw[3]);
    }

    [Fact]
    public void GetRowAndPixelValidateBounds()
    {
        using Canvas canvas = Canvas.Create(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.GetRow(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.GetRow(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.GetPixel(2, 0));
        Assert.Equal(4, canvas.GetPixel(1, 1).Length);
    }

    [Fact]
    public void WrapWithPaddingKeepsPaddingUntouched()
    {
        const int width = 2;
        const int height = 2;
        const int stride = (width * 4) + 8;
        var buffer = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            buffer.AsSpan((y * stride) + (width * 4), 8).Fill(0xAB);
        }

        using Canvas canvas = Canvas.Wrap(buffer, width, height, stride);
        canvas.SetColor(1, 1, Color.White);

        Assert.Equal(width * 4, canvas.GetRow(0).Length);
        for (int y = 0; y < height; y++)
        {
            Assert.All(buffer.AsSpan((y * stride) + (width * 4), 8).ToArray(), b => Assert.Equal(0xAB, b));
        }
    }

    [Fact]
    public void WrapRejectsTooSmallBuffer()
    {
        Assert.Throws<ArgumentException>(() => Canvas.Wrap(new byte[10], 2, 2, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => Canvas.Wrap(new byte[16], 2, 2, 7));
    }

    [Fact]
    public void CloneIsIndependent()
    {
        using Canvas canvas = Canvas.Create(2, 1, Color.Red);
        using Canvas clone = canvas.Clone();

        clone.SetColor(0, 0, Color.Blue);
        Assert.Equal(Color.Red, canvas.GetColor(0, 0));
        Assert.Equal(Color.Blue, clone.GetColor(0, 0));
    }

    [Fact]
    public void FlipHorizontalMirrors()
    {
        using Canvas canvas = Canvas.Create(3, 1);
        canvas.SetColor(0, 0, Color.Red);
        canvas.SetColor(2, 0, Color.Blue);

        canvas.FlipHorizontal();

        Assert.Equal(Color.Blue, canvas.GetColor(0, 0));
        Assert.Equal(Color.Red, canvas.GetColor(2, 0));
    }

    [Fact]
    public void FlipVerticalMirrors()
    {
        using Canvas canvas = Canvas.Create(1, 2);
        canvas.SetColor(0, 0, Color.Red);
        canvas.SetColor(0, 1, Color.Blue);

        canvas.FlipVertical();

        Assert.Equal(Color.Blue, canvas.GetColor(0, 0));
        Assert.Equal(Color.Red, canvas.GetColor(0, 1));
    }

    [Fact]
    public void Rotate90SwapsDimensionsClockwise()
    {
        using Canvas canvas = Canvas.Create(2, 1);
        canvas.SetColor(0, 0, Color.Red);
        canvas.SetColor(1, 0, Color.Blue);

        using Canvas rotated = canvas.Rotate90();

        Assert.Equal(1, rotated.Width);
        Assert.Equal(2, rotated.Height);
        Assert.Equal(Color.Red, rotated.GetColor(0, 0));
        Assert.Equal(Color.Blue, rotated.GetColor(0, 1));
    }

    [Fact]
    public void Rotate270IsInverseOfRotate90()
    {
        using Canvas canvas = Canvas.Create(3, 2);
        canvas.SetColor(0, 0, Color.Red);
        canvas.SetColor(2, 1, Color.Blue);

        using Canvas roundTrip = canvas.Rotate90().Rotate270();

        Assert.Equal(canvas.Width, roundTrip.Width);
        Assert.Equal(canvas.Height, roundTrip.Height);
        Assert.Equal(Color.Red, roundTrip.GetColor(0, 0));
        Assert.Equal(Color.Blue, roundTrip.GetColor(2, 1));
    }

    [Fact]
    public void Rotate180MapsCorners()
    {
        using Canvas canvas = Canvas.Create(2, 2);
        canvas.SetColor(0, 0, Color.Red);

        using Canvas rotated = canvas.Rotate180();

        Assert.Equal(Color.Red, rotated.GetColor(1, 1));
        Assert.Equal(Color.Transparent, rotated.GetColor(0, 0));
    }

    [Fact]
    public void CopyToStraightUnpremultiplies()
    {
        using Canvas canvas = Canvas.Create(2, 1);
        canvas.SetColor(0, 0, new Color(200, 100, 50, 128));

        var destination = new byte[8];
        canvas.CopyToStraight(destination, 8);

        Assert.Equal(128, destination[3]);
        Assert.InRange(Math.Abs(destination[0] - 200), 0, 1);
        Assert.InRange(Math.Abs(destination[1] - 100), 0, 1);
        Assert.InRange(Math.Abs(destination[2] - 50), 0, 1);
        Assert.Equal(0, destination[7]);
    }

    [Fact]
    public void CopyToStraightHonoursDestinationStride()
    {
        using Canvas canvas = Canvas.Create(1, 2, Color.White);
        var destination = new byte[16];

        canvas.CopyToStraight(destination, 8);

        Assert.Equal(
            new byte[] { 255, 255, 255, 255, 0, 0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 0 },
            destination);
    }

    [Fact]
    public void CopyToStraightRejectsSmallDestination()
    {
        using Canvas canvas = Canvas.Create(2, 2);
        Assert.Throws<ArgumentException>(() => canvas.CopyToStraight(new byte[8], 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.CopyToStraight(new byte[64], 7));
    }

    [Fact]
    public void FillRectangleClipsToCanvas()
    {
        using Canvas canvas = Canvas.Create(3, 3, Color.Black);
        canvas.FillRectangle(new Rectangle(-1, -1, 2, 2), Color.White);

        Assert.Equal(Color.White, canvas.GetColor(0, 0));
        Assert.Equal(Color.Black, canvas.GetColor(1, 1));
    }

    [Fact]
    public void FillRectangleFullyOutsideIsNoOp()
    {
        using Canvas canvas = Canvas.Create(2, 2, Color.Black);
        canvas.FillRectangle(new Rectangle(5, 5, 2, 2), Color.White);
        canvas.FillRectangle(new Rectangle(0, 0, 0, 3), Color.White);

        Assert.Equal(Color.Black, canvas.GetColor(0, 0));
    }

    [Fact]
    public void FillRectangleBlendsWithCoverage()
    {
        using Canvas canvas = Canvas.Create(2, 1);
        canvas.FillRectangle(new Rectangle(0, 0, 2, 1), new Color(255, 255, 255, 128));

        Color actual = canvas.GetColor(1, 0);
        Assert.Equal(128, actual.A);
        Assert.Equal(255, actual.R);
    }

    [Fact]
    public void DisposeInvalidatesCanvas()
    {
        Canvas canvas = Canvas.Create(1, 1);
        canvas.Dispose();
        Assert.Throws<ObjectDisposedException>(() => canvas.GetRow(0));
        canvas.Dispose();
    }
}
