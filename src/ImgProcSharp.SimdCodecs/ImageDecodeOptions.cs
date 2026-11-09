namespace ImgProcSharp;


/// <summary>解码选项。</summary>
public sealed class ImageDecodeOptions
{
    /// <summary>默认选项：直写快路径，ICC 能顺手拿到就带上。</summary>
    public static ImageDecodeOptions Default { get; } = new();

    /// <summary>
    /// 是否必须拿到 ICC 配置（默认 <see langword="false"/>）。
    /// <para>
    /// 默认走直写路径：解码器把像素直接写进画布缓冲，不分配解码器自己的输出数组；
    /// ICC 从文件头顺手取（PNG 的 <c>iCCP</c>、JPEG 的 APP2 <c>ICC_PROFILE</c>），取不到就留空。
    /// </para>
    /// <para>
    /// 置为 <see langword="true"/> 时改用完整解码（<c>TryDecode</c> + 逐行转换），任何格式都能拿到
    /// <see cref="Image.IccProfile"/>，代价是多一份解码器输出缓冲和一次整图转换。
    /// </para>
    /// </summary>
    public bool RequireIccProfile { get; init; }
}
