using Sdcb.SimdCodecs.Abstractions;
using SimdCodecsApi = Sdcb.SimdCodecs.SimdCodecs;

namespace ImgProcSharp;


/// <summary>
/// 解码桥：全库唯一接触 <c>Sdcb.SimdCodecs</c> 的文件。
/// <para>
/// 其余代码只认识 <see cref="Canvas"/>（预乘 RGBA8），换解码库只改这里。
/// </para>
/// </summary>
internal static class ImageDecoderBridge
{
    internal static Image Decode(ReadOnlySpan<byte> data)
    {
        if (!SimdCodecsApi.TryReadInfo(data, out ImageInfo info))
        {
            throw new InvalidDataException("无法识别的图像格式：Sdcb.SimdCodecs 不支持或数据不完整。");
        }

        if (!SimdCodecsApi.TryDecode(data, out IDecodedImage? decoded) || decoded is null)
        {
            throw new InvalidDataException($"解码失败：{info.Format} {info.Width}×{info.Height}。");
        }

        using IDisposable? decodedLifetime = decoded as IDisposable;

        Canvas canvas = Canvas.Create(decoded.Width, decoded.Height);
        try
        {
            if (!ConvertIntoCanvas(decoded, canvas))
            {
                throw new InvalidDataException($"解码失败：{info.Format} {info.Width}×{info.Height} 的像素无法投影为 8 位 RGBA。");
            }

            for (int y = 0; y < canvas.Height; y++)
            {
                PixelOps.PremultiplyInPlace(canvas.GetRow(y));
            }
        }
        catch
        {
            canvas.Dispose();
            throw;
        }

        return new Image(canvas, info.Format, decoded.IccProfile.ToArray(), info.Orientation);
    }

    /// <summary>
    /// 把解码结果逐行转换进画布缓冲（<paramref name="canvas"/> 的行即目标行）。
    /// 这条路径不再分配整幅 RGBA8 临时数组：1080p 直接省掉 8.3 MB。
    /// </summary>
    private static bool ConvertIntoCanvas(IDecodedImage decoded, Canvas canvas)
    {
        // float32 没有默认的 8 位投影（PixelConverter.TryConvert 会拒绝），
        // 退回 ToRgba8 的码值投影，保持 v1 的既有行为。
        if (decoded.SampleFormat == SampleFormat.Float32)
        {
            ReadOnlySpan<byte> straight = decoded.ToRgba8();
            int rowBytes = canvas.Width * Canvas.BytesPerPixel;

            for (int y = 0; y < canvas.Height; y++)
            {
                straight.Slice(y * rowBytes, rowBytes).CopyTo(canvas.GetRow(y));
            }

            return true;
        }

        return PixelConverter.TryConvert(
            decoded,
            canvas.Buffer.AsSpan(0, canvas.Stride * canvas.Height),
            canvas.Stride,
            ImagePixelFormat.Rgba32);
    }
}
