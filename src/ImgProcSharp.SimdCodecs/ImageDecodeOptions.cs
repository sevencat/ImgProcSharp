namespace ImgProcSharp;


/// <summary>
/// 解码选项。
/// <para>
/// Sdcb.SimdCodecs 2.0 之后这里已经没有可调的开关（ICC 任何格式都能从文件头拿到，直写路径是唯一路径），
/// 保留类型只是为了让 <see cref="Image.Load(string, ImageDecodeOptions?)"/> 等签名不破 0.1.0 的源码兼容。
/// </para>
/// </summary>
public sealed class ImageDecodeOptions
{
    /// <summary>默认选项。</summary>
    public static ImageDecodeOptions Default { get; } = new();

    /// <summary>
    /// 已失效（2.0 起恒等于「总能拿到 ICC」）。
    /// <para>
    /// 1.4 时代只有 JPEG 公开了「只读文件头拿 ICC」，PNG/WebP 想要 ICC 只能完整解码一遍，
    /// 所以留了这个开关；2.0 的 <c>TryReadMetadata</c> 对所有格式都只扫元数据段
    /// （JPEG APP2、PNG <c>iCCP</c>、WebP <c>ICCP</c>、TIFF IFD），不再需要为 ICC 走慢路径。
    /// </para>
    /// </summary>
    [Obsolete("Sdcb.SimdCodecs 2.0 起任何格式的 ICC 都由文件头直接给出，此开关已无作用。")]
    public bool RequireIccProfile { get; init; }
}
