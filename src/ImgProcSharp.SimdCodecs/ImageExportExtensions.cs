using System.Buffers;
using Sdcb.SimdCodecs.Abstractions;

namespace ImgProcSharp;


/// <summary>
/// 导出：把画布交给已注册的编码器，一行写完。本库不实现编码器，只负责调
/// <see cref="ImageEncoderRegistration.Create"/> → <c>AddFrame</c> → <c>WriteRows</c> → <c>Commit</c> 这套样板。
/// <para>
/// 画布是预乘 RGBA8，这里按 <see cref="PixelFormat.Rgba32Premultiplied"/> + 自己的 stride 递给编码器：
/// 编码器内部一边还原直通像素一边编，所以半透明图不需要调用方先摊一张 <c>W*H*4</c> 的临时数组。
/// </para>
/// <para>
/// 用法（先注册一次编码器，和解码器一样）：
/// <code>
/// EncoderRegistry.Register(PngImageEncoder.Registration, JpegImageEncoder.Registration);
/// image.Save("out.png");
/// byte[] jpeg = image.Encode(ImageFormat.Jpeg, new JpegEncodeOptions { Quality = 92 });
/// </code>
/// </para>
/// </summary>
public static class ImageExportExtensions
{
    /// <summary>编码成字节数组。格式必须有已注册的编码器。</summary>
    /// <param name="canvas">要编码的画布。</param>
    /// <param name="format">目标格式。</param>
    /// <param name="options">格式自己的编码选项（如 <c>PngWriteOptions</c>）；类型不匹配时编码器用默认值。</param>
    /// <param name="metadata">要写进文件的 ICC/EXIF/XMP，可为 <see langword="null"/>。</param>
    /// <exception cref="NotSupportedException">该格式没有已注册的编码器。</exception>
    public static byte[] Encode(
        this Canvas canvas,
        ImageFormat format,
        EncodeOptions? options = null,
        ImageMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        return EncodeToBuffer(canvas, format, options, metadata).WrittenSpan.ToArray();
    }

    /// <summary>编码成字节数组，并把图片的 ICC 配置原样带上。</summary>
    /// <param name="image">要编码的图。</param>
    /// <param name="format">目标格式。</param>
    /// <param name="options">格式自己的编码选项。</param>
    /// <exception cref="NotSupportedException">该格式没有已注册的编码器。</exception>
    public static byte[] Encode(this Image image, ImageFormat format, EncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        return EncodeToBuffer(image.Canvas, format, options, MetadataOf(image)).WrittenSpan.ToArray();
    }

    /// <summary>按扩展名推断格式写文件（<c>.png</c> / <c>.jpg</c> / <c>.jpeg</c> 等）。</summary>
    /// <param name="canvas">要编码的画布。</param>
    /// <param name="path">输出路径，扩展名决定格式。</param>
    /// <param name="options">格式自己的编码选项。</param>
    /// <exception cref="NotSupportedException">扩展名不认识，或该格式没有已注册的编码器。</exception>
    public static void Save(this Canvas canvas, string path, EncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        canvas.Save(path, ImageExportFormats.FromPath(path), options);
    }

    /// <summary>写文件，格式显式指定。</summary>
    /// <param name="canvas">要编码的画布。</param>
    /// <param name="path">输出路径。</param>
    /// <param name="format">目标格式。</param>
    /// <param name="options">格式自己的编码选项。</param>
    /// <exception cref="NotSupportedException">该格式没有已注册的编码器。</exception>
    public static void Save(this Canvas canvas, string path, ImageFormat format, EncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(path);
        WriteAll(path, EncodeToBuffer(canvas, format, options, metadata: null).WrittenSpan);
    }

    /// <summary>按扩展名推断格式写文件，并把图片的 ICC 配置原样带上。</summary>
    /// <param name="image">要编码的图。</param>
    /// <param name="path">输出路径，扩展名决定格式。</param>
    /// <param name="options">格式自己的编码选项。</param>
    /// <exception cref="NotSupportedException">扩展名不认识，或该格式没有已注册的编码器。</exception>
    public static void Save(this Image image, string path, EncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        image.Save(path, ImageExportFormats.FromPath(path), options);
    }

    /// <summary>写文件，格式显式指定；图片的 ICC 配置原样带上。</summary>
    /// <param name="image">要编码的图。</param>
    /// <param name="path">输出路径。</param>
    /// <param name="format">目标格式。</param>
    /// <param name="options">格式自己的编码选项。</param>
    /// <exception cref="NotSupportedException">该格式没有已注册的编码器。</exception>
    public static void Save(this Image image, string path, ImageFormat format, EncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(path);
        WriteAll(path, EncodeToBuffer(image.Canvas, format, options, MetadataOf(image)).WrittenSpan);
    }

    private static ArrayBufferWriter<byte> EncodeToBuffer(
        Canvas canvas,
        ImageFormat format,
        EncodeOptions? options,
        ImageMetadata? metadata)
    {
        ImageEncoderRegistration encoder = EncoderRegistry.Require(format);

        // 压缩后一般比原始像素小一半以上，先按一半起步，避免 ArrayBufferWriter 反复翻倍拷贝。
        var output = new ArrayBufferWriter<byte>(Math.Max(256, canvas.Stride * canvas.Height / 2));

        using IImageEncoderSession session = encoder.Create(output, options);
        IImageFrameEncoder frame = session.AddFrame(
            canvas.Width,
            canvas.Height,
            PixelFormat.Rgba32Premultiplied,
            color: null,
            metadata,
            duration: null);

        // 一次把全部行交出去，stride 用画布自己的：行尾 padding 由编码器跳过。
        // 预乘 → 直通（unpremultiply）由编码器内部完成（§Q），这里不摊整图数组。
        frame.WriteRows(
            canvas.Buffer.AsSpan(0, canvas.Stride * canvas.Height),
            canvas.Height,
            canvas.Stride,
            cancellationToken: default);

        session.Commit(cancellationToken: default);
        return output;
    }

    private static ImageMetadata? MetadataOf(Image image) =>
        image.IccProfile.IsEmpty ? null : new ImageMetadata { Icc = image.IccProfile };

    private static void WriteAll(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024);

        stream.Write(bytes);
    }
}


/// <summary>路径扩展名 → <see cref="ImageFormat"/>。</summary>
internal static class ImageExportFormats
{
    /// <summary>按扩展名推断格式；不认识就抛，并在消息里给出显式传格式的写法。</summary>
    internal static ImageFormat FromPath(string path)
    {
        // 本命名空间里有自己的 Path 类型，这里要的是 System.IO.Path。
        string extension = System.IO.Path.GetExtension(path);

        return extension.ToLowerInvariant() switch
        {
            ".png" => ImageFormat.Png,
            ".jpg" or ".jpeg" or ".jpe" or ".jfif" => ImageFormat.Jpeg,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            ".bmp" or ".dib" => ImageFormat.Bmp,
            ".gif" => ImageFormat.Gif,
            ".webp" => ImageFormat.Webp,
            ".qoi" => ImageFormat.Qoi,
            ".ico" => ImageFormat.Ico,
            ".cur" => ImageFormat.Cur,
            ".pnm" or ".ppm" or ".pgm" or ".pbm" or ".pam" => ImageFormat.Pnm,
            ".pfm" => ImageFormat.Pfm,
            _ => throw new NotSupportedException(
                $"无法从扩展名「{extension}」推断图像格式：请显式传入 ImageFormat，"
                + "例如 image.Save(path, ImageFormat.Png)。"),
        };
    }
}
