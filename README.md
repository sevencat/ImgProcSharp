# ImgProcSharp

Drawing for [Sdcb.SimdCodecs](https://www.nuget.org/packages/Sdcb.SimdCodecs): read an image, draw shapes / gradients / strokes / TrueType text on it, write it back as PNG. Pure managed, no native dependency, AOT-compatible.

```csharp
using ImgProcSharp;
using Sdcb.SimdCodecs.Png;

using var image = Image.Load("in.jpg");
image.Mutate(x => x
    .Fill(new SolidBrush(new Color(0, 0, 0, 96)), new RectangularPolygon(0, 0, image.Width, 120))
    .DrawText(new TextOptions { Size = 40, Color = Color.White }, "Hello, 世界", new PointF(32, 48)));

// 图片来源信息顺带带出
Console.WriteLine($"{image.SourceFormat} {image.Width}×{image.Height}, EXIF {image.Orientation}");

// v1 不自带存图：直通像素 + 一行 PngWriter（Sdcb.SimdCodecs 自带，无需额外包）
byte[] straight = new byte[image.Width * image.Height * 4];
image.Canvas.CopyToStraight(straight, image.Width * 4);
File.WriteAllBytes("out.png", PngWriter.Encode(straight, image.Width, image.Height));
```

## Status

`0.1.0` — work in progress, API 尚未稳定。

- 输入：`Sdcb.SimdCodecs` 1.4.0 能解的所有格式（PNG / JPEG / WebP / GIF / BMP / TIFF / QOI / Netpbm）。
- 输出：**本库不提供 `Save`**。需要 PNG 时直接用 `Sdcb.SimdCodecs.Png.PngWriter`（上面四行）；其他格式输出等 0.2（见 `plan.md` §Q）。
- 8 位 RGBA，内部预乘 alpha；不做 ICC 色彩管理（ICC 原样写回 PNG 的 `iCCP`）。
- 绘制：填充（纯色 / 线性渐变 / 径向渐变 / 图案）、描边（Miter/Bevel/Round 连接、Butt/Round/Square 端点、`MiterLimit`、虚线）、`Matrix3x2` 变换（笔刷、线宽与虚线同步跟随）、路径裁剪、TrueType 文字（掩码快路径 + 轮廓慢路径）。
- 文字：内嵌文泉驿微米黑（2 MB gzip），无 kerning / 无 GSUB / 无双向文本 / 无彩色 emoji。
- 还没做（0.2）：混合模式、图层；非等比缩放下用椭圆笔尖（现在取几何平均线宽）。差距表见 `misc.md` §I、§X、§Z、§Y。

## 现在就能用的部分（core + ext，M6/M7 完成）

```csharp
using System.Numerics;
using ImgProcSharp;

using var canvas = Canvas.Create(800, 400);
var ctx = new DrawingContext(canvas);

ctx.Clear(Color.White);
ctx.Fill(new SolidBrush(new Color(30, 30, 30, 200)), new RectangularPolygon(24, 24, 400, 96));
ctx.Fill(Color.Black, new CirclePolygon(600, 200, 80));

// 渐变 + 圆角（半径超过 min(w,h)/2 会夹成胶囊形，弧在构建期转成三次贝塞尔）
ctx.Fill(
    new LinearGradientBrush(
        new PointF(0, 0),
        new PointF(800, 0),
        new ColorStop(0f, Color.Red),
        new ColorStop(1f, Color.Blue)),
    new RoundRectanglePolygon(24, 300, 400, 60, 16));

// 描边：默认圆头端点 + 圆角连接
ctx.Draw(new Pen(Color.Yellow, 6f), new CirclePolygon(160, 200, 60));
ctx.Draw(Color.Black, 1.5f, new RoundRectanglePolygon(24, 300, 400, 60, 16));

// 描边样式：尖角 + 平头端点 / 虚线（图案长度是线宽倍数：4×6 实、4×4 虚）
ctx.Draw(
    new Pen(Color.Yellow, 4f) { LineJoin = LineJoin.Miter, LineCap = LineCap.Butt },
    new PathBuilder().MoveTo(500, 320).LineTo(600, 260).LineTo(700, 320).Build());
ctx.Draw(
    new Pen(Color.Black, 4f) { DashPattern = [6f, 4f] },
    new PathBuilder().MoveTo(500, 360).LineTo(760, 360).Build());

// 快路径：字形掩码直接当 coverage，与形状共用混合内核
ctx.DrawText("Hello, 世界", new PointF(48, 48), new TextOptions { Size = 40, Color = Color.White });

// 慢路径：字形轮廓转成 Path 后走通用填充，方便后续加变换/描边
ctx.FillText(new SolidBrush(Color.Black), "轮廓文字", new PointF(48, 150), new TextOptions { Size = 24, Align = TextAlign.Center, MaxWidth = 400 });

// 变换：不是栈，就是「当前矩阵」；PushTransform 左乘（等价 SVG 的嵌套 transform）
ctx.PushTransform(Matrix3x2.CreateTranslation(600, 120));
ctx.PushTransform(Matrix3x2.CreateRotation(-0.2f));
ctx.Fill(new SolidBrush(Color.Red), new RectangularPolygon(-60, -24, 120, 48));
ctx.ResetTransform();

// 描边文字：轮廓路径自己拿去 Draw；非恒等变换时 DrawText 也会自动走这条慢路径
Path outline = TextDrawing.GetTextPath("outline", new PointF(48, 380), new TextOptions { Size = 48 });
ctx.Draw(new Pen(Color.White, 1.5f), outline);

// 裁剪：真栈，与已有裁剪取交集；填充/描边/文字三条路径都受它约束
ctx.PushClip(new RoundRectanglePolygon(24, 300, 400, 60, 16));
ctx.Fill(new SolidBrush(new Color(255, 255, 255, 80)), new RectangularPolygon(24, 280, 400, 100));
ctx.PopClip();

SizeF measured = TextFont.BuiltIn.MeasureText("Hello, 世界", 40f);
```

变换与裁剪的语义，写在这里免得踩：

- 坐标都在用户坐标系里给，`Fill`/`Draw` 入口一次性把矩阵乘到路径上。线宽跟着缩放（`Width × sqrt(|det|)`，非等比取几何平均）；渐变/图案笔刷也跟随变换，几何（起止点、圆心、瓦片原点）都按用户坐标系理解。
- 描边样式：`LineJoin` 决定顶点外侧怎么补（`Round` 圆盘、`Bevel` 斜切、`Miter` 闭式尖角，尖角超过 `MiterLimit` 自动退回 `Bevel`）；`LineCap` 只作用于开放路径的两端；虚线图案的长度单位是**线宽倍数**，随线宽与变换一起缩放，`DashOffset` 与 SVG 的 `stroke-dashoffset` 同向。
- 变换是「当前矩阵」不是栈：`PushTransform` 只是左乘，配 `SetTransform`/`ResetTransform`。裁剪才是栈：`PushClip`/`PopClip` 必须配对，空栈 `PopClip` 会抛 `InvalidOperationException`。
- 裁剪每层是一张画布大小的 A8 掩码（1080p 约 2 MB），边界带抗锯齿；有裁剪时纯色矩形会退到通用路径。

扩展包另外提供 `Image.Load` / `Create` / `Mutate` / `Clone` / `ApplyOrientation`（net10.0）。`Mutate` 的上下文同样有 `Draw`/`PushClip`/`PushTransform`：

```csharp
image.Mutate(x => x
    .PushClip(new CirclePolygon(image.Width / 2, image.Height / 2, 200))
    .PushTransform(Matrix3x2.CreateTranslation(50, 50))
    .Draw(new Pen(Color.Red, 10f), new PathBuilder().MoveTo(0, 0).LineTo(200, 100).Build())
    .PopClip());
```

每次 `Mutate` 都是一个新的上下文：`Transform`/裁剪不会跨调用保留（`Resize` 例外——它保留变换、丢弃裁剪）。

## 示例、基准、AOT

```pwsh
dotnet run --project samples/ImgProcSharp.Samples
# → output/result.png（960×540：渐变背景、圆角卡片、中英文排版、裁剪条纹带、圆环描边、旋转徽章、描边文字、miter 尖角与虚线）

dotnet run -c Release --project benchmarks/ImgProcSharp.Benchmarks -- --filter "*Stroke*"
# 填充/描边/文字/编解码/端到端；ShortRunJob，十几秒出数，正式数据加 --job default

dotnet run -c Release --project samples/ImgProcSharp.AotSmoke
dotnet publish samples/ImgProcSharp.AotSmoke -c Release -r win-x64
# 两者的输出必须逐字节一致（打印确定性哈希；win-x64 自包含约 9.8 MB）
```

示例不依赖任何素材文件，跑完直接看图；它同时是「裁剪 + 变换 + 描边 + 文字」四条能力的端到端冒烟。AOT smoke 是发布形态的冒烟，基准只是「别把数量级搞错」的工具，都不进 CI 门禁。

## Layout

```
src/ImgProcSharp.Core/               绘图核心（net8.0;net10.0，不发布）
src/ImgProcSharp.SimdCodecs/         唯一打包工程（net10.0，程序集名 ImgProcSharp）
samples/ImgProcSharp.Samples/        示例控制台（net10.0，输出 output/result.png）
samples/ImgProcSharp.AotSmoke/       NativeAOT 冒烟（PublishAot，输出与 JIT 一致）
benchmarks/ImgProcSharp.Benchmarks/  BenchmarkDotNet 基准
tests/                               Core.Tests（net8.0;net10.0）+ SimdCodecs.Tests（net10.0）
```

核心源码通过 `Compile Include` 编进打包程序集，因此只发一个 dll、一个包。

## Docs

- `plan.md` — 设计、里程碑、验收标准。
- `misc.md` — 决策记录、被否决的方案、依赖现状。

## License

Apache-2.0. 内嵌字体 WenQuanYi Micro Hei 为 Apache-2.0（许可见 `licenses/`）。
