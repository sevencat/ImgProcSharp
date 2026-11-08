namespace ImgProcSharp;


/// <summary>
/// 描边轮廓生成：把一条折线变成「可以按 <see cref="FillRule.NonZero"/> 填充的闭合轮廓」。
/// <para>
/// 做法是给每条线段生成一个 2r 宽的四边形，再在每个顶点放一个半径 r 的圆盘，
/// 所有子路径统一绕向，于是 NonZero 的并集就是「圆角连接 + 圆头端点」的描边结果。
/// </para>
/// <para>
/// 这样做的代价是轮廓边数约为直线段数的 2 + 圆盘段数（8–64）倍，换来的是不必写第二套
/// 「描边专用光栅化器」——AA、裁剪、笔刷混合全部复用 <see cref="ScanlineRasterizer"/> 那一套。
/// 真正的描边光栅化（按距离场算覆盖率）留给 M7 之后按基准决定。
/// </para>
/// </summary>
internal static class StrokeOutline
{
    /// <summary>圆盘离散化的弦高容差（像素）。0.15 在 8 位覆盖率上已经看不出多边形感。</summary>
    private const float ChordTolerance = 0.15f;

    private const float DegenerateLengthSquared = 1e-6f;

    internal static Path Build(FlattenedPath source, float width)
    {
        float radius = width * 0.5f;
        var builder = new PathBuilder();

        int[] starts = source.ContourStarts;
        PointF[] points = source.Points;

        for (int c = 0; c + 1 < starts.Length; c++)
        {
            int begin = starts[c];
            int end = starts[c + 1];
            if (end - begin < 1)
            {
                continue;
            }

            // CloseFigure 会把起点补到末尾，据此判断子路径是否闭合
            bool closed = end - begin >= 2 && SamePoint(points[begin], points[end - 1]);
            int count = closed ? end - begin - 1 : end - begin;
            int segmentCount = closed ? count : count - 1;

            for (int i = 0; i < segmentCount; i++)
            {
                PointF a = points[begin + i];
                PointF b = points[begin + ((i + 1) % count)];
                AddQuad(builder, a, b, radius);
            }

            // 圆盘同时负责「圆角连接」和「圆头端点」：开放路径的首尾顶点也在内
            for (int i = 0; i < count; i++)
            {
                AddDisk(builder, points[begin + i], radius);
            }
        }

        return builder.Build();
    }

    private static void AddQuad(PathBuilder builder, PointF a, PointF b, float radius)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        float lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared < DegenerateLengthSquared)
        {
            return;
        }

        float inverseLength = 1f / MathF.Sqrt(lengthSquared);
        float nx = -dy * inverseLength * radius;
        float ny = dx * inverseLength * radius;

        Span<PointF> quad = stackalloc PointF[4];
        quad[0] = new PointF(a.X + nx, a.Y + ny);
        quad[1] = new PointF(b.X + nx, b.Y + ny);
        quad[2] = new PointF(b.X - nx, b.Y - ny);
        quad[3] = new PointF(a.X - nx, a.Y - ny);

        AddClosed(builder, quad);
    }

    private static void AddDisk(PathBuilder builder, PointF center, float radius)
    {
        int segments = DiskSegments(radius);
        Span<PointF> ring = stackalloc PointF[segments];

        for (int i = 0; i < segments; i++)
        {
            float angle = MathF.Tau * i / segments;
            ring[i] = new PointF(
                center.X + (MathF.Cos(angle) * radius),
                center.Y + (MathF.Sin(angle) * radius));
        }

        AddClosed(builder, ring);
    }

    /// <summary>按弦高容差选圆盘段数：段数越多越圆，但边数线性增长。</summary>
    private static int DiskSegments(float radius)
    {
        float cosine = 1f - (ChordTolerance / radius);
        if (cosine <= -1f)
        {
            return 8;
        }

        int segments = (int)MathF.Ceiling(MathF.PI / MathF.Acos(cosine));
        return Math.Clamp(segments, 8, 64);
    }

    /// <summary>
    /// 追加一条闭合子路径，并把绕向统一成「正面积」。
    /// <para>
    /// 这一步是正确性的前提：NonZero 下绕向相反的轮廓重叠处环绕数相消，会在描边接缝上挖出洞。
    /// </para>
    /// </summary>
    private static void AddClosed(PathBuilder builder, ReadOnlySpan<PointF> points)
    {
        if (points.Length < 3)
        {
            return;
        }

        float twiceArea = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            PointF current = points[i];
            PointF next = points[(i + 1) % points.Length];
            twiceArea += (current.X * next.Y) - (next.X * current.Y);
        }

        builder.MoveTo(points[0]);

        if (twiceArea >= 0f)
        {
            for (int i = 1; i < points.Length; i++)
            {
                builder.LineTo(points[i]);
            }
        }
        else
        {
            for (int i = points.Length - 1; i >= 1; i--)
            {
                builder.LineTo(points[i]);
            }
        }

        builder.CloseFigure();
    }

    private static bool SamePoint(PointF a, PointF b) => a.X == b.X && a.Y == b.Y;
}
