using Sdcb.SimdCodecs.Abstractions;

namespace ImgProcSharp;


/// <summary>
/// 一张可绘制的图：解码后的 <see cref="Canvas"/> 加上它的来源信息。
/// <para>
/// v1 不提供 <c>Save</c>：输出编码不在本库范围内。不透明画布可以把
/// <see cref="Canvas.Buffer"/> 直接交给 <c>PngWriter</c> / <c>JpegEncoder</c>（预乘即直通）；
/// 带透明的画布先 <see cref="Canvas.CopyToStraight(Span{byte}, int, bool?)"/> 再编，见 README。
/// </para>
/// </summary>
public sealed class Image : IDisposable
{
    private Canvas _canvas;

    internal Image(Canvas canvas, ImageFormat sourceFormat, ReadOnlyMemory<byte> iccProfile, ImageOrientation orientation)
    {
        _canvas = canvas;
        SourceFormat = sourceFormat;
        IccProfile = iccProfile;
        Orientation = orientation;
    }

    /// <summary>从文件加载。格式由内容决定，不看扩展名。</summary>
    /// <param name="path">文件路径。</param>
    /// <param name="options">解码选项（2.0 起已无可用开关，仅为源码兼容保留）。</param>
    /// <exception cref="IOException">读文件失败。</exception>
    /// <exception cref="InvalidDataException">不是可解码的图像。</exception>
    public static Image Load(string path, ImageDecodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        _ = options;
        return ImageDecoderBridge.DecodeFile(path);
    }

    /// <summary>从流加载（读到结尾）。<paramref name="leaveOpen"/> 为 <see langword="false"/> 时负责释放流。</summary>
    /// <param name="stream">要读取的流。</param>
    /// <param name="leaveOpen">读完是否保留流。</param>
    /// <param name="options">解码选项（2.0 起已无可用开关，仅为源码兼容保留）。</param>
    public static Image Load(Stream stream, bool leaveOpen = false, ImageDecodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _ = options;

        try
        {
            // 可寻址流按剩余长度预分配，省掉 MemoryStream 反复翻倍扩容的整块拷贝。
            long remaining = stream.CanSeek ? stream.Length - stream.Position : 0;
            using var buffer = new MemoryStream(remaining is > 0 and <= int.MaxValue ? (int)remaining : 0);
            stream.CopyTo(buffer);
            return ImageDecoderBridge.Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        }
        finally
        {
            if (!leaveOpen)
            {
                stream.Dispose();
            }
        }
    }

    /// <summary>从内存加载。</summary>
    /// <param name="data">完整的编码图像字节。</param>
    /// <param name="options">解码选项（2.0 起已无可用开关，仅为源码兼容保留）。</param>
    public static Image Load(ReadOnlySpan<byte> data, ImageDecodeOptions? options = null)
    {
        _ = options;
        return ImageDecoderBridge.Decode(data);
    }

    /// <summary>新建空白画布（全透明，除非指定 <paramref name="background"/>）。</summary>
    public static Image Create(int width, int height, Color? background = null) =>
        new(Canvas.Create(width, height, background), ImageFormat.Unknown, ReadOnlyMemory<byte>.Empty, ImageOrientation.Normal);

    /// <summary>底层画布（预乘 RGBA8）。绘制直接对它做，或用 <see cref="Mutate"/>。</summary>
    public Canvas Canvas => _canvas;

    public int Width => _canvas.Width;

    public int Height => _canvas.Height;

    /// <summary>来源格式；<see cref="Create"/> 出来的图是 <see cref="ImageFormat.Unknown"/>。</summary>
    public ImageFormat SourceFormat { get; }

    /// <summary>原图的 ICC 配置（没有则为空）。只做透传，不做色彩管理。</summary>
    public ReadOnlyMemory<byte> IccProfile { get; }

    /// <summary>EXIF 方向。<see cref="Load(string, ImageDecodeOptions)"/> 不会自动应用，需要时调 <see cref="ApplyOrientation"/>。</summary>
    public ImageOrientation Orientation { get; private set; }

    /// <summary>链式处理：绘制/缩放后返回自身。</summary>
    public Image Mutate(Action<IImageProcessingContext> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        operations(new ImageProcessingContext(this));
        return this;
    }

    /// <summary>深拷贝（画布独立）。</summary>
    public Image Clone() => new(_canvas.Clone(), SourceFormat, IccProfile, Orientation);

    /// <summary>按 EXIF 方向把画布转正；已经是正向时不做任何事。</summary>
    public Image ApplyOrientation()
    {
        switch (Orientation)
        {
            case ImageOrientation.Normal:
                break;
            case ImageOrientation.FlipHorizontal:
                _canvas.FlipHorizontal();
                break;
            case ImageOrientation.Rotate180:
                ReplaceCanvas(_canvas.Rotate180());
                break;
            case ImageOrientation.FlipVertical:
                _canvas.FlipVertical();
                break;
            case ImageOrientation.Transpose:
                ReplaceCanvas(_canvas.Rotate90());
                _canvas.FlipHorizontal();
                break;
            case ImageOrientation.Rotate90:
                ReplaceCanvas(_canvas.Rotate90());
                break;
            case ImageOrientation.Transverse:
                ReplaceCanvas(_canvas.Rotate90());
                _canvas.FlipVertical();
                break;
            case ImageOrientation.Rotate270:
                ReplaceCanvas(_canvas.Rotate270());
                break;
            default:
                break;
        }

        Orientation = ImageOrientation.Normal;
        return this;
    }

    internal void ReplaceCanvas(Canvas canvas)
    {
        Canvas previous = _canvas;
        _canvas = canvas;

        if (!ReferenceEquals(previous, canvas))
        {
            previous.Dispose();
        }
    }

    public void Dispose() => _canvas.Dispose();
}
