using Sdcb.SimdCodecs.Abstractions;
using Xunit;

namespace ImgProcSharp.SimdCodecs.Tests;


/// <summary>
/// <see cref="ImageDecoderBridge"/> 用 <c>PixelConverter.TryConvert</c> 逐行直写画布，
/// 不再经 <c>ToRgba8()</c> 的整幅临时数组。两条路径必须给出逐字节相同的 8 位 RGBA，
/// 否则 <see cref="Image.Load(ReadOnlySpan{byte})"/> 的像素会悄悄变化。
/// </summary>
public sealed class PixelConverterParityTests
{
    public static TheoryData<int, SampleFormat> Formats => new()
    {
        { 1, SampleFormat.UInt8 },
        { 2, SampleFormat.UInt8 },
        { 3, SampleFormat.UInt8 },
        { 4, SampleFormat.UInt8 },
        { 1, SampleFormat.UInt16 },
        { 2, SampleFormat.UInt16 },
        { 3, SampleFormat.UInt16 },
        { 4, SampleFormat.UInt16 },
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void ConvertMatchesToRgba8(int channels, SampleFormat sampleFormat)
    {
        const int width = 5;
        const int height = 3;
        int bytesPerSample = sampleFormat == SampleFormat.UInt16 ? 2 : 1;

        var random = new Random((channels * 31) + bytesPerSample);
        var pixels = new byte[width * height * channels * bytesPerSample];
        random.NextBytes(pixels);

        var raster = new RasterImage(width, height, channels, sampleFormat, pixels, [], ColorEncoding.AssumedSrgb);
        byte[] expected = raster.ToRgba8();
        var actual = new byte[width * height * 4];

        Assert.True(PixelConverter.TryConvert(raster, actual, 0, ImagePixelFormat.Rgba32));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConvertHonoursRowStrideAndLeavesPaddingUntouched()
    {
        const int width = 3;
        const int height = 2;
        const int stride = (width * 4) + 8;

        var pixels = new byte[width * height * 3];
        new Random(9).NextBytes(pixels);

        var raster = new RasterImage(width, height, 3, SampleFormat.UInt8, pixels, [], ColorEncoding.AssumedSrgb);
        byte[] expected = raster.ToRgba8();

        var destination = new byte[stride * height];
        destination.AsSpan().Fill(0xCD);

        Assert.True(PixelConverter.TryConvert(raster, destination, stride, ImagePixelFormat.Rgba32));

        for (int y = 0; y < height; y++)
        {
            Assert.Equal(
                expected.AsSpan(y * width * 4, width * 4).ToArray(),
                destination.AsSpan(y * stride, width * 4).ToArray());

            Assert.All(
                destination.AsSpan((y * stride) + (width * 4), stride - (width * 4)).ToArray(),
                value => Assert.Equal(0xCD, value));
        }
    }
}
