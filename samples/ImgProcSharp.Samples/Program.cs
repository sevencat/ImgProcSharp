using System.Numerics;
using ImgProcSharp;
using Sdcb.SimdCodecs.Png;

namespace ImgProcSharp.Samples;


/// <summary>
/// 生成 <c>output/result.png</c>：渐变背景 + 圆角卡片 + 中英文排版 + 描边 + 变换。
/// <para>
/// 这张图用肉眼就能检查抗锯齿、渐变脏边、中文缺字、描边接缝、旋转文字的锯齿。
/// 编码只用 <see cref="PngWriter"/>（调用方自己拿直通像素），本库不提供 <c>Save</c>。
/// </para>
/// </summary>
internal static class Program
{
    private const int Width = 960;
    private const int Height = 540;

    private static void Main()
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

        Directory.CreateDirectory("output");
        byte[] straight = new byte[image.Width * image.Height * 4];
        image.Canvas.CopyToStraight(straight, image.Width * 4);

        string path = System.IO.Path.Combine("output", "result.png");
        File.WriteAllBytes(path, PngWriter.Encode(straight, image.Width, image.Height));

        Console.WriteLine($"wrote {path} ({image.Width}×{image.Height}, {new FileInfo(path).Length:N0} bytes)");
    }
}
