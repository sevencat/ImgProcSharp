using Sdcb.SimdCodecs.Abstractions;
using Sdcb.SimdCodecs.Jpeg;
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
    internal static Image Decode(ReadOnlySpan<byte> data, ImageDecodeOptions? options = null)
    {
        options ??= ImageDecodeOptions.Default;

        if (!SimdCodecsApi.TryReadInfo(data, out ImageInfo info))
        {
            throw new InvalidDataException("无法识别的图像格式：Sdcb.SimdCodecs 不支持或数据不完整。");
        }

        // 直写路径：解码器把像素写进画布，省掉解码器自己的输出数组和一次整图转换。
        // ICC 不保证有：从文件头顺手取，取不到留空。
        if (!options.RequireIccProfile && DecodeDirectInto(data, info) is { } direct)
        {
            return new Image(direct, info.Format, TryReadIccProfile(data, info), info.Orientation);
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

            Premultiply(canvas);
        }
        catch
        {
            canvas.Dispose();
            throw;
        }

        return new Image(canvas, info.Format, decoded.IccProfile.ToArray(), info.Orientation);
    }

    /// <summary>
    /// 直写路径：<c>TryDecodeInto</c> 把像素直接写进画布缓冲，全程没有解码器输出数组。
    /// 解码器不支持该目标格式（或 HDR 文件拒绝 8 位投影）时返回 <see langword="null"/>，由调用方回退慢路径。
    /// </summary>
    private static Canvas? DecodeDirectInto(ReadOnlySpan<byte> data, ImageInfo info)
    {
        Canvas canvas = Canvas.Create(info.Width, info.Height);
        try
        {
            if (!SimdCodecsApi.TryDecodeInto(
                    data,
                    canvas.Buffer.AsSpan(0, canvas.Stride * canvas.Height),
                    canvas.Stride,
                    ImagePixelFormat.Rgba32))
            {
                canvas.Dispose();
                return null;
            }

            // 不透明图（绝大多数照片）预乘是恒等变换，跳过这一趟全图读改写。
            if (NeedsPremultiply(canvas, info))
            {
                Premultiply(canvas);
            }

            return canvas;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or NotSupportedException)
        {
            canvas.Dispose();
            return null;
        }
        catch
        {
            canvas.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 解码器写进画布的是直通像素，只有带 alpha 的图才需要预乘。
    /// JPEG 没有 alpha 通道；2/4 通道必定带 alpha；1/3 通道的图多数不透明，
    /// 但 PNG 的 <c>tRNS</c> 与 palette 能让它们带透明，所以实测 alpha 比按格式猜稳。
    /// </summary>
    private static bool NeedsPremultiply(Canvas canvas, ImageInfo info)
    {
        if (info.Format == ImageFormat.Jpeg)
        {
            return false;
        }

        return info.Channels is 2 or 4 || !canvas.IsFullyOpaque();
    }

    private static void Premultiply(Canvas canvas)
    {
        // 行无 padding 时整块一次进内核，省掉每行一次的方法调用。
        if (canvas.Stride == canvas.Width * Canvas.BytesPerPixel)
        {
            PixelOps.PremultiplyInPlace(canvas.Buffer.AsSpan(0, canvas.Stride * canvas.Height));
            return;
        }

        for (int y = 0; y < canvas.Height; y++)
        {
            PixelOps.PremultiplyInPlace(canvas.GetRow(y));
        }
    }

    /// <summary>
    /// 直写路径的 ICC：只扫文件头，不碰像素。
    /// 1.4 里只有 JPEG 公开了这种读取（APP2 <c>ICC_PROFILE</c>，多段自动拼接）；
    /// PNG 的 <c>PngReader.ReadHeader</c> 是 internal，WebP 的 ICCP 也得先完整解码，所以两者留空。
    /// 需要 PNG/WebP 的 ICC 时用 <see cref="ImageDecodeOptions.RequireIccProfile"/> 走完整解码。
    /// </summary>
    private static ReadOnlyMemory<byte> TryReadIccProfile(ReadOnlySpan<byte> data, ImageInfo info)
    {
        if (info.Format != ImageFormat.Jpeg)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        try
        {
            return JpegDecoder.TryReadIccProfile(data, out byte[]? icc) && icc.Length > 0
                ? new ReadOnlyMemory<byte>(icc)
                : ReadOnlyMemory<byte>.Empty;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or NotSupportedException or IndexOutOfRangeException)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
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
