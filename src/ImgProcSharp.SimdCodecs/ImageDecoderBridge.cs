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

        Canvas canvas = Canvas.Create(decoded.Width, decoded.Height);
        ReadOnlySpan<byte> straight = decoded.ToRgba8();
        int rowBytes = decoded.Width * Canvas.BytesPerPixel;

        for (int y = 0; y < decoded.Height; y++)
        {
            Span<byte> row = canvas.GetRow(y);
            straight.Slice(y * rowBytes, rowBytes).CopyTo(row);
            PixelOps.PremultiplyInPlace(row);
        }

        return new Image(canvas, info.Format, decoded.IccProfile.ToArray(), info.Orientation);
    }
}
