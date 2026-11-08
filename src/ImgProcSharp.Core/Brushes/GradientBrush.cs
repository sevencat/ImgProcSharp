using System.Numerics;

namespace ImgProcSharp;

/// <summary>渐变停止点：<c>offset</c> 是 0–1 的位置，超出会被夹住。</summary>
public readonly struct ColorStop : IEquatable<ColorStop>
{
    public ColorStop(float offset, Color color)
    {
        Offset = offset;
        Color = color;
    }

    public float Offset { get; }

    public Color Color { get; }

    public bool Equals(ColorStop other) => Offset.Equals(other.Offset) && Color.Equals(other.Color);

    public override bool Equals(object? obj) => obj is ColorStop other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Offset, Color);

    public override string ToString() => $"{Color} @ {Offset}";

    public static bool operator ==(ColorStop left, ColorStop right) => left.Equals(right);

    public static bool operator !=(ColorStop left, ColorStop right) => !left.Equals(right);
}

/// <summary>
/// 渐变笔刷的公共部分：停止点归一化、预乘插值、逐像素混合、可选 4×4 ordered dither。
/// <para>
/// 插值在**预乘空间**做：否则「透明黑 → 不透明白」会在中段出现灰色脏边，
/// 这是所有「渐变叠加在图上」场景最容易踩的坑。
/// </para>
/// </summary>
public abstract class GradientBrush : IBrush
{
    // 4×4 Bayer 矩阵，用于抖动掉色带：值域 0..15，减 7.5 后归一到 ±0.5 LSB
    private static readonly byte[] BayerMatrix =
    [
        0, 8, 2, 10,
        12, 4, 14, 6,
        3, 11, 1, 9,
        15, 7, 13, 5,
    ];

    private readonly ColorStop[] _stops;
    private readonly float[] _stopsR;
    private readonly float[] _stopsG;
    private readonly float[] _stopsB;
    private readonly float[] _stopsA;
    private readonly bool _uniform;
    private readonly Color _uniformColor;

    protected GradientBrush(IEnumerable<ColorStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        ColorStop[] array = stops.ToArray();
        if (array.Length == 0)
        {
            throw new ArgumentException("渐变至少需要一个停止点。", nameof(stops));
        }

        if (array.Length == 1)
        {
            // 单点时补一个同色停止点，让后续逻辑不必特判
            array = [array[0], new ColorStop(1f, array[0].Color)];
        }

        Array.Sort(array, static (a, b) => a.Offset.CompareTo(b.Offset));
        _stops = array;

        _stopsR = new float[array.Length];
        _stopsG = new float[array.Length];
        _stopsB = new float[array.Length];
        _stopsA = new float[array.Length];

        bool uniform = true;
        for (int i = 0; i < array.Length; i++)
        {
            Color c = array[i].Color;
            float a = c.A;
            _stopsR[i] = c.R * a / 255f;
            _stopsG[i] = c.G * a / 255f;
            _stopsB[i] = c.B * a / 255f;
            _stopsA[i] = a;
            uniform &= c == array[0].Color;
        }

        _uniform = uniform;
        _uniformColor = array[0].Color;
    }

    /// <summary>是否对颜色做 4×4 ordered dither（长渐变上抑制色带，默认关闭）。</summary>
    public bool Dither { get; init; }

    /// <summary>
    /// 画布坐标 → 用户坐标的逆矩阵。非恒等时采样点先映射回去，于是渐变的起止点可以理解成用户坐标系里的坐标
    /// （跟随 <see cref="DrawingContext.Transform"/> 一起旋转缩放）。由 <see cref="DrawingContext"/> 内部设置。
    /// </summary>
    internal Matrix3x2 InverseTransform
    {
        get => _inverse;
        init
        {
            _inverse = value;
            _hasInverse = !value.IsIdentity;
        }
    }

    private readonly Matrix3x2 _inverse = Matrix3x2.Identity;
    private readonly bool _hasInverse;

    /// <summary>停止点（按 offset 升序）。</summary>
    public IReadOnlyList<ColorStop> Stops => _stops;

    /// <summary>所有停止点同色时返回该颜色，让纯色矩形仍能命中整行快路径。</summary>
    public bool TryGetSolidColor(out Color color)
    {
        color = _uniformColor;
        return _uniform;
    }

    /// <summary>
    /// 返回一个「采样前先把画布坐标映射回用户坐标」的副本（笔刷保持不可变，色表与副本共享计算结果的代价可忽略）。
    /// <para>
    /// 外部派生类默认返回自身，即不跟随变换；内置的线性/径向渐变会正确跟随。
    /// </para>
    /// </summary>
    internal virtual GradientBrush WithInverseTransform(Matrix3x2 inverse) => this;

    public void Blend(Canvas canvas, Rectangle region, ReadOnlySpan<byte> coverage, int coverageStride)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        for (int y = 0; y < region.Height; y++)
        {
            Span<byte> row = canvas.GetRow(region.Y + y)
                .Slice(region.X * Canvas.BytesPerPixel, region.Width * Canvas.BytesPerPixel);
            ReadOnlySpan<byte> coverageRow = coverage.Slice(y * coverageStride, region.Width);
            BlendRow(row, coverageRow, region.X, region.Y + y);
        }
    }

    /// <summary>
    /// 把一行的渐变混进目标行。<paramref name="row"/> 已裁剪到 region，<paramref name="coverageRow"/> 等长。
    /// </summary>
    /// <param name="row">画布的一行（预乘 RGBA）。</param>
    /// <param name="coverageRow">逐像素覆盖率。</param>
    /// <param name="absoluteX">该行第一个像素的画布 x 坐标（给抖动矩阵用的，务必传绝对坐标）。</param>
    /// <param name="absoluteY">该行的画布 y 坐标。</param>
    protected void BlendRow(Span<byte> row, ReadOnlySpan<byte> coverageRow, int absoluteX, int absoluteY)
    {
        for (int i = 0, o = 0; i < coverageRow.Length; i++, o += Canvas.BytesPerPixel)
        {
            byte coverage = coverageRow[i];
            if (coverage == 0)
            {
                continue;
            }

            float offset = Dither ? GetDitherOffset(absoluteX + i, absoluteY) : 0f;
            SamplePremultiplied(absoluteX + i, absoluteY, offset, out float sr, out float sg, out float sb, out float sa);

            int a = PixelOps.Mul255((int)MathF.Round(Math.Clamp(sa, 0f, 255f)), coverage);
            if (a == 0)
            {
                continue;
            }

            int inv = 255 - a;
            row[o] = (byte)Math.Min(255, PixelOps.Mul255((int)sr, coverage) + PixelOps.Mul255(row[o], inv));
            row[o + 1] = (byte)Math.Min(255, PixelOps.Mul255((int)sg, coverage) + PixelOps.Mul255(row[o + 1], inv));
            row[o + 2] = (byte)Math.Min(255, PixelOps.Mul255((int)sb, coverage) + PixelOps.Mul255(row[o + 2], inv));
            row[o + 3] = (byte)Math.Min(255, a + PixelOps.Mul255(row[o + 3], inv));
        }
    }

    /// <summary>
    /// 采样某个绝对像素的预乘颜色。子类只需回答「这个点落在渐变的哪个位置（0–1）」。
    /// </summary>
    /// <param name="x">绝对像素 x。</param>
    /// <param name="y">绝对像素 y。</param>
    /// <param name="ditherOffset">抖动偏置（以「像素中心」为单位，通常 ±0.5）。</param>
    /// <param name="r">预乘红。</param>
    /// <param name="g">预乘绿。</param>
    /// <param name="b">预乘蓝。</param>
    /// <param name="a">alpha（0–255）。</param>
    protected void SamplePremultiplied(int x, int y, float ditherOffset, out float r, out float g, out float b, out float a)
    {
        // 像素中心采样：+0.5 避免整像素边界上出现「半像素」偏差
        float sampleX = x + 0.5f;
        float sampleY = y + 0.5f;

        if (_hasInverse)
        {
            float userX = (sampleX * _inverse.M11) + (sampleY * _inverse.M21) + _inverse.M31;
            float userY = (sampleX * _inverse.M12) + (sampleY * _inverse.M22) + _inverse.M32;
            sampleX = userX;
            sampleY = userY;
        }

        float t = ParameterAt(sampleX, sampleY) + (ditherOffset / 255f);
        t = Math.Clamp(t, 0f, 1f);

        int index = FindSegment(t);
        if (index >= _stops.Length - 1)
        {
            r = _stopsR[^1];
            g = _stopsG[^1];
            b = _stopsB[^1];
            a = _stopsA[^1];
            return;
        }

        float from = _stops[index].Offset;
        float to = _stops[index + 1].Offset;
        float local = to > from ? (t - from) / (to - from) : 0f;

        r = _stopsR[index] + ((_stopsR[index + 1] - _stopsR[index]) * local);
        g = _stopsG[index] + ((_stopsG[index + 1] - _stopsG[index]) * local);
        b = _stopsB[index] + ((_stopsB[index + 1] - _stopsB[index]) * local);
        a = _stopsA[index] + ((_stopsA[index + 1] - _stopsA[index]) * local);
    }

    /// <summary>点 (x, y) 在渐变轴上的归一化位置；可以超出 0–1，由采样端夹住。</summary>
    protected abstract float ParameterAt(float x, float y);

    private int FindSegment(float t)
    {
        int i = 0;
        while (i < _stops.Length - 2 && t > _stops[i + 1].Offset)
        {
            i++;
        }

        return i;
    }

    private static float GetDitherOffset(int x, int y)
    {
        int index = ((y & 3) * 4) + (x & 3);
        return BayerMatrix[index] - 7.5f;
    }
}

/// <summary>
/// 线性渐变：沿 <c>from</c> → <c>to</c> 的方向插值。两个点重合时退化成单色（全部取最后一个停止点）。
/// </summary>
public sealed class LinearGradientBrush : GradientBrush
{
    private readonly float _fromX;
    private readonly float _fromY;
    private readonly float _dx;
    private readonly float _dy;
    private readonly float _lengthSquared;

    public LinearGradientBrush(PointF from, PointF to, params ColorStop[] stops)
        : base(stops)
    {
        _fromX = from.X;
        _fromY = from.Y;
        _dx = to.X - from.X;
        _dy = to.Y - from.Y;
        _lengthSquared = (_dx * _dx) + (_dy * _dy);
    }

    public PointF From => new(_fromX, _fromY);

    public PointF To => new(_fromX + _dx, _fromY + _dy);

    internal override GradientBrush WithInverseTransform(Matrix3x2 inverse)
        => new LinearGradientBrush(From, To, Stops.ToArray()) { Dither = Dither, InverseTransform = inverse };

    protected override float ParameterAt(float x, float y)
        => _lengthSquared <= 1e-12f
            ? 1f
            : (((x - _fromX) * _dx) + ((y - _fromY) * _dy)) / _lengthSquared;
}

/// <summary>
/// 径向渐变：从 <c>center</c> 向外到 <c>radius</c> 插值。支持椭圆（宽高不同），半径 ≤ 0 时退化成单色。
/// </summary>
public sealed class RadialGradientBrush : GradientBrush
{
    private readonly float _centerX;
    private readonly float _centerY;
    private readonly float _radiusX;
    private readonly float _radiusY;

    public RadialGradientBrush(PointF center, SizeF radius, params ColorStop[] stops)
        : base(stops)
    {
        _centerX = center.X;
        _centerY = center.Y;
        _radiusX = MathF.Abs(radius.Width);
        _radiusY = MathF.Abs(radius.Height);
    }

    public RadialGradientBrush(PointF center, float radius, params ColorStop[] stops)
        : this(center, new SizeF(radius, radius), stops)
    {
    }

    public PointF Center => new(_centerX, _centerY);

    public SizeF Radius => new(_radiusX, _radiusY);

    internal override GradientBrush WithInverseTransform(Matrix3x2 inverse)
        => new RadialGradientBrush(Center, Radius, Stops.ToArray()) { Dither = Dither, InverseTransform = inverse };

    protected override float ParameterAt(float x, float y)
    {
        if (_radiusX <= 1e-6f || _radiusY <= 1e-6f)
        {
            return 1f;
        }

        float nx = (x - _centerX) / _radiusX;
        float ny = (y - _centerY) / _radiusY;
        return MathF.Sqrt((nx * nx) + (ny * ny));
    }
}
