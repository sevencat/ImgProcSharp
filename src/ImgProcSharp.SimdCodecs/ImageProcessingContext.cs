using System.Numerics;

namespace ImgProcSharp;


/// <summary><see cref="IImageProcessingContext"/> 的实现：所有操作都转给当前画布的 <see cref="DrawingContext"/>。</summary>
internal sealed class ImageProcessingContext : IImageProcessingContext
{
    private DrawingContext _drawing;

    internal ImageProcessingContext(Image image)
    {
        Image = image;
        _drawing = new DrawingContext(image.Canvas);
    }

    public Image Image { get; }

    public Matrix3x2 Transform => _drawing.Transform;

    public Size GetCurrentSize() => new(Image.Width, Image.Height);

    public IImageProcessingContext Resize(ResizeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Image.ReplaceCanvas(Image.Canvas.Resize(options));
        Rebind();
        return this;
    }

    public IImageProcessingContext Resize(int width, int height)
    {
        Image.ReplaceCanvas(Image.Canvas.Resize(width, height, KnownResamplers.Bicubic));
        Rebind();
        return this;
    }

    public IImageProcessingContext Clear(Color color)
    {
        _drawing.Clear(color);
        return this;
    }

    public IImageProcessingContext Fill(IBrush brush, IPath path, FillRule rule = FillRule.NonZero)
    {
        _drawing.Fill(brush, path, rule);
        return this;
    }

    public IImageProcessingContext Fill(Color color, IPath path, FillRule rule = FillRule.NonZero)
    {
        _drawing.Fill(color, path, rule);
        return this;
    }

    public IImageProcessingContext Draw(IPen pen, IPath path)
    {
        _drawing.Draw(pen, path);
        return this;
    }

    public IImageProcessingContext Draw(Color color, float width, IPath path)
    {
        _drawing.Draw(color, width, path);
        return this;
    }

    public IImageProcessingContext DrawText(TextOptions options, string text, PointF location)
    {
        _drawing.DrawText(text, location, options);
        return this;
    }

    public IImageProcessingContext PushClip(IPath path)
    {
        _drawing.PushClip(path);
        return this;
    }

    public IImageProcessingContext PopClip()
    {
        _drawing.PopClip();
        return this;
    }

    public IImageProcessingContext PushTransform(Matrix3x2 transform)
    {
        _drawing.PushTransform(transform);
        return this;
    }

    public IImageProcessingContext SetTransform(Matrix3x2 transform)
    {
        _drawing.SetTransform(transform);
        return this;
    }

    public IImageProcessingContext ResetTransform()
    {
        _drawing.ResetTransform();
        return this;
    }

    /// <summary>
    /// 换画布之后重绑 <see cref="DrawingContext"/>，变换保持不变（用户代码里 <c>PushTransform</c> 的语义不该被 <c>Resize</c> 清掉）。
    /// 裁剪掩码与画布尺寸绑定，<c>Resize</c> 后必然失效，随旧上下文一起丢弃。
    /// </summary>
    private void Rebind()
    {
        Matrix3x2 transform = _drawing.Transform;
        _drawing = new DrawingContext(Image.Canvas);
        _drawing.SetTransform(transform);
    }
}
