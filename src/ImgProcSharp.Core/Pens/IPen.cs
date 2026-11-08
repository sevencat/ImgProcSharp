namespace ImgProcSharp;


/// <summary>
/// 描边画笔：只要颜色和线宽。命名与 ImageSharp.Drawing 的 <c>IPen</c> 对齐。
/// <para>
/// v1 只有实线：圆角连接（round join）+ 圆头端点（round cap），没有 dash、没有 miter。
/// 需要虚线请自己用 <see cref="PathBuilder"/> 把线段切开后逐段 <c>Draw</c>。
/// </para>
/// </summary>
public interface IPen
{
    /// <summary>线条颜色。</summary>
    Color Color { get; }

    /// <summary>线宽（画布像素，必须为正）。描边时的实际宽度会被当前变换的缩放因子放大。</summary>
    float Width { get; }
}

/// <summary>最常用的画笔：单一颜色 + 线宽。</summary>
public sealed class Pen : IPen
{
    public Pen(Color color, float width = 1f)
    {
        if (!(width > 0f) || float.IsInfinity(width))
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "线宽必须是有限正数。");
        }

        Color = color;
        Width = width;
    }

    public Color Color { get; }

    public float Width { get; }
}
