namespace ImgProcSharp;

/// <summary>
/// 像素级的定点运算与格式转换。画布内部是预乘 Rgba32，这里是唯一的转换边界。
/// </summary>
public static class PixelOps
{
    /// <summary>
    /// x / 255 的定点实现（四舍五入）：<c>t = x + 0x80; (t + (t &gt;&gt; 8)) &gt;&gt; 8</c>。
    /// </summary>
    internal static int Div255(uint x)
    {
        uint t = x + 0x80;
        return (int)((t + (t >> 8)) >> 8);
    }

    /// <summary>8 位乘法：<c>a * b / 255</c>，四舍五入。</summary>
    internal static int Mul255(int a, int b) => Div255((uint)(a * b));

    /// <summary>
    /// 预乘 → 直通：<c>(c * 255 + a / 2) / a</c>。
    /// <paramref name="alpha"/> &lt; 8 的像素直接归零，避免把预乘误差放大成噪点（见 misc §F）。
    /// </summary>
    internal static byte Unpremultiply(byte channel, byte alpha)
    {
        if (alpha < 8)
        {
            return 0;
        }

        if (alpha == 255)
        {
            return channel;
        }

        int v = ((channel * 255) + (alpha >> 1)) / alpha;
        return (byte)(v > 255 ? 255 : v);
    }

    /// <summary>直通 RGBA → 预乘 RGBA（长度必须是 4 的倍数）。</summary>
    public static void Premultiply(ReadOnlySpan<byte> straight, Span<byte> destination)
    {
        if (straight.Length != destination.Length || (straight.Length & 3) != 0)
        {
            throw new ArgumentException("源与目标的长度必须相同且是 4 的倍数。", nameof(destination));
        }

        for (int i = 0; i < straight.Length; i += 4)
        {
            byte a = straight[i + 3];
            destination[i] = (byte)Mul255(straight[i], a);
            destination[i + 1] = (byte)Mul255(straight[i + 1], a);
            destination[i + 2] = (byte)Mul255(straight[i + 2], a);
            destination[i + 3] = a;
        }
    }

    /// <summary>原地预乘。</summary>
    public static void PremultiplyInPlace(Span<byte> pixels)
    {
        if ((pixels.Length & 3) != 0)
        {
            throw new ArgumentException("长度必须是 4 的倍数。", nameof(pixels));
        }

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a == 255)
            {
                continue;
            }

            pixels[i] = (byte)Mul255(pixels[i], a);
            pixels[i + 1] = (byte)Mul255(pixels[i + 1], a);
            pixels[i + 2] = (byte)Mul255(pixels[i + 2], a);
        }
    }

    /// <summary>预乘 RGBA → 直通 RGBA。</summary>
    public static void Unpremultiply(ReadOnlySpan<byte> premultiplied, Span<byte> destination)
    {
        if (premultiplied.Length != destination.Length || (premultiplied.Length & 3) != 0)
        {
            throw new ArgumentException("源与目标的长度必须相同且是 4 的倍数。", nameof(destination));
        }

        for (int i = 0; i < premultiplied.Length; i += 4)
        {
            byte a = premultiplied[i + 3];
            destination[i] = Unpremultiply(premultiplied[i], a);
            destination[i + 1] = Unpremultiply(premultiplied[i + 1], a);
            destination[i + 2] = Unpremultiply(premultiplied[i + 2], a);
            destination[i + 3] = a;
        }
    }

    /// <summary>导出 BGRA32（straight），给只认 Bgra 的消费方用。</summary>
    public static byte[] ToBgra32(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var result = new byte[canvas.Width * canvas.Height * 4];
        for (int y = 0; y < canvas.Height; y++)
        {
            ReadOnlySpan<byte> src = canvas.GetRow(y);
            Span<byte> dst = result.AsSpan(y * canvas.Width * 4, canvas.Width * 4);
            for (int x = 0, o = 0; x < canvas.Width; x++, o += 4)
            {
                byte a = src[o + 3];
                dst[o] = Unpremultiply(src[o + 2], a);
                dst[o + 1] = Unpremultiply(src[o + 1], a);
                dst[o + 2] = Unpremultiply(src[o], a);
                dst[o + 3] = a;
            }
        }

        return result;
    }

    /// <summary>导出 Rgb24（straight），alpha 被丢弃。</summary>
    public static byte[] ToRgb24(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var result = new byte[canvas.Width * canvas.Height * 3];
        for (int y = 0; y < canvas.Height; y++)
        {
            ReadOnlySpan<byte> src = canvas.GetRow(y);
            Span<byte> dst = result.AsSpan(y * canvas.Width * 3, canvas.Width * 3);
            for (int x = 0, s = 0, d = 0; x < canvas.Width; x++, s += 4, d += 3)
            {
                byte a = src[s + 3];
                dst[d] = Unpremultiply(src[s], a);
                dst[d + 1] = Unpremultiply(src[s + 1], a);
                dst[d + 2] = Unpremultiply(src[s + 2], a);
            }
        }

        return result;
    }

    /// <summary>导出 8 位灰度（Rec.601，alpha 忽略），符号距离图/调试用。</summary>
    public static byte[] ToGray8(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var result = new byte[canvas.Width * canvas.Height];
        for (int y = 0; y < canvas.Height; y++)
        {
            ReadOnlySpan<byte> src = canvas.GetRow(y);
            Span<byte> dst = result.AsSpan(y * canvas.Width, canvas.Width);
            for (int x = 0, s = 0; x < canvas.Width; x++, s += 4)
            {
                byte a = src[s + 3];
                int r = Unpremultiply(src[s], a);
                int g = Unpremultiply(src[s + 1], a);
                int b = Unpremultiply(src[s + 2], a);
                dst[x] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
            }
        }

        return result;
    }
}
