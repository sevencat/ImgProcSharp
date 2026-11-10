using Sdcb.SimdCodecs.Abstractions;

namespace ImgProcSharp;


/// <summary>
/// 编码器注册表，与 <see cref="CodecRegistry"/> 对称：本库只认识
/// <c>Sdcb.SimdCodecs.Abstractions</c> 的 <see cref="ImageEncoderRegistration"/>，
/// 具体格式的编码器由上层引用对应格式包后登记：
/// <c>EncoderRegistry.Register(PngImageEncoder.Registration, JpegImageEncoder.Registration)</c>。
/// <para>
/// 与解码器不同，一个 <see cref="ImageFormat"/> 只保留一个编码器——同一格式重复注册即替换（后注册的生效），
/// 与上游门面的判重口径一致。
/// </para>
/// </summary>
public static class EncoderRegistry
{
    private static ImageEncoderRegistration[] s_registry = [];
    private static readonly Lock s_lock = new();

    /// <summary>已注册的格式（快照）。</summary>
    public static IReadOnlyList<ImageFormat> RegisteredFormats
    {
        get
        {
            ImageEncoderRegistration[] snapshot = Volatile.Read(ref s_registry);
            var formats = new ImageFormat[snapshot.Length];

            for (int i = 0; i < snapshot.Length; i++)
            {
                formats[i] = snapshot[i].Format;
            }

            return formats;
        }
    }

    /// <summary>是否一个编码器都没注册。</summary>
    public static bool IsEmpty => Volatile.Read(ref s_registry).Length == 0;

    /// <summary>登记编码器；同一格式已有的编码器被替换。</summary>
    /// <param name="registrations">编码器的 <c>Registration</c>，例如 <c>PngImageEncoder.Registration</c>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> 或其元素为 null。</exception>
    public static void Register(params ImageEncoderRegistration[] registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        if (registrations.Length == 0)
        {
            return;
        }

        lock (s_lock)
        {
            var merged = new List<ImageEncoderRegistration>(s_registry);

            foreach (ImageEncoderRegistration registration in registrations)
            {
                ArgumentNullException.ThrowIfNull(registration);
                merged.RemoveAll(existing => existing.Format == registration.Format);
                merged.Add(registration);
            }

            Volatile.Write(ref s_registry, [.. merged]);
        }
    }

    /// <summary>写该格式的编码器，没注册则 null。</summary>
    internal static ImageEncoderRegistration? Find(ImageFormat format)
    {
        foreach (ImageEncoderRegistration registration in Volatile.Read(ref s_registry))
        {
            if (registration.Format == format)
            {
                return registration;
            }
        }

        return null;
    }

    /// <summary>写该格式的编码器；没有就把「怎么注册」写进异常消息。</summary>
    internal static ImageEncoderRegistration Require(ImageFormat format) =>
        Find(format) ?? throw new NotSupportedException(
            IsEmpty
                ? $"没有注册任何编码器：先引用所需的格式包（如 Sdcb.SimdCodecs.Png、Sdcb.SimdCodecs.Jpeg），"
                  + $"再在启动时调用 EncoderRegistry.Register(PngImageEncoder.Registration, JpegImageEncoder.Registration)。（要写的是 {format}）"
                : $"没有能写 {format} 的编码器：已注册的编码器只覆盖 {string.Join(" / ", RegisteredFormats)}，"
                  + "引用该格式的包后调用 EncoderRegistry.Register(...) 登记它的 Registration 即可。");
}
