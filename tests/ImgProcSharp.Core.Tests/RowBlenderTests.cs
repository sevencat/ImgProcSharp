using Xunit;

namespace ImgProcSharp.Tests;

public class RowBlenderTests
{
    [Fact]
    public void ZeroCoverageLeavesDestinationUntouched()
    {
        var dst = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
        byte[] expected = (byte[])dst.Clone();

        RowBlender.OverSolid(dst, Color.White, new byte[] { 0, 0 });

        Assert.Equal(expected, dst);
    }

    [Fact]
    public void OpaqueSourceWithFullCoverageReplacesPixels()
    {
        var dst = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
        RowBlender.OverSolid(dst, new Color(1, 2, 3, 255), new byte[] { 255, 255 });
        Assert.Equal(new byte[] { 1, 2, 3, 255, 1, 2, 3, 255 }, dst);
    }

    [Fact]
    public void HalfCoverageOfOpaqueWhiteOnTransparentGivesHalfAlpha()
    {
        var dst = new byte[8];
        RowBlender.OverSolid(dst, Color.White, new byte[] { 128, 255 });

        Assert.Equal(new byte[] { 128, 128, 128, 128, 255, 255, 255, 255 }, dst);
    }

    [Fact]
    public void BlendStaysWithinPremultipliedBounds()
    {
        var dst = new byte[] { 64, 32, 16, 128, 0, 0, 0, 0 };
        RowBlender.OverSolid(dst, new Color(200, 100, 50, 96), new byte[] { 255, 77 });

        Assert.True(dst[0] <= dst[3]);
        Assert.True(dst[1] <= dst[3]);
        Assert.True(dst[2] <= dst[3]);
        Assert.True(dst[4] <= dst[7]);
        Assert.True(dst[5] <= dst[7]);
        Assert.True(dst[6] <= dst[7]);
    }

    [Fact]
    public void RejectsMismatchedRowLength()
    {
        Assert.Throws<ArgumentException>(() => RowBlender.OverSolid(new byte[4], Color.White, new byte[] { 255, 255 }));
    }
}
