using Sdcb.SimdCodecs.Abstractions;

namespace ImgProcSharp;


/// <summary>
/// 解码桥：全库唯一接触 <c>Sdcb.SimdCodecs</c> 的文件。
/// <para>
/// 本程序集只依赖 <c>Sdcb.SimdCodecs.Abstractions</c>——格式解码器由上层注册到 <see cref="CodecRegistry"/>，
/// 所以引用本库不会连带任何格式包。其余代码只认识 <see cref="Canvas"/>（预乘 RGBA8），换解码库只改这里。
/// </para>
/// </summary>
internal static class ImageDecoderBridge
{
    internal static Image Decode(ReadOnlySpan<byte> data)
    {
        ImageDecoderRegistration codec = Require(data);

        if (!codec.TryReadInfo(data, out ImageInfo info))
        {
            throw new InvalidDataException("无法解析图像头：数据不完整或已损坏。");
        }

        return new Image(DecodeCanvas(codec, data, info), info.Format, ReadIccProfile(codec, data), info.Orientation);
    }

    /// <summary>
    /// 从文件解码：<c>ByteSource.FromFile(path)</c> 把文件内存映射进来，整份编码数据不进托管堆——
    /// 12 MP 的 JPEG 省掉一次 5–10 MB 的 <c>File.ReadAllBytes</c> 分配与拷贝。
    /// </summary>
    internal static Image DecodeFile(string path)
    {
        using IByteSource source = ByteSource.FromFile(path);
        return Decode(source.GetMemory().Span);
    }

    /// <summary>
    /// 强制走「完整解码 + 逐行转换」的慢路径，绕开直写画布。
    /// 供对拍测试用：两条路径必须给出逐字节相同的画布。
    /// </summary>
    internal static Canvas DecodeThroughRaster(ReadOnlySpan<byte> data)
    {
        ImageDecoderRegistration codec = Require(data);

        if (!codec.TryReadInfo(data, out ImageInfo info))
        {
            throw new InvalidDataException("无法解析图像头：数据不完整或已损坏。");
        }

        return DecodeThroughRaster(codec, data, info);
    }

    /// <summary>认领数据的解码器；一个都没注册时把「怎么注册」写进异常消息。</summary>
    private static ImageDecoderRegistration Require(ReadOnlySpan<byte> data) =>
        CodecRegistry.Find(data) ?? throw new InvalidDataException(
            CodecRegistry.IsEmpty
                ? "没有注册任何解码器：先引用所需的格式包（如 Sdcb.SimdCodecs.Png、Sdcb.SimdCodecs.Jpeg），"
                  + "再在启动时调用 CodecRegistry.Register(PngImageDecoder.Registration, JpegImageDecoder.Registration)。"
                : "无法识别的图像格式：已注册的解码器都不认这段数据（格式不在支持范围内，或数据不完整）。");

    private static Canvas DecodeCanvas(ImageDecoderRegistration codec, ReadOnlySpan<byte> data, ImageInfo info) =>
        TryDecodeInto(codec, data, info) ?? DecodeThroughRaster(codec, data, info);

    /// <summary>
    /// 直写路径：<c>TryDecodeInto</c> 把像素直接写进画布缓冲，全程没有解码器输出数组。
    /// 目标必须是「基本 8 位」格式 <c>Rgba32</c>：预乘格式会让编解码器退回
    /// decode-then-convert（<c>PixelFormat.IsBasic8</c> 为假就多一份整图缓冲），
    /// 所以预乘仍由本地那趟 <see cref="Premultiply"/> 补，见 plan §12.2-7。
    /// 解码器拒绝目标格式（或 HDR 文件拒绝 8 位投影）时返回 <see langword="null"/>，由调用方回退慢路径。
    /// </summary>
    private static Canvas? TryDecodeInto(ImageDecoderRegistration codec, ReadOnlySpan<byte> data, ImageInfo info)
    {
        if (codec.TryDecodeInto is not { } decodeInto)
        {
            return null;
        }

        Canvas canvas = Canvas.Create(info.Width, info.Height);
        try
        {
            if (!decodeInto(
                    data,
                    canvas.Buffer.AsSpan(0, canvas.Stride * canvas.Height),
                    canvas.Stride,
                    PixelFormat.Rgba32,
                    options: null,
                    cancellationToken: default))
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
    /// ICC：<c>TryReadMetadata</c> 对所有格式都只扫元数据段（JPEG APP2、PNG <c>iCCP</c>、
    /// WebP <c>ICCP</c>、TIFF IFD…），完全不碰像素。这里复制一份再交给 <see cref="Image"/>：
    /// 元数据可能是调用方缓冲（或刚释放的映射内存）的切片，而 <see cref="Image"/> 活得比它久。
    /// </summary>
    private static ReadOnlyMemory<byte> ReadIccProfile(ImageDecoderRegistration codec, ReadOnlySpan<byte> data)
    {
        try
        {
            if (codec.TryReadMetadata is not { } readMetadata
                || !readMetadata(data, out ImageMetadata? metadata)
                || metadata is null)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            return metadata.Icc.IsEmpty ? ReadOnlyMemory<byte>.Empty : metadata.Icc.ToArray();
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or NotSupportedException or IndexOutOfRangeException)
        {
            // 元数据段畸形不该让整张图解码失败：留空即可。
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private static Canvas DecodeThroughRaster(ImageDecoderRegistration codec, ReadOnlySpan<byte> data, ImageInfo info)
    {
        if (codec.TryDecode is not { } tryDecode
            || !tryDecode(data, out IDecodedImage? decoded, options: null, cancellationToken: default)
            || decoded is null)
        {
            throw new InvalidDataException($"解码失败：{info.Format} {info.Width}×{info.Height}。");
        }

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
