using Sdcb.SimdCodecs.Abstractions;

namespace ImgProcSharp;


/// <summary>
/// 解码器注册表：本库不判断格式，只按注册顺序问每个解码器「这段数据你认不认识」。
/// <para>
/// 本程序集只依赖 <c>Sdcb.SimdCodecs.Abstractions</c>，具体格式的解码器由上层引用对应的格式包后在这里登记：
/// <c>CodecRegistry.Register(PngImageDecoder.Registration, JpegImageDecoder.Registration)</c>。
/// 不注册任何解码器时 <see cref="Image.Load(ReadOnlySpan{byte}, ImageDecodeOptions?)"/> 会给出明确的异常信息，
/// 而不是默默失败。
/// </para>
/// <para>
/// 注册是追加式的：先注册的先生效（嗅探顺序即注册顺序），同一个解码器重复注册没有副作用；注册永久有效，
/// 且线程安全——写入走写时复制，读取只取快照，解码与注册可以并发。
/// </para>
/// </summary>
public static class CodecRegistry
{
    private static ImageDecoderRegistration[] s_registry = [];
    private static readonly Lock s_lock = new();

    /// <summary>已注册的格式，按嗅探顺序（快照）。</summary>
    public static IReadOnlyList<ImageFormat> RegisteredFormats
    {
        get
        {
            ImageDecoderRegistration[] snapshot = Volatile.Read(ref s_registry);
            var formats = new ImageFormat[snapshot.Length];

            for (int i = 0; i < snapshot.Length; i++)
            {
                formats[i] = snapshot[i].Format;
            }

            return formats;
        }
    }

    /// <summary>是否一个解码器都没注册。</summary>
    public static bool IsEmpty => Volatile.Read(ref s_registry).Length == 0;

    /// <summary>
    /// 登记解码器。先注册的先生效：<c>GainMapImageDecoder</c> 必须排在 <c>JpegImageDecoder</c> 之前——
    /// 两者都从 <c>FF D8 FF</c> 开始，前者只认真正带 HDR gain map 的 JPEG。
    /// </summary>
    /// <param name="registrations">解码器的 <c>Registration</c>，例如 <c>PngImageDecoder.Registration</c>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> 或其元素为 null。</exception>
    /// <exception cref="ArgumentException">某个注册项缺少必需的委托（见 <see cref="ImageDecoderRegistration.Validate"/>）。</exception>
    public static void Register(params ImageDecoderRegistration[] registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        foreach (ImageDecoderRegistration registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            registration.Validate();
        }

        if (registrations.Length == 0)
        {
            return;
        }

        lock (s_lock)
        {
            ImageDecoderRegistration[] current = s_registry;
            var merged = new List<ImageDecoderRegistration>(current.Length + registrations.Length);
            merged.AddRange(current);

            foreach (ImageDecoderRegistration registration in registrations)
            {
                if (!merged.Exists(existing => IsSameCodec(existing, registration)))
                {
                    merged.Add(registration);
                }
            }

            if (merged.Count != current.Length)
            {
                Volatile.Write(ref s_registry, [.. merged]);
            }
        }
    }

    /// <summary>认领 <paramref name="data"/> 的解码器，没有则 null。</summary>
    internal static ImageDecoderRegistration? Find(ReadOnlySpan<byte> data)
    {
        foreach (ImageDecoderRegistration registration in Volatile.Read(ref s_registry))
        {
            if (registration.CanDecode(data))
            {
                return registration;
            }
        }

        return null;
    }

    // 方法组委托可比较：同一个解码器的两个 Registration 对象算同一个（与上游门面的判重口径一致）。
    private static bool IsSameCodec(ImageDecoderRegistration a, ImageDecoderRegistration b) =>
        ReferenceEquals(a, b)
        || (a.Format == b.Format && a.CanDecode.Equals(b.CanDecode));
}
