using System.Numerics;
using ImgProcSharp;
using Sdcb.SimdCodecs.Jpeg;
using Sdcb.SimdCodecs.Png;

namespace ImgProcSharp.Samples;


/// <summary>
/// 跑出三张图：
/// <list type="bullet">
/// <item><c>output/result.png</c>：纯矢量海报（渐变背景 + 圆角卡片 + 中英文排版 + 描边 + 变换）。</item>
/// <item><c>output/photo.jpg</c>：照片管线——<see cref="Image.Load(string)"/> 解码 JPEG
/// （内部就是 <c>Sdcb.SimdCodecs</c> 的解码门面），在像素上叠标注，再用 <see cref="JpegEncoder"/> 编码回去。</item>
/// <item><c>output/compare.png</c>：原图与处理后的成品左右拼成一张对比图。</item>
/// </list>
/// <para>
/// 编码不在本库范围内：出 PNG 用 <see cref="PngWriter"/>，出 JPEG 用 <see cref="JpegEncoder"/>，都由调用方直接调。
/// 输入优先用 <c>samples/assets/sample.jpg</c>，没有就现场合成一张照片再编码成 JPEG——两条路走同一段解码代码。
/// </para>
/// </summary>
internal static class Program
{
    private const int Width = 960;
    private const int Height = 540;

    private const int PhotoWidth = 1280;
    private const int PhotoHeight = 720;

    private static readonly string[] PhotoAssetCandidates =
    [
        "samples/assets/sample.jpg",
        "assets/sample.jpg",
    ];

    private static void Main()
    {
        Directory.CreateDirectory("output");

        RenderPoster();
        RenderPhoto();
    }

    /// <summary>矢量海报：不读任何素材，纯靠绘图 API 拼出来。</summary>
    private static void RenderPoster()
    {
        using Image image = Image.Create(Width, Height);

        image.Mutate(context =>
        {
            // 背景：斜向渐变
            context.Fill(
                new LinearGradientBrush(
                    new PointF(0, 0),
                    new PointF(Width, Height),
                    new ColorStop(0f, Color.FromRgb(22, 26, 46)),
                    new ColorStop(1f, Color.FromRgb(58, 26, 74))),
                new RectangularPolygon(0, 0, Width, Height));

            // 卡片：半透明底 + 1.5px 描边
            var card = new RoundRectanglePolygon(48, 48, Width - 96, Height - 96, 28);
            context.Fill(new SolidBrush(Color.FromRgba(255, 255, 255, 26)), card);
            context.Draw(new Pen(Color.FromRgba(255, 255, 255, 40), 1.5f), card);

            // 标题：英文用大字号，中文换行
            context.DrawText(
                new TextOptions { Size = 62f, Color = Color.White },
                "ImgProcSharp",
                new PointF(88, 108));

            context.DrawText(
                new TextOptions { Size = 24f, Color = Color.FromRgb(168, 180, 214) },
                "矢量绘图 · 中文排版 · 描边与变换\n纯托管，无原生依赖，AOT 友好",
                new PointF(90, 210));

            // 渐变圆：外圈描边 + 内部渐变填充
            var ring = new CirclePolygon(760, 300, 92);
            context.Fill(
                new RadialGradientBrush(
                    new PointF(760, 300),
                    new SizeF(92f, 92f),
                    new ColorStop(0f, Color.FromRgb(255, 196, 92)),
                    new ColorStop(1f, Color.FromRgb(232, 68, 124))),
                ring);
            context.Draw(new Pen(Color.FromRgba(255, 255, 255, 90), 6f), new CirclePolygon(760, 300, 104));

            // 裁剪 + 图案笔刷：斜纹只在圆角矩形内部出现（PushClip/PopClip 是唯一会入栈的状态）
            using (Canvas tile = Canvas.Create(12, 12, Color.FromRgb(96, 220, 255)))
            {
                for (int y = 0; y < 12; y++)
                {
                    for (int x = 0; x < 12; x++)
                    {
                        if (((x + y) / 3) % 2 == 0)
                        {
                            tile.SetColor(x, y, Color.FromRgb(64, 116, 188));
                        }
                    }
                }

                context.PushClip(new RoundRectanglePolygon(90, 296, 340, 120, 20));
                context.Fill(new PatternBrush(tile, new PointF(90, 296)), new RectangularPolygon(90, 296, 340, 120));
                context.PopClip();
            }

            context.Draw(
                new Pen(Color.FromRgba(255, 255, 255, 70), 1.5f),
                new RoundRectanglePolygon(90, 296, 340, 120, 20));

            // 变换：旋转的徽章（文字跟着一起转）
            context
                .PushTransform(Matrix3x2.CreateTranslation(742, 452))
                .PushTransform(Matrix3x2.CreateRotation(-0.16f))
                .Fill(
                    new LinearGradientBrush(
                        new PointF(-84, 0),
                        new PointF(84, 0),
                        new ColorStop(0f, Color.FromRgb(96, 220, 255)),
                        new ColorStop(1f, Color.FromRgb(120, 255, 190))),
                    new RoundRectanglePolygon(-84, -26, 168, 52, 26))
                .DrawText(
                    new TextOptions { Size = 20f, Color = Color.FromRgb(18, 32, 40), MaxWidth = 168f, Align = TextAlign.Center },
                    "rotate + draw",
                    new PointF(-84, -14))
                .ResetTransform();

            // 描边文字：轮廓路径 + 画笔
            Path strokePath = TextDrawing.GetTextPath(
                "outline",
                new PointF(92, 452),
                new TextOptions { Size = 54f });

            context.Draw(new Pen(Color.FromRgb(190, 180, 255), 1.5f), strokePath);

            // 描边样式：miter 尖角 + 平头端点，以及虚线（图案长度单位是线宽倍数）
            Path chevron = new PathBuilder().MoveTo(486, 398).LineTo(530, 348).LineTo(574, 398).Build();
            context.Draw(
                new Pen(Color.FromRgb(120, 255, 190), 10f)
                {
                    LineJoin = LineJoin.Miter,
                    LineCap = LineCap.Butt,
                },
                chevron);

            context.Draw(
                new Pen(Color.FromRgba(255, 255, 255, 120), 2f)
                {
                    DashPattern = [8f, 5f],
                    LineCap = LineCap.Round,
                },
                new PathBuilder().MoveTo(486, 424).LineTo(630, 424).Build());
        });

        byte[] straight = new byte[image.Width * image.Height * 4];
        image.Canvas.CopyToStraight(straight, image.Width * 4);
        Write(PngWriter.Encode(straight, image.Width, image.Height), "result.png", image.Width, image.Height);
    }

    /// <summary>
    /// 照片管线：JPEG 解码 → 在像素上画渐变/描边/文字 → 编码回 JPEG，最后把原图与成品拼成一张对比图。
    /// </summary>
    private static void RenderPhoto()
    {
        string? asset = Array.Find(PhotoAssetCandidates, File.Exists);
        byte[] source = asset is null ? BuildSamplePhoto() : File.ReadAllBytes(asset);

        using Image photo = Image.Load(source);   // 格式由内容决定，不看扩展名
        photo.ApplyOrientation();                 // 相机竖拍的照片先转正，否则字会跟着歪
        using Image original = photo.Clone();     // 拼图左边那张：解码后、未绘制的原始像素

        Console.WriteLine(
            $"loaded {asset?.Replace('\\', '/') ?? "<generated>"}: {photo.Width}×{photo.Height} " +
            $"{photo.SourceFormat}, {source.Length:N0} B, orientation={photo.Orientation}, icc={photo.IccProfile.Length} B");

        int width = photo.Width;
        int height = photo.Height;
        float unit = width / 1280f;               // 标注全部按宽度缩放，换素材也不会错位

        photo.Mutate(context =>
        {
            // 1) 顶部压暗：透明→黑的线性渐变，保证白字压在亮天空上也读得清
            float band = height * 0.34f;
            context.Fill(
                new LinearGradientBrush(
                    new PointF(0, 0),
                    new PointF(0, band),
                    new ColorStop(0f, Color.FromRgba(4, 8, 16, 205)),
                    new ColorStop(1f, Color.FromRgba(4, 8, 16, 0))),
                new RectangularPolygon(0, 0, width, band));

            // 2) 标题与副标题（中文走内嵌文泉驿微米黑，不需要系统装字体）
            context.DrawText(
                new TextOptions { Size = 54f * unit, Color = Color.White },
                "JPEG → 绘制 → JPEG",
                new PointF(48f * unit, 38f * unit));

            context.DrawText(
                new TextOptions { Size = 22f * unit, Color = Color.FromRgb(206, 218, 236) },
                "Sdcb.SimdCodecs 解码，渐变 / 描边 / 文字叠在像素上，再编码回去",
                new PointF(50f * unit, 106f * unit));

            // 3) 目标高亮：虚线圆 + 引线 + 标签（虚线图案与端帽直接作用在照片上）
            var target = new PointF(width * 0.68f, height * 0.30f);
            float radius = 132f * unit;
            context.Draw(
                new Pen(Color.FromRgba(255, 255, 255, 210), 3f * unit)
                {
                    DashPattern = [10f, 7f],
                    LineCap = LineCap.Round,
                },
                new CirclePolygon(target, radius));

            context.Draw(
                new Pen(Color.FromRgba(255, 255, 255, 200), 2f * unit),
                new PathBuilder()
                    .MoveTo(target.X + (radius * 0.72f), target.Y - (radius * 0.72f))
                    .LineTo(target.X + (radius * 1.45f), target.Y - (radius * 1.30f))
                    .LineTo(target.X + (radius * 2.30f), target.Y - (radius * 1.30f))
                    .Build());

            context.DrawText(
                new TextOptions { Size = 20f * unit, Color = Color.White },
                "叠加在解码后的像素上",
                new PointF(target.X + (radius * 1.45f), target.Y - (radius * 1.52f)));

            // 4) 左下信息卡：半透明圆角底 + 细描边
            var card = new RoundRectanglePolygon(48f * unit, height - (168f * unit), 430f * unit, 112f * unit, 22f * unit);
            context.Fill(new SolidBrush(Color.FromRgba(12, 18, 30, 170)), card);
            context.Draw(new Pen(Color.FromRgba(255, 255, 255, 70), 1.5f * unit), card);

            context.DrawText(
                new TextOptions { Size = 24f * unit, Color = Color.FromRgb(150, 226, 255) },
                "ImgProcSharp on photo",
                new PointF(74f * unit, height - (142f * unit)));

            context.DrawText(
                new TextOptions { Size = 18f * unit, Color = Color.FromRgb(226, 232, 240) },
                "纯托管绘制：无 Skia、无原生依赖、AOT 友好",
                new PointF(74f * unit, height - (104f * unit)));

            // 5) 右下徽章：旋转的渐变块，文字跟着一起转
            context
                .PushTransform(Matrix3x2.CreateTranslation(width - (208f * unit), height - (82f * unit)))
                .PushTransform(Matrix3x2.CreateRotation(-0.07f))
                .Fill(
                    new LinearGradientBrush(
                        new PointF(-150f, 0),
                        new PointF(150f, 0),
                        new ColorStop(0f, Color.FromRgb(96, 220, 255)),
                        new ColorStop(1f, Color.FromRgb(120, 255, 190))),
                    new RoundRectanglePolygon(-150f, -34f, 300f, 68f, 34f))
                .DrawText(
                    new TextOptions { Size = 22f * unit, Color = Color.FromRgb(10, 26, 34), MaxWidth = 300f, Align = TextAlign.Center },
                    "decode · draw · encode",
                    new PointF(-150f, -14f))
                .ResetTransform();

            // 6) 暗角：中心透明的径向渐变盖到四角，让标注区更聚焦
            context.Fill(
                new RadialGradientBrush(
                    new PointF(width * 0.5f, height * 0.5f),
                    new SizeF(width * 0.62f, height * 0.62f),
                    new ColorStop(0.55f, Color.FromRgba(0, 0, 0, 0)),
                    new ColorStop(1f, Color.FromRgba(0, 0, 0, 120))),
                new RectangularPolygon(0, 0, width, height));
        });

        byte[] straight = new byte[width * height * 4];
        photo.Canvas.CopyToStraight(straight, width * 4);

        // JPEG 没有 alpha：直通像素按 4 通道喂进去，编码器忽略 alpha。
        // ICC 原样带上，Display P3 / Adobe RGB 的照片重新编码后色彩空间不丢。
        byte[] jpeg = JpegEncoder.Encode(straight, width, height, 4, new JpegEncodeOptions
        {
            Quality = 92,
            OptimizeHuffman = true,
            IccProfile = photo.IccProfile,
        });

        Write(jpeg, "photo.jpg", width, height);
        RenderComparison(
            original,
            photo,
            $"原图 · {photo.SourceFormat} · {source.Length:N0} B",
            $"处理后 · JPEG q92 · {jpeg.Length:N0} B");
    }

    /// <summary>
    /// 把「原图」与「处理后」左右拼成一张对比图（顶部标签条 + 中间分隔线 + 外框），输出 <c>output/compare.png</c>。
    /// <para>
    /// 拼图这层没有「叠加一张图」的 API（本库没有 image brush），所以直接用 <see cref="Canvas.GetRow"/> 逐行搬字节：
    /// 三张画布都是预乘 Rgba32，行内像素可以原样拷贝，不用解预乘再乘回去。
    /// </para>
    /// </summary>
    private static void RenderComparison(Image before, Image after, string leftLabel, string rightLabel)
    {
        const int Gap = 20;
        const int LabelHeight = 64;

        using Image sheet = Image.Create(
            (before.Width * 2) + Gap,
            before.Height + LabelHeight,
            Color.FromRgb(16, 20, 28));

        for (int y = 0; y < before.Height; y++)
        {
            Span<byte> row = sheet.Canvas.GetRow(LabelHeight + y);
            before.Canvas.GetRow(y).CopyTo(row[..(before.Width * 4)]);
            after.Canvas.GetRow(y).CopyTo(row[((before.Width + Gap) * 4)..]);
        }

        sheet.Mutate(context =>
        {
            context.DrawText(
                new TextOptions { Size = 26f, Color = Color.FromRgb(196, 208, 228) },
                leftLabel,
                new PointF(24, 18));

            context.DrawText(
                new TextOptions { Size = 26f, Color = Color.FromRgb(140, 224, 255) },
                rightLabel,
                new PointF(before.Width + Gap + 24f, 18));

            // 中间分隔线：笔宽正好填满 gap
            context.Draw(
                new Pen(Color.FromRgb(58, 68, 92), Gap),
                new PathBuilder()
                    .MoveTo(before.Width + (Gap / 2f), 0)
                    .LineTo(before.Width + (Gap / 2f), sheet.Height)
                    .Build());

            context.Draw(
                new Pen(Color.FromRgb(48, 56, 78), 1f),
                new RectangularPolygon(0.5f, 0.5f, sheet.Width - 1f, sheet.Height - 1f));
        });

        byte[] straight = new byte[sheet.Width * sheet.Height * 4];
        sheet.Canvas.CopyToStraight(straight, sheet.Width * 4);
        Write(PngWriter.Encode(straight, sheet.Width, sheet.Height), "compare.png", sheet.Width, sheet.Height);

        // README 首页展示的就是这张：JPEG 体积小，适合放进仓库当预览图
        Write(
            JpegEncoder.Encode(straight, sheet.Width, sheet.Height, 4, new JpegEncodeOptions
            {
                Quality = 90,
                OptimizeHuffman = true,
            }),
            "compare.jpg",
            sheet.Width,
            sheet.Height);
    }

    /// <summary>
    /// 合成一张「照片」并编码成 JPEG 当输入：示例不依赖任何素材文件，
    /// 但走的仍是「真 JPEG 字节 → 解码 → 绘制 → 编码」这条路（见 misc §M）。
    /// </summary>
    private static byte[] BuildSamplePhoto()
    {
        using Image scene = Image.Create(PhotoWidth, PhotoHeight, Color.FromRgb(20, 26, 42));

        scene.Mutate(context =>
        {
            var sun = new PointF(PhotoWidth * 0.68f, PhotoHeight * 0.30f);

            // 天空：上冷下暖的三段渐变
            context.Fill(
                new LinearGradientBrush(
                    new PointF(0, 0),
                    new PointF(0, PhotoHeight * 0.72f),
                    new ColorStop(0f, Color.FromRgb(44, 84, 156)),
                    new ColorStop(0.55f, Color.FromRgb(178, 202, 224)),
                    new ColorStop(1f, Color.FromRgb(246, 198, 138))),
                new RectangularPolygon(0, 0, PhotoWidth, PhotoHeight * 0.72f));

            // 太阳与光晕：径向渐变（外圈 alpha 到 0，边缘不会出现硬圈）
            context.Fill(
                new RadialGradientBrush(
                    sun,
                    new SizeF(260f, 260f),
                    new ColorStop(0f, Color.FromRgba(255, 250, 226, 255)),
                    new ColorStop(0.28f, Color.FromRgba(255, 220, 150, 168)),
                    new ColorStop(1f, Color.FromRgba(255, 190, 100, 0))),
                new CirclePolygon(sun, 260f));

            // 两层山脊：贝塞尔曲线 + 竖向渐变，模拟远山与近岸
            context.Fill(
                new LinearGradientBrush(
                    new PointF(0, PhotoHeight * 0.42f),
                    new PointF(0, PhotoHeight),
                    new ColorStop(0f, Color.FromRgb(118, 136, 164)),
                    new ColorStop(1f, Color.FromRgb(52, 66, 92))),
                new PathBuilder()
                    .MoveTo(-40, PhotoHeight * 0.62f)
                    .CubicTo(220, PhotoHeight * 0.44f, 420, PhotoHeight * 0.74f, 700, PhotoHeight * 0.56f)
                    .CubicTo(940, PhotoHeight * 0.42f, 1100, PhotoHeight * 0.6f, PhotoWidth + 40, PhotoHeight * 0.5f)
                    .LineTo(PhotoWidth + 40, PhotoHeight + 40)
                    .LineTo(-40, PhotoHeight + 40)
                    .CloseFigure()
                    .Build());

            context.Fill(
                new LinearGradientBrush(
                    new PointF(0, PhotoHeight * 0.6f),
                    new PointF(0, PhotoHeight),
                    new ColorStop(0f, Color.FromRgb(60, 96, 96)),
                    new ColorStop(1f, Color.FromRgb(16, 40, 46))),
                new PathBuilder()
                    .MoveTo(-40, PhotoHeight * 0.78f)
                    .CubicTo(300, PhotoHeight * 0.66f, 620, PhotoHeight * 0.9f, 980, PhotoHeight * 0.72f)
                    .CubicTo(1120, PhotoHeight * 0.65f, 1220, PhotoHeight * 0.7f, PhotoWidth + 40, PhotoHeight * 0.68f)
                    .LineTo(PhotoWidth + 40, PhotoHeight + 40)
                    .LineTo(-40, PhotoHeight + 40)
                    .CloseFigure()
                    .Build());

            // 近景水面：半透明图案笔刷做波光（瓦片按取模平铺）
            using (Canvas wave = Canvas.Create(36, 9, Color.FromRgba(0, 0, 0, 0)))
            {
                for (int i = 0; i < 4; i++)
                {
                    wave.SetColor(6 + (i * 8), 2 + i, Color.FromRgba(190, 232, 244, (byte)(120 - (i * 22))));
                    wave.SetColor(10 + (i * 8), 5 + (i % 2), Color.FromRgba(226, 246, 255, (byte)(96 - (i * 18))));
                }

                context.Fill(
                    new PatternBrush(wave, new PointF(0, PhotoHeight * 0.86f)),
                    new RectangularPolygon(0, PhotoHeight * 0.86f, PhotoWidth, PhotoHeight * 0.14f));
            }

        });

        byte[] straight = new byte[PhotoWidth * PhotoHeight * 4];
        scene.Canvas.CopyToStraight(straight, PhotoWidth * 4);

        return JpegEncoder.Encode(straight, PhotoWidth, PhotoHeight, 4, new JpegEncodeOptions
        {
            Quality = 88,
            OptimizeHuffman = true,
        });
    }

    private static void Write(byte[] encoded, string fileName, int width, int height)
    {
        string path = System.IO.Path.Combine("output", fileName);
        File.WriteAllBytes(path, encoded);
        Console.WriteLine($"wrote {path} ({width}×{height}, {encoded.Length:N0} bytes)");
    }
}
