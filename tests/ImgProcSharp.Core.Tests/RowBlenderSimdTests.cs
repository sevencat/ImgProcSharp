using Xunit;

namespace ImgProcSharp.Tests;

public class RowBlenderSimdTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(33)]
    [InlineData(100)]
    public void VectorAndScalarPathsAreByteIdentical(int pixels)
    {
        var random = new Random(1234 + pixels);
        for (int iteration = 0; iteration < 20; iteration++)
        {
            var start = new byte[pixels * 4];
            random.NextBytes(start);
            MakePremultiplied(start);

            byte[] coverage = new byte[pixels];
            random.NextBytes(coverage);

            var color = new Color((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));

            byte[] scalar = (byte[])start.Clone();
            byte[] vector = (byte[])start.Clone();

            RowBlender.OverSolidScalar(scalar, color, coverage);
            RowBlender.OverSolidVector128(vector, color, coverage);

            Assert.Equal(scalar, vector);
        }
    }

    [Fact]
    public void DispatchMatchesScalar()
    {
        var random = new Random(99);
        var start = new byte[64 * 4];
        random.NextBytes(start);
        MakePremultiplied(start);

        byte[] coverage = new byte[64];
        random.NextBytes(coverage);
        var color = new Color(200, 100, 50, 180);

        byte[] scalar = (byte[])start.Clone();
        byte[] dispatched = (byte[])start.Clone();

        RowBlender.OverSolidScalar(scalar, color, coverage);
        RowBlender.OverSolid(dispatched, color, coverage);

        Assert.Equal(scalar, dispatched);
    }

    /// <summary>把随机像素的 RGB 压到 alpha 以下，保证输入本身就是合法的预乘数据。</summary>
    private static void MakePremultiplied(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            pixels[i] = (byte)Math.Min(pixels[i], a);
            pixels[i + 1] = (byte)Math.Min(pixels[i + 1], a);
            pixels[i + 2] = (byte)Math.Min(pixels[i + 2], a);
        }
    }
}
