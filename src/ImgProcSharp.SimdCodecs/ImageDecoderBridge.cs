using Sdcb.SimdCodecs;
using Sdcb.SimdCodecs.Abstractions;
using SimdCodecsApi = Sdcb.SimdCodecs.ImageCodecs;

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

        return new Image(DecodeCanvas(data, info), info.Format, ReadIccProfile(data), info.Orientation);
    }

    /// <summary>
    /// 从文件解码：<c>ImageDecoder.Open(path)</c> 把文件内存映射进来，整份编码数据不进托管堆——
    /// 12 MP 的 JPEG 省掉一次 5–10 MB 的 <c>File.ReadAllBytes</c> 分配与拷贝。
    /// </summary>
    internal static Image DecodeFile(string path)
    {
        using ImageDecoder decoder = ImageDecoder.Open(path);
        ImageFrame frame = decoder.GetFrame(0);
        ImageInfo info = frame.Info;

        return new Image(DecodeCanvas(frame, info), info.Format, ReadMetadata(frame.Metadata), info.Orientation);
    }

    /// <summary>
    /// 强制走「完整解码 + 逐行转换」的慢路径，绕开直写画布。
    /// 供对拍测试用：两条路径必须给出逐字节相同的画布。
    /// </summary>
    internal static Canvas DecodeThroughRaster(ReadOnlySpan<byte> data)
    {
        if (!SimdCodecsApi.TryReadInfo(data, out ImageInfo info))
        {
            throw new InvalidDataException("无法识别的图像格式：Sdcb.SimdCodecs 不支持或数据不完整。");
        }

        return DecodeThroughRaster(data, info);
    }

    private static Canvas DecodeCanvas(ReadOnlySpan<byte> data, ImageInfo info) =>
        TryDecodeInto(data, info) ?? DecodeThroughRaster(data, info);

    private static Canvas DecodeCanvas(ImageFrame frame, ImageInfo info) =>
        TryCopyPixels(frame, info) ?? DecodeThroughRaster(frame, info);

    /// <summary>
    /// 直写路径：<c>TryDecodeInto</c> 把像素直接写进画布缓冲，全程没有解码器输出数组。
    /// 目标必须是「基本 8 位」格式 <c>Rgba32</c>：预乘格式会让编解码器退回
    /// decode-then-convert（<c>PixelFormat.IsBasic8</c> 为假就多一份整图缓冲），
    /// 所以预乘仍由本地那趟 <see cref="Premultiply"/> 补，见 plan §12.2-7。
    /// 解码器拒绝目标格式（或 HDR 文件拒绝 8 位投影）时返回 <see langword="null"/>，由调用方回退慢路径。
    /// </summary>
    private static Canvas? TryDecodeInto(ReadOnlySpan<byte> data, ImageInfo info)
    {
        Canvas canvas = Canvas.Create(info.Width, info.Height);
        try
        {
            if (!SimdCodecsApi.TryDecodeInto(
                    data,
                    canvas.Buffer.AsSpan(0, canvas.Stride * canvas.Height),
                    canvas.Stride,
                    PixelFormat.Rgba32))
            {
                canvas.Dispose();
                return null;
            }

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

    /// <summary>文件路径的直写版本：<see cref="ImageFrame.CopyPixels"/> 是从映射内存直接解到画布，失败时抛异常。</summary>
    private static Canvas? TryCopyPixels(ImageFrame frame, ImageInfo info)
    {
        Canvas canvas = Canvas.Create(info.Width, info.Height);
        try
        {
            frame.CopyPixels(null, PixelFormat.Rgba32, canvas.Buffer.AsSpan(0, canvas.Stride * canvas.Height), canvas.Stride);

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
    /// ICC：2.0 的 <c>TryReadMetadata</c> 对所有格式都只扫元数据段（JPEG APP2、PNG <c>iCCP</c>、
    /// WebP <c>ICCP</c>、TIFF IFD…），完全不碰像素。1.4 时代只有 JPEG 公开了这种读取，
    /// PNG/WebP 想要 ICC 得完整解码一遍；现在任何格式都是免费的。
    /// </summary>
    private static ReadOnlyMemory<byte> ReadIccProfile(ReadOnlySpan<byte> data)
    {
        try
        {
            return SimdCodecsApi.TryReadMetadata(data, out ImageMetadata? metadata)
                ? metadata.Icc
                : ReadOnlyMemory<byte>.Empty;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or NotSupportedException or IndexOutOfRangeException)
        {
            // 元数据段畸形不该让整张图解码失败：留空即可。
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private static ReadOnlyMemory<byte> ReadMetadata(ImageMetadata? metadata) =>
        metadata?.Icc ?? ReadOnlyMemory<byte>.Empty;

    private static Canvas DecodeThroughRaster(ReadOnlySpan<byte> data, ImageInfo info)
    {
        if (!SimdCodecsApi.TryDecode(data, out IDecodedImage? decoded) || decoded is null)
        {
            throw new InvalidDataException($"解码失败：{info.Format} {info.Width}×{info.Height}。");
        }

        using IDisposable? decodedLifetime = decoded as IDisposable;
        return ConvertIntoCanvas(decoded, info);
    }

    private static Canvas DecodeThroughRaster(ImageFrame frame, ImageInfo info)
    {
        IDecodedImage decoded = frame.Decode();
        using IDisposable? decodedLifetime = decoded as IDisposable;
        return ConvertIntoCanvas(decoded, info);
    }

    /// <summary>
    /// 把解码结果逐行转换进新建的画布（行即目标行）：不分配整幅 RGBA8 临时数组，1080p 省掉 8.3 MB。
    /// </summary>
    private static Canvas ConvertIntoCanvas(IDecodedImage decoded, ImageInfo info)
    {
        Canvas canvas = Canvas.Create(decoded.Width, decoded.Height);
        try
        {
            // 目标仍是直通 Rgba32（预乘目标会让转换器多做一遍整图写），预乘在这之后补。
            if (decoded.TryCopyTo(
                    canvas.Buffer.AsSpan(0, canvas.Stride * canvas.Height),
                    canvas.Stride,
                    PixelFormat.Rgba32))
            {
                if (decoded.Channels is 2 or 4 || !canvas.IsFullyOpaque())
                {
                    Premultiply(canvas);
                }

                return canvas;
            }

            // float32 没有默认的 8 位投影（TryCopyTo 会拒绝），退回 ToRgba8 的码值投影，
            // 保持 v1 的既有行为。
            if (decoded.SampleFormat == SampleFormat.Float32)
            {
                ReadOnlySpan<byte> straight = decoded.ToRgba8();
                int rowBytes = canvas.Width * Canvas.BytesPerPixel;

                for (int y = 0; y < canvas.Height; y++)
                {
                    straight.Slice(y * rowBytes, rowBytes).CopyTo(canvas.GetRow(y));
                }

                if (decoded.Channels is 2 or 4 || !canvas.IsFullyOpaque())
                {
                    Premultiply(canvas);
                }

                return canvas;
            }

            throw new InvalidDataException($"解码失败：{info.Format} {info.Width}×{info.Height} 的像素无法投影为 8 位 RGBA。");
        }
        catch
        {
            canvas.Dispose();
            throw;
        }
    }
}
