using System.Numerics;

namespace ImgProcSharp;


/// <summary>
/// 链式处理上下文，方法名与 ImageSharp.Drawing 的 <c>IImageProcessingContext</c> 对齐。
/// <para>每个方法返回自身，可以 <c>image.Mutate(x =&gt; x.Clear(...).Fill(...))</c> 串起来。</para>
/// </summary>
public interface IImageProcessingContext
{
    /// <summary>正在处理的图。</summary>
    Image Image { get; }

    /// <summary>当前用户坐标系矩阵（由 <see cref="PushTransform"/> 等维护）。</summary>
    Matrix3x2 Transform { get; }

    /// <summary>当前画布尺寸（<see cref="Resize(ResizeOptions)"/> 之后会变）。</summary>
    Size GetCurrentSize();

    /// <summary>替换当前画布为缩放结果（旧画布释放）。</summary>
    IImageProcessingContext Resize(ResizeOptions options);

    /// <summary>拉伸到指定尺寸，等价于 <see cref="ResizeMode.Stretch"/>。</summary>
    IImageProcessingContext Resize(int width, int height);

    IImageProcessingContext Clear(Color color);

    IImageProcessingContext Fill(IBrush brush, IPath path, FillRule rule = FillRule.NonZero);

    IImageProcessingContext Fill(Color color, IPath path, FillRule rule = FillRule.NonZero);

    /// <summary>描边（圆角连接 + 圆头端点）。</summary>
    IImageProcessingContext Draw(IPen pen, IPath path);

    /// <summary>用纯色描边。</summary>
    IImageProcessingContext Draw(Color color, float width, IPath path);

    IImageProcessingContext DrawText(TextOptions options, string text, PointF location);

    /// <summary>限制后续绘制的可见区域（与已有裁剪取交集）。</summary>
    IImageProcessingContext PushClip(IPath path);

    /// <summary>弹出一层裁剪（与 <see cref="PushClip"/> 配对）。</summary>
    IImageProcessingContext PopClip();

    /// <summary>在当前变换之上再叠加一层。</summary>
    IImageProcessingContext PushTransform(Matrix3x2 transform);

    /// <summary>替换当前变换。</summary>
    IImageProcessingContext SetTransform(Matrix3x2 transform);

    /// <summary>回到恒等变换。</summary>
    IImageProcessingContext ResetTransform();
}
