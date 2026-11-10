# ImgProcSharp 计划

目标：给 `Sdcb.SimdCodecs` 补一个「画」的能力——读一张图，在图上画形状、渐变、文字，再存成另一张图。字体渲染直接采用 `F:\code\zlmsharp\src\GlyphRaster` 的代码；绘图核心不依赖任何编解码库，编解码只由外层薄门面负责。

**输入**用 `Sdcb.SimdCodecs` 的格式包（NuGet 2.0.0，PNG/JPEG/WebP/GIF/BMP/TIFF/QOI/Netpbm/ICO/JPEG 2000/JBIG2 各一个包）。本库自身只依赖契约层 `Sdcb.SimdCodecs.Abstractions`，能读写哪些格式由调用方注册编解码器决定（`CodecRegistry.Register(...)` / `EncoderRegistry.Register(...)`，0.2.1 起）。**v1 不自研编码器**：公开 API 只有 `Image.Save` / `Canvas.Save` / `Encode` 这组扩展方法，内部调 `ImageEncoderRegistration` 的委托，PNG/JPEG 的实现仍来自调用方引用的格式包。

决策、许可、算法笔记、未决问题等统统在 [`misc.md`](misc.md)，本文只放计划本身。

---

## 1. 目标与非目标

### 交付的能力

1. **画布**：从解码后的像素建画布，或从零创建；支持 alpha 合成。
2. **图形**：矩形、圆角矩形、椭圆/圆、多边形、折线、任意贝塞尔路径；填充与描边；纯色、线性渐变、径向渐变、图案笔刷。
3. **文字**：中文/英文按字号绘制，可换行、可测量、可描边；字体直接复用 GlyphRaster（含内嵌文泉驿微米黑，免配置可用）。
4. **解码 → 绘制 → 导出**：`Image.Load("in.jpg")` → `Mutate(...)` 画 → `image.Save("out.png")`（0.2.1 起的扩展方法，底下调已注册的编码器）。输入输出格式都随调用方注册的格式包。

### 非目标（v1 明确不做）

- **编码器实现**：本库不自研编码器，也不引用任何格式实现包；公开 API 里只有「按格式查 `EncoderRegistry` 并调 `Abstractions` 的编码契约」的扩展方法（`Image.Save` / `Canvas.Save` / `Encode`，0.2.1 起），具体性能与压缩率由调用方登记的子包决定。WebP 等其他输出等 0.4 再议（misc §Q）。
- 色彩管理：不做 ICC/primaries 转换，解码按「display tier」8 位 RGBA 直接画（见 misc §K）。
- 文字的复杂排版：无 kerning、无 GSUB/整形、无双向文本、无彩色 emoji（GlyphRaster 现状，见 misc §I）。
- 动画/多帧编辑：只处理首帧。
- SVG 解析、字体族回退链、可变字体、混合模式（Multiply/Screen 等）。
- 与 ImageSharp.Drawing 的 API 兼容：只借「Fill / Draw / Mutate」这种形状，实现全部自研（clean-room，见 misc §A）。

---

## 2. 交付物

| 产物 | 说明 |
|---|---|
| `ImgProcSharp`（NuGet，net10.0） | 唯一对外发布的包。内含绘图核心源码 + GlyphRaster 源码 + 内嵌字体 + 解码桥；只依赖 `Sdcb.SimdCodecs.Abstractions`（解码契约），格式实现与编码器都不进包 |
| `ImgProcSharp.Core`（net8.0;net10.0） | 开发与测试用工程，`IsPackable=false`，不单独发布（方案 A） |
| `samples/ImgProcSharp.Samples` | 控制台示例：矢量海报 → `output/result.png`；照片管线（JPEG 解码 → 标注 → 编码）→ `output/photo.jpg` + 原图/成品对比拼图 `output/compare.png|jpg`（JPG 版复制到 `docs/compare.jpg` 作 README 首页图） |
| `tests/ImgProcSharp.Core.Tests`（net8+net10） | 几何、光栅化、混合、文字 |
| `tests/ImgProcSharp.SimdCodecs.Tests`（net10） | 端到端解码 → 绘制：像素保真、格式识别、Exif 方向、链式 `Mutate`（PNG 素材用 `PngWriter` 现造） |

---

## 3. 仓库结构

```
f:/code/ImgProcSharp/
  ImgProcSharp.slnx
  Directory.Build.props            # 公共属性：Nullable/ImplicitUsings/RootNamespace/AOT 分析器
  Directory.Packages.props         # 集中包版本（CPM：Sdcb.SimdCodecs、xunit）
  .editorconfig  .gitignore
  LICENSE                          # Apache-2.0（已有）
  README.md
  plan.md  misc.md
  src/
    ImgProcSharp.Core/             # net8.0;net10.0，IsPackable=false
      Canvas/                      # Canvas, Canvas.Resize, CanvasPool, PixelOps(转换)
      Colors/                      # Color, ColorStop, Blend
      Primitives/                  # PointF, RectF, Size, SizeF, Rectangle, Circle
      Resampling/                  # IResampler, KnownResamplers, ResizeOptions, Resizer
      Paths/                       # IPath, Path, PathBuilder, PathFlattener, GlyphPathBridge
      Rasterization/               # ScanlineRasterizer, CoverageBuffer, FillRule, RowBlender
      Brushes/                     # IBrush, SolidBrush, LinearGradientBrush, RadialGradientBrush, PatternBrush
      Pens/                        # IPen, Pen, LineJoin/LineCap, StrokeStyle, StrokeOutline
      Text/                        # GlyphRaster 引擎（全 internal）、TextOptions/TextDrawing/GlyphPathBridge（见 §6.7）
        TrueTypeFont.cs … TextPainter.cs
        Fonts/WenQuanYiMicroHei.ttf.gz
        Fonts/LICENSE-WenQuanYiMicroHei.txt
      TextFont.cs                  # 文字门面（public，根命名空间）
      TextFont.Layout.cs
      Drawing/                     # DrawingContext（含裁剪掩码栈）, DrawExtensions, TextExtensions
    ImgProcSharp.SimdCodecs/       # net10.0，唯一打包工程
      ImgProcSharp.SimdCodecs.csproj
      Image.cs                     # 门面：Load/Create/Mutate/Clone/ApplyOrientation
      ImageDecoderBridge.cs        # 唯一接触 SimdCodecs 的文件：解码 → Canvas
      IImageProcessingContext.cs   # 链式处理接口（Resize/Clear/Fill/DrawText）
      ImageProcessingContext.cs    # 实现：Resize 换画布，其余转给 DrawingContext
  tests/
    ImgProcSharp.Core.Tests/       # net8.0;net10.0，内核与算法
    ImgProcSharp.SimdCodecs.Tests/ # net10.0，端到端（M4 起）
  samples/
    ImgProcSharp.Samples/          # net10.0 示例，输出 result.png + photo.jpg + compare.png/jpg
    ImgProcSharp.AotSmoke/         # net10.0 + PublishAot，输出必须与 JIT 逐字节一致
  benchmarks/
    ImgProcSharp.Benchmarks/       # BenchmarkDotNet（填充/描边/文字/编解码）
  docs/
    compare.jpg                    # README 首页预览图（示例 output/compare.jpg 的副本）
```

已完成（截至 M7 + 0.2 渐变优化，Core 199 个用例 ×2 目标框架 + ext 16 个用例，全绿）：

```
src/ImgProcSharp.Core/Colors/Color.cs                    预乘/直通、Blend、Div255
src/ImgProcSharp.Core/Canvas/PixelOps.cs                 预乘/反预乘、ToBgra32/ToRgb24/ToGray8
src/ImgProcSharp.Core/Canvas/Canvas.cs                   Create/Wrap/GetRow/Clear/Clone/翻转/旋转/导出
src/ImgProcSharp.Core/Canvas/Canvas.Resize.cs            Resize 六个模式
src/ImgProcSharp.Core/Primitives/{PointF,Size,Rectangle}.cs   几何基础类型
src/ImgProcSharp.Core/Primitives/{RectangularPolygon,Polygon,RoundRectanglePolygon}.cs  图元（EllipsePolygon/CirclePolygon 在 Polygon.cs）
src/ImgProcSharp.Core/Paths/{IPath,Path,PathBuilder,PathBuilder.Arc,PathFlattener}.cs  含 SVG 端点弧
src/ImgProcSharp.Core/Resampling/{IResampler,ResizeOptions,Resizer}.cs
src/ImgProcSharp.Core/Rasterization/{FillRule,CoverageBuffer,ScanlineRasterizer,RectangleFill,RowBlender}.cs
src/ImgProcSharp.Core/Brushes/{IBrush,GradientBrush,PatternBrush}.cs   纯色/线性/径向/图案笔刷
src/ImgProcSharp.Core/Pens/{IPen,LineJoin,LineCap,StrokeStyle,StrokeOutline}.cs  画笔 + 描边轮廓
src/ImgProcSharp.Core/Drawing/DrawingContext.cs          Fill/Draw/Clear + 变换 + 裁剪掩码栈（BlendCoverage 给字形掩码复用）
src/ImgProcSharp.Core/Text/{TextFont,TextFont.Layout}.cs 公开门面（Layout/Measure/Advance）
src/ImgProcSharp.Core/Text/{TextOptions,TextAlign,TextDrawing}.cs  DrawText/FillText/MeasureText
src/ImgProcSharp.Core/Text/GlyphPathBridge.cs            字形轮廓 → 核心 Path
src/ImgProcSharp.Core/Text/{TrueTypeFont*,Glyph*,PathCollection,EmbeddedFonts,TextLayout}.cs  引擎（11 个，全 internal）
src/ImgProcSharp.Core/Text/Fonts/WenQuanYiMicroHei.ttf.gz  内置字体（2 MB）
src/ImgProcSharp.SimdCodecs/Image.cs                     Image 门面（Load/Create/Mutate/Clone/ApplyOrientation）
src/ImgProcSharp.SimdCodecs/ImageDecoderBridge.cs         唯一接触 SimdCodecs 的文件
src/ImgProcSharp.SimdCodecs/IImageProcessingContext.cs    链式接口
src/ImgProcSharp.SimdCodecs/ImageProcessingContext.cs     实现（Resize 换画布）
tests/ImgProcSharp.Core.Tests/StrokeAndTransformTests.cs  描边/变换/文字轮廓
tests/ImgProcSharp.Core.Tests/StrokeStyleTests.cs         连接/端帽/MiterLimit/虚线/画笔校验
tests/ImgProcSharp.Core.Tests/ClipTests.cs                裁剪掩码（嵌套/描边/文字/AA/变换）
tests/ImgProcSharp.Core.Tests/BrushTransformTests.cs       渐变/图案笔刷跟随变换
tests/ImgProcSharp.SimdCodecs.Tests/ImageTests.cs         解码保真/方向/链式/Mutate 绘制与裁剪
samples/ImgProcSharp.Samples/Program.cs                  海报（渐变+卡片+裁剪条纹+描边+旋转+尖角+虚线）+ 照片管线（JPEG 编解码）
samples/ImgProcSharp.AotSmoke/Program.cs                   AOT 冒烟：裁剪+变换+文字+PNG 往返，打印确定性哈希
benchmarks/ImgProcSharp.Benchmarks/Program.cs              BDN：填充/描边/文字/编解码/端到端
```

---

## 4. 工程配置

### 4.1 核心工程（不发布）

```xml
<!-- src/ImgProcSharp.Core/ImgProcSharp.Core.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <LangVersion>12.0</LangVersion>          <!-- 与 GlyphRaster 对齐，保证 net8 也编得过 -->
    <IsPackable>false</IsPackable>           <!-- 只作为源码载体，永不发布 -->
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="Text\Fonts\WenQuanYiMicroHei.ttf.gz"
                      LogicalName="ImgProcSharp.Text.Fonts.WenQuanYiMicroHei.ttf.gz" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="ImgProcSharp.Core.Tests" />
  </ItemGroup>
</Project>
```

`ImplicitUsings` / `Nullable` / `AllowUnsafeBlocks` / `IsAotCompatible` / `RootNamespace` / `GenerateDocumentationFile` / `NoWarn=CS1591` 统一放在 `Directory.Build.props`，两个工程共用。

### 4.2 打包工程（唯一对外）

```xml
<!-- src/ImgProcSharp.SimdCodecs/ImgProcSharp.SimdCodecs.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>ImgProcSharp</AssemblyName>
    <RootNamespace>ImgProcSharp</RootNamespace>
    <PackageId>Sevencat.ImgproSharp</PackageId>
    <Version>0.2.0</Version>
    <Authors>Sevencat</Authors>
    <Description>Drawing for Sdcb.SimdCodecs: shapes, gradients, strokes and TrueType text over decoded pixels, with no native dependency. AOT-compatible.</Description>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageTags>drawing;graphics;skia-free;image;simd;decoding</PackageTags>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <IncludeBuildOutput>true</IncludeBuildOutput>
  </PropertyGroup>

  <!-- 核心源码源内嵌：编进本程序集，而不是引用 dll -->
  <ItemGroup>
    <Compile Include="..\ImgProcSharp.Core\**\*.cs"
             Exclude="..\ImgProcSharp.Core\bin\**;..\ImgProcSharp.Core\obj\**"
             Link="Core\%(RecursiveDir)%(Filename)%(Extension)" />
    <EmbeddedResource Include="..\ImgProcSharp.Core\Text\Fonts\WenQuanYiMicroHei.ttf.gz"
                      LogicalName="ImgProcSharp.Text.Fonts.WenQuanYiMicroHei.ttf.gz"
                      Link="Core\Text\Fonts\WenQuanYiMicroHei.ttf.gz" />
    <None Include="..\ImgProcSharp.Core\Text\Fonts\LICENSE-WenQuanYiMicroHei.txt"
          Pack="true" PackagePath="licenses" Visible="false" />
    <None Include="..\..\README.md" Pack="true" PackagePath="\" Visible="false" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="ImgProcSharp.SimdCodecs.Tests" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Sdcb.SimdCodecs.Abstractions" />   <!-- 只引契约层；版本在 Directory.Packages.props（CPM） -->
  </ItemGroup>
</Project>
```

**要点**

- 绝不写 `ProjectReference` 指向核心工程——那就是「引用 dll」了。`Compile Include` 才是把源码编进本程序集的正确做法。
- 同一批 .cs 会在 net8 与 net10 两次构建里各编一份，`#if NET10_0_OR_GREATER` 天然分叉。
- `Sdcb.SimdCodecs.*` 固定 **2.0.0**（2026-11 已发版）。本库只引 `Abstractions`，解码只经 `ImageDecoderBridge.cs` + `CodecRegistry`；输出由调用方走子包的 `PngWriter`/`JpegEncoder`（见 misc §B / §Q）。
- 开 `ManagePackageVersionsCentrally`，包版本只在 `Directory.Packages.props` 出现一次；`PackageReference` 上不写 `Version`（写了会报 NU1008）。
- 嵌入资源不会随 `Compile` 一起带过来，必须再引一次，且 `LogicalName` 与代码里的常量严格一致（编译期查不出来）。
- 核心工程与打包工程的程序集名必须不同（`ImgProcSharp.Core` vs `ImgProcSharp`），且核心工程永不发布，避免消费者同时拿到两份同名类型（`CS0433`）。

### 4.3 示例工程

```xml
<!-- samples/ImgProcSharp.Samples/ImgProcSharp.Samples.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ImgProcSharp.SimdCodecs\ImgProcSharp.SimdCodecs.csproj" />
  </ItemGroup>
</Project>
```

`Program.cs` 不依赖任何素材文件，跑两条流水线：

1. `RenderPoster()`——整张图由代码画出来（渐变背景 + 圆角卡片 + 中英文排版 + 裁剪条纹带 + 圆环描边 + 旋转徽章 + 描边文字 + miter 尖角 + 虚线），`PngWriter` 写到 `output/result.png`。
2. `RenderPhoto()`——`BuildSamplePhoto()` 合成一张 1280×720 的照片并用 `JpegEncoder` 编码成真 JPEG 字节（存在 `samples/assets/sample.jpg` 时优先读它），`Image.Load` 解码 → 顶部压暗渐变 / 虚线高亮 / 引线 / 信息卡 / 旋转徽章 / 暗角 → `Canvas.CopyToStraight` 后 `JpegEncoder.Encode` 写 `output/photo.jpg`。
3. `RenderComparison()`——解码后先 `Image.Clone()` 留一份原图，处理完用 `Canvas.GetRow` 把两张图逐行拷进一张 2580×784 的拼图（顶部标签条 + 中间分隔线 + 外框，行内预乘像素直接搬），`PngWriter` 写 `output/compare.png`、`JpegEncoder` 写 `output/compare.jpg`（README 首页那张即它的副本 `docs/compare.jpg`），一眼就能看出哪些是画上去的。

`dotnet run --project samples/ImgProcSharp.Samples` 一条命令即可复现，肉眼就能验 AA、渐变脏边、中文缺字、描边接缝、miter 尖角与虚线、旋转文字锯齿、裁剪边界，以及「解码 → 绘制 → 编码」整条链。

另外两个同级工程：`samples/ImgProcSharp.AotSmoke`（`PublishAot=true`，打印确定性哈希，JIT 与 AOT 必须逐字节一致）与 `benchmarks/ImgProcSharp.Benchmarks`（BenchmarkDotNet，`--filter`/`--job` 照常）。

---

## 5. 分层与数据流

```
Image.Load("in.jpg")                              [ext: ImageDecoderBridge]
  ├─ CodecRegistry.Find(data) → 命中的 ImageDecoderRegistration（未注册则抛 InvalidDataException）
  ├─ TryReadInfo → 尺寸/Format/Orientation
  ├─ TryReadMetadata → ICC（任何格式都只扫元数据段，不碰像素）
  ├─ 默认：registration.TryDecodeInto(画布缓冲, Rgba32) → 带 alpha 才 premultiply
  │    └─ 文件路径：ByteSource.FromFile(path) 内存映射 → 同一条路径
  └─ 回退：TryDecode/TryCopyTo → 逐行转换进 Canvas → premultiply
  └─ Canvas(预乘 Rgba32) → Image

image.Mutate(x => x.Resize(...).Fill(brush, path).DrawText(options, text, at))   [ext → core]
  └─ DrawingContext: clip → 路径 flatten → ScanlineRasterizer → A8 coverage
  └─ brush 采样 + coverage 混合（按行，标量 + SIMD）

image.Save("out.png") / image.Encode(ImageFormat.Jpeg, options)   [ext: ImageExportExtensions]
  ├─ EncoderRegistry.Require(format) → ImageEncoderRegistration（未注册则抛 NotSupportedException）
  ├─ encoder.Create(ArrayBufferWriter, options) → AddFrame(w, h, Rgba32Premultiplied, metadata)
  ├─ WriteRows(canvas.Buffer, height, canvas.Stride)   ← 预乘→直通由编码器内部完成，无临时数组
  └─ Commit → 字节写入 MemoryStream / FileStream
```

---

## 6. 核心设计

### 6.1 Canvas

```csharp
public sealed class Canvas : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }                 // 默认 Width * 4，可外部指定（对接解码缓冲）
    public byte[] Buffer { get; }              // Rgba32，预乘 alpha
    public bool IsPremultiplied => true;

    public static Canvas Create(int width, int height, Color? background = null);
    public static Canvas Wrap(byte[] buffer, int width, int height, int stride);  // 不拥有 buffer

    public Span<byte> GetRow(int y);
    public Span<byte> GetPixel(int x, int y);   // 4 字节
    public Color GetColor(int x, int y);
    public void SetColor(int x, int y, Color straight);

    public void Clear(Color? color = null);
    public Canvas Clone();
    public void CopyToStraight(Span<byte> destination, int stride);   // 输出用
    public void CopyToPremultiplied(Span<byte> destination, int stride);
    public void FlipHorizontal(); FlipVertical(); Rotate90(); Rotate180(); Rotate270();

    // 缩放：接口形态对齐 ImageSharp 的 ResizeProcessor，返回新画布
    public Canvas Resize(int width, int height);                      // Stretch
    public Canvas Resize(int width, int height, IResampler sampler);  // Stretch
    public Canvas Resize(Size size);
    public Canvas Resize(Size size, IResampler sampler);
    public Canvas Resize(ResizeOptions options);                      // Crop/Pad/BoxPad/Max/Min/Stretch
}
```

- 内部统一 **预乘 Rgba32**（理由与公式见 misc §F）：`over` 退化成逐通道 `src + dst * (1 - sa)`，无除法、无 alpha=0 特例。
- 边界转换（straight ↔ premultiplied、Rgba32 ↔ Bgra32/Rgb24/Gray8）集中在 `PixelOps`，由单元测试覆盖。

**缩放（`Resampling/`）**

```csharp
public enum ResizeMode { Crop, Pad, BoxPad, Max, Min, Stretch }

public sealed class ResizeOptions
{
    public Size Size { get; set; }              // 目标框；Max/Min 下是上限
    public ResizeMode Mode { get; set; } = ResizeMode.Max;
    public IResampler Sampler { get; set; } = KnownResamplers.Bicubic;
    public bool Compand { get; set; }           // v1 只支持 false，true 抛 NotSupportedException
    public Color PadColor { get; set; } = Color.Transparent;
}

public interface IResampler { float Radius { get; } float GetValue(float x); }
public static class KnownResamplers   // NearestNeighbour / Box / Bilinear / Bicubic / CatmullRom /
{ }                                   // MitchellNetravali / Lanczos2 / Lanczos3
```

- 与 ImageSharp 的对应关系：`Size`/`SizeF`/`ResizeMode`/`IResampler`/`KnownResamplers`/`ResizeOptions` 同名同义；`Canvas.Resize(...)` 对应 `ctx.Resize(...)`，只是返回新画布而不是原地替换（`Canvas` 尺寸不可变）。
- **插值只在预乘空间做**：直通空间对半透明像素插值会出现黑边/色边，这是内部预乘的直接收益。负权重核（Lanczos）可能让通道越界，输出时统一夹到 `rgb ≤ a`。
- 两趟分离（先水平后垂直），中间缓冲是 `float[dstW × srcH × 4]`；超大放大（>2 GB 中间缓冲）直接抛异常并提示分块，不做静默降级。
- **不做 `Compand`**：sRGB ↔ 线性光需要查表与 gamma 往返，v1 的定位是绘图而不是色彩管线；`ResizeOptions.Compand = true` 抛异常，避免「以为做了色彩正确缩放」。
- 内存/性能属 §N：普通 4K 图片放大到 8K 时中间缓冲约 530 MB，先记录为已知上限，M7 之后再考虑分块流式。

### 6.2 颜色与混合

```csharp
public readonly struct Color : IEquatable<Color>
{
    public readonly byte R, G, B, A;
    public static Color FromRgb(byte r, byte g, byte b);
    public static Color FromRgba(byte r, byte g, byte b, byte a);
    public static Color Transparent / White / Black / …;
    public Color WithAlpha(byte a);
    public Color Blend(Color over);        // 非预乘语义的 source-over
    internal uint Premultiplied();         // 4 字节打包
}
```

混合内核：

```csharp
// 一行：out = src + dst * (255 - sa) / 255，定点：(x * invSa + 127) / 255 用 (t = x * invSa + 0x80; (t + (t >> 8)) >> 8)
internal static class RowBlender
{
    public static void Over(Span<byte> dstRow, ReadOnlySpan<byte> srcRow, ReadOnlySpan<byte> coverage);  // 标量孪生
    public static void OverVector(Span<byte> dstRow, …);                                                  // SIMD 快路径
}
```

要求：每个 SIMD 内核都有标量孪生，且在 AVX2 / SSE / 标量三档输出**逐字节一致**；内核标 `MethodImplOptions.NoInlining | AggressiveOptimization`。

### 6.3 几何与路径

| 类型 | 说明 |
|---|---|
| `PointF` / `RectF` / `SizeF` / `Size` | 浮点与整数几何；`Rectangle` 是整数像素矩形（裁剪、画布尺寸），不参与路径 |
| `IPath` | 轮廓接口：`Bounds`、`Transform(Matrix3x2)`。内置实现是封闭集合，自定义轮廓请用 `PathBuilder` |
| `Path` | 不可变命令序列（`MoveTo/LineTo/CubicTo/Close`）+ 懒展平（默认 0.25 px，自适应细分） |
| `PathBuilder` | 流畅构建：`MoveTo/LineTo/QuadraticTo/CubicTo/ArcTo/CloseFigure/AddSegment/Build` |
| 图元 | `RectangularPolygon`、`Polygon`、`EllipsePolygon`、`CirclePolygon`、`RoundRectanglePolygon`，全部实现 `IPath` |

```csharp
IPath path = new PathBuilder()
    .MoveTo(20, 20).LineTo(320, 20).LineTo(320, 140).CloseFigure()
    .MoveTo(360, 50).CubicTo(400, 20, 440, 90, 400, 120)
    .Build();

IPath circle = new CirclePolygon(new PointF(400, 90), 40f);
```

- 复用 `System.Numerics.Matrix3x2` 做仿射变换，不自己造矩阵。
- 展平：cubic 用 de Casteljau 二分 + 「控制点到弦的距离」判据，额外检查控制点是否在弦同侧（防 S 形曲线被过早判定为直），最大深度 16。
- 二次贝塞尔在 `PathBuilder` 里按 2/3 规则转成 cubic，光栅化器只见折线，没有 arc/quad 分支。
- 弧（`ArcTo`）走 SVG 端点参数化（`end, radius, rotationDegrees, isLargeArc, sweepClockwise`）：求出圆心参数后按 ≤90° 拆成 ≤4 段 cubic（`alpha = 4/3 · tan(Δ/4)`），最后一段强制落在精确端点上。半径不足时按规范等比放大，半径 0 退化成 `LineTo`。光栅化器仍然只见折线。
- 圆角矩形（`RoundRectanglePolygon`）用四条直线 + 四段 90° 弧拼；半径夹到 `min(w, h)/2`，因此「半径超大」退化成胶囊而不是自交。

### 6.4 光栅化

```csharp
public enum FillRule { NonZero, EvenOdd }

internal sealed class ScanlineRasterizer
{
    // 输入：已 flatten 的闭合/开放子路径（float 点列）
    // 输出：A8 coverage（宽×高，单独一块可复用缓冲），覆盖率 0..255
    public void Rasterize(ReadOnlySpan<FlattenedPath> paths, FillRule rule, Rect clip, CoverageBuffer output);
}
```

- 沿用 `GlyphRasterizer.cs` 的算法思路（纵向 N=4 超采样 + 横向精确覆盖），但输入从「字形二次贝塞尔轮廓」泛化为「任意折线 + 环绕数」，并按 `FillRule` 决定填充。
- 抗锯齿精度：与 8-bit coverage 对齐，误差目标见 §9。
- `CoverageBuffer` 按最大画布尺寸池化复用，避免每次 draw 分配。
- 混合阶段与光栅化分离：先得 coverage，再 `brush.Sample(...)` × coverage 混进画布，这样文字（自带 mask）与形状走同一条混合路径。

#### 6.4.1 矩形快速通道（必须做，不是优化可选项）

UI 与文字排版里绝大多数填充就是矩形，走通用扫描线等于为每个矩形付出展平 + 建边表 + 分配 coverage 的固定开销。规则如下：

```csharp
// DrawingContext.Fill 内部先试快路径，失败再落通用光栅化
internal static bool TryFillRectangle(Canvas canvas, RectangleF region, Color color, out bool handled);
```

| 级别 | 触发条件 | 做法 | 省掉的成本 |
|---|---|---|---|
| **像素对齐全覆盖** | 轴对齐、边界落在整像素（`x/y/w/h` 为整数）、纯色笔刷、无旋转 | 整行 `RowBlender.OverSolid(row, color, 全 255)` | 展平、边表、coverage 缓冲全部跳过 |
| **半像素边界条带** | 轴对齐、边界带小数、纯色笔刷、无旋转 | 内部区域整行全覆盖；只有边界行/列各算一次 1 px 宽的 coverage 条带 | 一整个矩形的 coverage 退化成至多 4 条边 |
| **不透明大块** | 上述任一 + `color.A == 255` | 直接 `dst = src` 覆盖（`RowBlender.CopyOpaque`），不做混合 | 每像素省掉一次乘法 |

- **一致性是硬约束**：快路径与通用光栅化必须逐字节相同。测试用随机矩形跑两遍（正常路径 vs `ForceSlowPath = true`）全缓冲区比对，不是「看起来差不多」。
- **降级条件写死**：旋转/剪切变换、渐变或图案笔刷、圆角矩形、带 clip 交集且交集边界非轴对齐 → 直接落通用路径。笔刷不是纯色时，只要 shape 本身是矩形仍可命中「整行 255 coverage」级别（省的是光栅化，不是采样）。
- **不进入 `Canvas.FillRectangle`**：M1 那个 `internal` 入口只是混合内核的自检工具，M2 后归档为测试专用；绘图入口只有 `DrawingContext`，避免两条语义不同的填色路径。
- 快路径代码放在 `Rasterization/RectangleFill.cs`，不塞进 `DrawingContext`，方便单测直接调用与基准对比。

### 6.5 笔刷

```csharp
public interface IBrush
{
    bool TryGetSolidColor(out Color color);   // 纯色可命中「整行无 coverage 计算」快路径
    // 在 coverage 上按像素采样颜色并混合；region 是已裁剪的包围盒
    void Blend(Canvas canvas, Rectangle region, ReadOnlySpan<byte> coverage, int coverageStride);
}

public sealed class SolidBrush(Color color) : IBrush;

public readonly struct ColorStop(float offset, Color color);

public abstract class GradientBrush : IBrush      // 停止点归一化 + 预乘插值 + 逐像素混合 + 可选抖动
{
    public bool Dither { get; init; }
    public IReadOnlyList<ColorStop> Stops { get; }
    protected abstract float ParameterAt(float x, float y);
}

public sealed class LinearGradientBrush(PointF from, PointF to, params ColorStop[] stops) : GradientBrush;
public sealed class RadialGradientBrush(PointF center, float radius /* 或 SizeF 椭圆 */, params ColorStop[] stops) : GradientBrush;
public sealed class PatternBrush(Canvas tile, PointF origin) : IBrush;   // 平铺，构造时深拷贝瓦片
```

- 渐变**在预乘空间插值**：透明黑 → 不透明白在中段必须是「白色半透明」，直通插值会给出灰色脏边（有测试守住）。
- 停止点按 offset 排序、越界夹住；只有 1 个停止点时补一个同色停止点，全部同色时 `TryGetSolidColor` 返回 true，矩形仍走纯色快路径。
- 可选 4×4 ordered dither（`Dither = true`）抑制色带：在「像素中心参数」上加 `(bayer - 7.5) / 255`，只影响颜色通道，不改变 alpha。
- `PatternBrush` 按取模环绕平铺；瓦片全不透明且同色时才报告纯色（预乘瓦片反预乘会丢精度，半透明一律走逐像素）。
- 渐变/图案坐标是画布像素坐标，**默认不随 `DrawingContext.Transform` 旋转缩放**（构造笔刷时的坐标系）；`Fill` 会把画布坐标按 `Matrix3x2.Invert(Transform)` 映射回用户空间再采样（M7），因此同一个笔刷实例在不同变换下会得到不同采样相位，而笔刷对象本身不被改写。自定义 `GradientBrush` 派生类默认「不跟随」（`WithInverseTransform` 返回自身）。
- 性能：内置渐变按行建「参数游标」（`U`/`Du`/`Ddu`），线性渐变一阶差分、径向渐变二阶差分，跟随变换时导数取逆矩阵第一列——热循环里没有点积、除法与矩阵乘。`TryCreateRowCursor` 是 `internal virtual`，默认 `false`，外部派生类仍走逐像素 `ParameterAt`；快慢两条路径共用 `BlendPixel`，结果逐字节一致（misc §AA）。

### 6.6 描边

```csharp
public enum LineJoin { Miter, Bevel, Round }
public enum LineCap { Butt, Round, Square }

public interface IPen
{
    Color Color { get; }
    float Width { get; }                        // 画布像素，必须为正
    LineJoin LineJoin => LineJoin.Round;        // 默认实现：只关心颜色的画笔无需重写
    LineCap LineCap => LineCap.Round;
    float MiterLimit => 4f;
    float[]? DashPattern => null;               // 线宽倍数；奇数长度按 SVG 语义重复
    float DashOffset => 0f;
}

public sealed class Pen(Color color, float width = 1f) : IPen;   // 样式用 init 属性设置

internal static class StrokeOutline
{
    public static Path Build(FlattenedPath source, float width, in StrokeStyle style);
}
```

实现要点（详见 misc §X）：flatten → 每段生成 ±w/2 的四边形 → 顶点按 `LineJoin` 补连接、开放端点按 `LineCap` 补端帽 → 所有子路径统一成「正面积绕向」→ 丢给 `NonZero` 填充。**nonzero 自动完成并集**，因此不需要多边形布尔运算，这是把描边从「很难」降到「中等」的关键；接缝处不会出现「两次半透明叠加」的深色线，因为 coverage 是整条轮廓一次算出来的。

- 连接：两条边的叉积决定外侧，`miter` 用两条外侧偏移线的闭式交点（比值 = 1/sin(θ/2)），超过 `MiterLimit` 退回 `bevel`；共线与 180° 折返不补几何（四边形已贴合）；重复点补圆盘兜底。
- 端帽：`butt` 不补；`round` 补圆盘（内半被四边形吸收）；`square` 补一个沿切向平移半个线宽的矩形。
- 虚线：图案单位是**线宽倍数**，所以线宽/变换缩放时虚线段长自动跟着变；flatten 后按弧长切段，每一段是独立的开折线（两端补端帽、内部顶点用 join）。闭合路径跨起点的两段会合并；整圈都落在实线段内时按闭合轮廓处理（起点补连接而不是两个端帽）。
- 笔宽跟着变换的缩放走：`actual = Width × sqrt(|det(Matrix)|)`。非等比缩放下取几何平均，文档写明。
- 展平后没有任何点的路径（只有 `MoveTo`）不产生像素；单点且端帽为平头时也不产生像素。
- 特化快路径（`DrawCircle` 直接生成环带、矩形/圆角矩形的一体化环）不做：M7 基准显示描边的主要成本在扫描线（已加活动边表），四边形 ∪ 连接/端帽的边数在同一量级，特化收益不足以抵消一条独立代码路径的维护成本。

### 6.7 文字

从 `F:\code\zlmsharp\src\GlyphRaster` 拷贝 11 个 .cs + `Fonts/` 两个文件：

```
EmbeddedFonts.cs  TrueTypeFont.cs  TrueTypeFont.Glyphs.cs  GlyphOutline.cs  GlyphRasterizer.cs
GlyphMask.cs  GlyphPath.cs  PathCollection.cs  TextFont.cs  TextFont.Layout.cs  TextLayout.cs
```

`TextPainter.cs`（含 `RgbColor`）**不拷**：它自带一套 RGB24/BGRA 混合实现，与本库的 `IBrush` + `RowBlender` 是两条语义不同的路径，只会带来不一致。文字混合统一走 coverage + 笔刷。

命名空间与可见性规则（全库统一，逐条见 misc §C）：

- **公开 API 一律在根命名空间 `ImgProcSharp`**，消费者只需 `using ImgProcSharp;`。`TextFont`（`.cs` + `.Layout.cs`）留在根命名空间，是文字部分唯一的 public 门面。
- **从 GlyphRaster 拷来的引擎代码统一放 `ImgProcSharp.Text`，且全部 `internal`**：`TrueTypeFont`、`GlyphOutline`、`GlyphRasterizer`、`GlyphMask`、`GlyphPath`、`VectorPath`、`PathCollection`、`PathVerb`、`EmbeddedFonts`、`BuiltInFont`、`TextLayout`、`TextLayoutOptions`、`PositionedGlyph`。
- 因此**不需要改任何类型名**（`PathCollection`/`VectorPath` 与核心 `Path` 并存也无冲突），只需改 `namespace` + 加 `internal`。
- `TextFont` 的 public 面收敛为：`Load/LoadFromFile/LoadFromStream`、`BuiltIn`、`FamilyName`、`UnitsPerEm`、`GetAscent/GetDescent/GetLineHeight`、`HasGlyph`、`GetAdvance`、`Measure`；`GetGlyphMask/GetGlyphPaths/Layout/GetTextPaths` 降为 `internal`（对外用我们的 `TextDrawing.MeasureText` 与慢路径转换）。
- `EmbeddedFonts.cs` 的资源名常量 → `ImgProcSharp.Text.Fonts.WenQuanYiMicroHei.ttf.gz`，与 csproj 的 `LogicalName` 严格一致。
- 删掉 GlyphRaster 的打包属性与 `InternalsVisibleTo ZlmSharp.Tests`；改由打包工程给两个测试工程声明。

```csharp
namespace ImgProcSharp;   // 公开门面（引擎在 ImgProcSharp.Text，internal）

public sealed partial class TextFont
{
    public static TextFont Load(byte[] data);
    public static TextFont LoadFromFile(string path);
    public static TextFont LoadFromStream(Stream stream);
    public static TextFont BuiltIn { get; }            // 内嵌文泉驿微米黑，懒加载

    public string FamilyName { get; }
    public int UnitsPerEm { get; }
    public float GetAscent(float size);
    public float GetDescent(float size);
    public float GetLineHeight(float size);
    public bool HasGlyph(int codePoint);
    public float GetAdvance(int codePoint, float size);
    public float Measure(string text, float size);
}
```

绘制两条路径：

- **快路径**（默认）：`TextFont.Layout()`（internal）→ 每个 `GlyphMask` 的 A8 覆盖直接当 coverage 混合。整数笔位、轴对齐。
- **慢路径**（需要旋转/缩放/描边字时）：`TextFont.GetTextPaths()`（internal）→ `GlyphPathBridge.ToPath()` 转成核心 `Path` → 走通用填充/描边。

`TextOptions`/`TextDrawing` 已落地。描边文字不需要 `TextOptions` 里再加字段：`GetTextPath` 直接给轮廓，`ctx.Draw(pen, path)` 即可（`Transform` 非恒等时 `DrawText` 内部也走这条路径）：

```csharp
public sealed class TextOptions
{
    public TextFont Font { get; init; } = TextFont.BuiltIn;
    public float Size { get; init; } = 16f;
    public Color Color { get; init; } = Color.Black;
    public IBrush? Brush { get; init; }
    public float MaxWidth { get; init; }      // 0 = 不折行
    public float LineHeight { get; init; }
    public TextAlign Align { get; init; }     // Left / Center / Right
}

public static class TextDrawing
{
    public static void DrawText(this DrawingContext ctx, string text, PointF at, TextOptions? options = null);  // 快路径
    public static void FillText(this DrawingContext ctx, IBrush brush, string text, PointF at, TextOptions? options = null);  // 轮廓慢路径
    public static Path GetTextPath(string text, PointF at, TextOptions? options = null);   // 拿去 Fill / Draw 描边
    public static SizeF MeasureText(this TextFont font, string text, float size, float maxWidth = 0f);
}
```

- `at` 是**首行行顶左端**（不是基线），与 ImageSharp.Drawing 的 `location` 一致；基线偏移由 `TextLayout.Ascent` 内部处理。
- 对齐在排版层完成：`TextAlign` 以 `MaxWidth`（为 0 时以最宽行）为参照，逐行平移字形，快慢两条路径共用同一份行宽数据。
- 字形掩码经过 hinting，轮廓没有：同一段文字两条路径的墨水量可差 20% 左右，验收只要求「同量级 + 墨迹包围盒对齐 ±3 px」，不追求逐字节一致（misc §V）。

### 6.8 DrawingContext

```csharp
public sealed class DrawingContext
{
    public Canvas Canvas { get; }
    public Matrix3x2 Transform { get; }          // 当前累计变换，默认恒等
    public bool HasClip { get; }
    public int ClipDepth { get; }

    public void PushTransform(Matrix3x2 matrix);  // 左乘：先应用 matrix，再应用已有变换
    public void SetTransform(Matrix3x2 matrix);
    public void ResetTransform();

    public void PushClip(IPath path);             // 与已有裁剪求交，掩码入栈
    public void PopClip();                       // 顺序不对就抛 InvalidOperationException

    public void Fill(IBrush brush, IPath path, FillRule rule = FillRule.NonZero);
    public void Fill(Color color, IPath path, FillRule rule = FillRule.NonZero);
    public void Draw(IPen pen, IPath path);
    public void Draw(Color color, float width, IPath path);
    public void Clear(Color color);
}
```

- 变换在 `Fill`/`Draw` 入口一次性乘到轮廓上（`path.Transform(Transform)`），光栅化器永远只见到画布坐标。副作用是矩形特化快路径在非恒等变换下自动失效——`Transform` 返回 `Path`，不再是 `RectangularPolygon`，不需要额外判断。
- 文字：`Transform` 非恒等时 `DrawText` 自动改走轮廓慢路径（字形掩码是轴对齐 A8 位图，不能旋转）；`GetTextPath` 同样吃当前变换。
- 裁剪是**真栈**（M7）：每层是一张画布大小的 A8 掩码，`PushClip` 时把轮廓按当前变换光栅化后与栈顶相乘。填充/描边/文字三条路径都走同一个 `BlendCoverage`（内部方法），所以字形掩码快路径也不会漏裁剪。有裁剪时矩形特化快路径自动关掉——被裁掉的矩形不能再整块写。细节见 misc §Y。
- 渐变/图案笔刷**跟随变换**（M7）：`Fill` 里先算 `Matrix3x2.Invert(Transform)`，再让笔刷返回一个「采样前把画布坐标映射回用户坐标」的副本（`GradientBrush.WithInverseTransform` / `PatternBrush.WithInverseTransform`）。笔刷本身不可变，原对象在恒等变换下行为不变（有测试）。
- 线宽随变换缩放（`Width × sqrt(|det|)`）；退化的变换直接不画。

- 图元命名对齐 ImageSharp.Drawing：`IPath` 是轮廓接口，`Path`（`PathBuilder` 构建）、`RectangularPolygon`、`EllipsePolygon`、`CirclePolygon`、`Polygon`、`RegularPolygon` 都实现它。`Rectangle`（整数、像素矩形）只用于画布/裁剪，不参与路径。
- 因此「填充一个矩形」在 v1 有两种写法：`Fill(color, new RectangularPolygon(0, 0, 100, 50))`（走光栅化）与 `ctx.Clear(color)`/特化快路径。若 M2 的矩形特化快路径要保留，也必须接受 `RectangularPolygon` 并保证与通用光栅化逐字节一致。
- ext 层的链式糖：`image.Mutate(x => x.Resize(...).Fill(...).Draw(...))`，`Resize` 换画布、`Fill/Draw` 转给当前 `DrawingContext`。

---

## 7. ext 包设计（解码桥 + 链式处理）

```csharp
namespace ImgProcSharp;

public sealed class Image : IDisposable
{
    public static Image Load(string path);
    public static Image Load(Stream stream, bool leaveOpen = false);
    public static Image Load(ReadOnlySpan<byte> data);
    public static Image Create(int width, int height, Color? background = null);

    public Canvas Canvas { get; }
    public ImageFormat SourceFormat { get; }         // Sdcb.SimdCodecs.Abstractions.ImageFormat
    public ReadOnlyMemory<byte> IccProfile { get; }  // 原样保留，v1 不做色彩管理
    public ImageOrientation Orientation { get; }     // EXIF 方向，Load 默认不应用
    public int Width { get; }
    public int Height { get; }

    public Image Mutate(Action<IImageProcessingContext> operations);   // ImageSharp 风格处理器链
    public Image Clone();
    public Image ApplyOrientation();                  // 按 Orientation 把画布转正
}

/// <summary>处理器链上下文，方法名与 ImageSharp.Drawing 的 IImageProcessingContext 对齐。</summary>
public interface IImageProcessingContext
{
    Image Image { get; }
    Size GetCurrentSize();

    IImageProcessingContext Resize(ResizeOptions options);        // 替换当前画布
    IImageProcessingContext Resize(int width, int height);        // Stretch
    IImageProcessingContext Clear(Color color);
    IImageProcessingContext Fill(IBrush brush, IPath path);
    IImageProcessingContext Fill(Color color, IPath path);
    IImageProcessingContext Draw(IPen pen, IPath path);           // 圆角连接 + 圆头端点
    IImageProcessingContext Draw(Color color, float width, IPath path);
    IImageProcessingContext DrawText(TextOptions options, string text, PointF location);
    IImageProcessingContext PushClip(IPath path);                 // 与已有裁剪求交
    IImageProcessingContext PopClip();
    IImageProcessingContext PushTransform(Matrix3x2 transform);   // 左乘
    IImageProcessingContext SetTransform(Matrix3x2 transform);
    IImageProcessingContext ResetTransform();
}
```

典型调用：

```csharp
image.Mutate(x => x
    .Resize(new ResizeOptions { Size = new Size(800, 600), Mode = ResizeMode.Max, Sampler = KnownResamplers.Lanczos3 })
    .Fill(new SolidBrush(Color.White.WithAlpha(128)), new RectangularPolygon(0, 0, 800, 80)));
```

`DrawingContext`（core）只负责「画到某个 `Canvas` 上」；`IImageProcessingContext` 属于 ext 层，持有 `Image`，`Resize` 换掉内部画布，`Fill/Draw/DrawText` 转给当前 `DrawingContext`。这样 core 不依赖 `Image`，也不用引入可变尺寸。

**实现要点**

- 解码（`ImageDecoderBridge.cs` + `CodecRegistry.cs`，全库唯一接触 SimdCodecs 的两个文件）：`CodecRegistry.Find(data)` 按注册顺序问每个解码器的 `CanDecode`，命中的 `ImageDecoderRegistration` 再 `TryReadInfo` 取 `Format`/`Orientation`/尺寸。然后两条路径：默认 `registration.TryDecodeInto(data, canvas.Buffer, canvas.Stride, PixelFormat.Rgba32)`，解码器直接把像素写进画布，连解码器自己的输出数组都不要；目标一律是「基本 8 位」`Rgba32`，预乘格式会让编解码器退回 decode-then-convert，所以预乘仍由本地那趟 `PremultiplyInPlace` 补。直写被拒（HDR/float32/解码器不支持目标格式）时回退 `TryDecode` + `IDecodedImage.TryCopyTo` 逐行进画布。没注册任何解码器时抛 `InvalidDataException`，消息里写明怎么注册。
- 文件路径走 `ByteSource.FromFile(path)`：文件内存映射，编码字节不进托管堆，直接从映射内存解到画布（省掉 `File.ReadAllBytes` 的一次整文件分配与拷贝）。`ByteSource` 就在 `Abstractions` 里，不引元包也有。
- ICC：`registration.TryReadMetadata` 对所有格式都只扫元数据段（JPEG APP2、PNG `iCCP`、WebP `ICCP`、TIFF IFD），任何格式的 ICC 都是免费的；交给 `Image` 前复制一份（元数据可能是映射内存的切片，而 `Image` 活得更久）。`ImageDecodeOptions.RequireIccProfile` 因此失效（保留类型只为源码兼容）。
- 编码（`EncoderRegistry.cs` + `ImageExportExtensions.cs`，同样只碰 `Abstractions`）：`image.Save(path)` / `canvas.Encode(format, options)` 按 `ImageFormat` 取 `ImageEncoderRegistration`，`Create` → `AddFrame(w, h, Rgba32Premultiplied, metadata)` → `WriteRows(canvas.Buffer, height, canvas.Stride)` → `Commit`。预乘→直通在编码器内部完成，半透明图也不用临时数组；发布的 dll 里仍然没有 zlib/CRC/DCT，也不用背编码器的兼容性责任。没注册编码器时抛 `NotSupportedException` 并给出 `EncoderRegistry.Register` 示例。用法见 misc §Q。
- 方向：2.0 的 `TryReadInfo` 已给出 `ImageInfo.Orientation`，不必自己解析 EXIF APP1。`ApplyOrientation()` 应用后把 `Orientation` 归位 `Normal`，因此重复调用无副作用（幂等，有测试）。
- `Mutate` 的 `Resize` 会**替换并释放**旧画布，并把同一个 `DrawingContext` 重绑到新画布（`Transform` 保留）；其它方法只是薄封装，不引入额外状态。

---

## 8. 里程碑（垂直切片优先）

先打通「读图 → 画图 → 存图」，再回头补描边与渐变。这样在第 4 个里程碑就有可运行的示例。

| # | 里程碑 | 内容 | 工时 |
|---|---|---|---|
| M0 | 骨架 | 仓库结构、props、两个 csproj、源码内嵌链路（放一个 `Hello` 类型验证 net8/net10 双编译 + 打包内容正确）、slnx、CI build | 0.5d |
| M1 | Canvas + 混合 | `Canvas`、`Color`、`PixelOps`、`RowBlender`（标量 + Vector128）、临时 `FillRectangle` 入口（M2 交给 `DrawingContext`）、单元测试 | 1.5d |
| M1.5 | 缩放 ✅ | `Size/SizeF`、`IResampler/KnownResamplers`、`ResizeOptions/ResizeMode`、`Canvas.Resize`（Crop/Pad/BoxPad/Max/Min/Stretch，预乘空间两趟重采样）、26 个用例 | 1d |
| M2 | 路径 + 填充 | `IPath` 图元（`Path`/`RectangularPolygon`/`EllipsePolygon`/`Polygon`/`RoundRectanglePolygon`）、`PathBuilder`/展平/`ArcTo`、`ScanlineRasterizer`（NonZero/EvenOdd + AA）、`RectangleFill` 快路径（与通用路径逐字节一致）、四种笔刷、`DrawingContext.Fill`（**已完成**） | 2.5d |
| M3 | 文字 | GlyphRaster 拷入 + 命名空间/可见性改造（引擎 `internal`）、`TextFont` 门面收敛、`TextOptions`/`TextAlign`、`DrawText` 快路径、`FillText` 轮廓慢路径、`GlyphPathBridge`、`MeasureText`、折行/对齐、中英文测试（**已完成**） | 1.5d |
| M4 | ext 端到端 | `Image` 门面（Load/Create/Mutate/Clone/ApplyOrientation）、解码桥、链式上下文、Exif 方向、打包（**编码不做**：测试用 `Sdcb.SimdCodecs.Png.PngWriter`） | 2.5d |
| M5 | 描边 + 变换 + 示例 ✅ | `IPen`/`Pen`、`StrokeOutline`（四边形 ∪ 圆盘）、`DrawingContext.Draw`、变换栈（`PushTransform`/`SetTransform`/`ResetTransform`）、`GetTextPath`、samples 控制台（`output/result.png`，用 `PngWriter` 输出）、README 用法（**已完成**：当时只有圆角连接 + 圆头端点，M6 已补齐） | 1.5d |
| M6 | 描边扩展 ✅ | `LineJoin`（Miter/Bevel/Round）、`LineCap`（Butt/Round/Square）、`MiterLimit` 超限退回 bevel、`DashPattern`/`DashOffset`（线宽倍数，闭合路径跨起点合并）、`IPen` 默认成员保持向后兼容（**已完成**：15 个用例，见 misc §Z；矩形/圆/圆角矩形特化快路径经 M7 基准评估后不做） | 3d |
| M7 | 裁剪 / AOT / 基准 ✅ | `PushClip`/`PopClip`（A8 掩码栈）、笔刷跟随变换、BenchmarkDotNet（填充/描边/文字/编解码）、AOT 发布 smoke（**已完成**：win-x64 自包含 9.8 MB，JIT/AOT 输出逐字节一致，见 misc §Y） | 2d |

合计 **18 人天**；到 M7 结束（**13 天**）就是一个能发 0.1 的可用版本，M6 原计划拆到 0.2，现已一并完成。

顺序依赖：M0 → M1 → M2 → M3 → M4 → M5 为关键路径；M6、M7 依赖 M2，可并行或后置。

进度：**M0、M1、M1.5、M2、M3、M4、M5、M6、M7 全部完成**（Core 199 个用例在 net8.0 / net10.0 上全绿，ext 16 个用例在 net10.0 上全绿，`dotnet build` 0 警告，示例产出 960×540 PNG 与 1280×720 JPEG，AOT 发布产物与 JIT 输出逐字节一致）。M2 已落地：几何与路径模型（`IPath`/`Path`/`PathBuilder`/展平/五种图元）、SVG 端点弧 `ArcTo`、`RoundRectanglePolygon`、`ScanlineRasterizer`（4× 子采样 + 横向精确覆盖，NonZero/EvenOdd）、`CoverageBuffer`、`RectangleFill` 快路径、`DrawingContext.Fill`，以及 M2 后段的四种笔刷（`SolidBrush`/`LinearGradientBrush`/`RadialGradientBrush`/`PatternBrush`，渐变预乘插值 + 可选 ordered dither）。M3 已落地：GlyphRaster 引擎拷入（`internal`）、`TextFont` 门面、`TextOptions`/`TextAlign`、`DrawText` 快路径、`FillText` 轮廓慢路径、`GlyphPathBridge`、`MeasureText`、折行与对齐。M4 已落地：`Image` 门面（`Load`/`Create`/`Mutate`/`Clone`/`ApplyOrientation`）、`ImageDecoderBridge`（`TryReadInfo` + `TryDecode` + 逐行预乘）、`IImageProcessingContext` 链式糖、端到端测试（PNG 素材现造）。**公开 API 不含编码**：`PngWriter` 由调用方直接用（测试即如此）。M5 已落地：`IPen`/`Pen`、`StrokeOutline`（线段四边形 ∪ 顶点圆盘，统一绕向走 NonZero 并集）、`DrawingContext.Draw`、变换栈（`PushTransform`/`SetTransform`/`ResetTransform`）、`TextDrawing.GetTextPath`、`DrawText` 在非恒等变换下自动改走轮廓路径、samples 示例工程（无素材依赖，`PngWriter` 输出 `output/result.png`）。M7 已落地：`PushClip`/`PopClip`（画布大小 A8 掩码栈，填充/描边/文字三条路径统一走 `BlendCoverage`）、渐变与图案笔刷跟随变换（逆矩阵采样 + 不可变副本）、BenchmarkDotNet 基准工程（填充/描边/文字/编解码/端到端）、NativeAOT smoke 工程（`PublishAot`，win-x64 自包含 9.8 MB，输出哈希与 JIT 一致）。M6 已落地：`LineJoin`（Miter/Bevel/Round）与 `LineCap`（Butt/Round/Square）、`MiterLimit` 超限退回 bevel、`DashPattern`/`DashOffset`（线宽倍数，闭合路径跨起点合并，整圈实线退化为闭合描边），样式通过 `IPen` 默认成员保持向后兼容。0.2 已落地第一项优化：渐变按行预计算参数游标（`GradientRowCursor`，线性渐变一阶差分、径向渐变二阶差分，跟随变换时导数取逆矩阵第一列，热循环里没有点积与除法；外部派生类仍走逐像素慢路径，两条路径共用一个 `BlendPixel`），渐变矩形 13.3 ms → 8.6 ms（纯色 10.9 倍 → 7.1 倍），另加圆心对称性、旋转椭圆渐变、外部派生类兜底、快慢路径 ≤1 LSB 对照四个用例（199 = 195 + 4）。剩余：0.2 的混合模式与图层。M0 实际未做 CI（仓库尚无远端），其余按计划落地。

---

## 9. 验收标准

**功能**

- `dotnet run --project samples/ImgProcSharp.Samples` 生成 `output/result.png`（`PngWriter`）：渐变背景 + 圆角卡片 + 中英文排版 + 裁剪条纹带 + 圆环描边 + 旋转徽章 + 描边文字 + miter 尖角 + 虚线，肉眼可验，无错误像素。
- 同一条命令生成 `output/photo.jpg` 与 `output/compare.png` / `compare.jpg`：`JpegEncoder` 造出真 JPEG 素材 → `Image.Load` 解码 → 叠加渐变/虚线/文字/旋转徽章 → `JpegEncoder.Encode` 写回，并把原图与成品左右拼成一张对比图，证明「解码 → 绘制 → 编码」闭环可用（README 首页展示的就是 `docs/compare.jpg`）。
- `Image.Load("x.png").Mutate(x => x.Resize(...).Fill(...).Draw(...).DrawText(...))` 跑通，`Image.Clone()` 出来的图画布互不影响。
- **解码保真**：`PngWriter` 写出的 PNG 经 `Image.Load` 回到 `Canvas`，与原始像素逐通道相差 ≤ 1（直通→预乘→直通在 `a<255` 时的量化误差），全不透明时逐字节相同。
- **方向**：带 EXIF 方向的图 `Orientation` 与文件一致；`ApplyOrientation()` 后画布按 `dst(dx,dy) = src(dy, H-1-dx)`（90° CW）等规则转正，再次调用不改变像素。
- `IccProfile` 原样带出（空则 `IsEmpty`），v1 不做色彩管理。
- **缩放**：`ResizeMode` 六种模式的输出尺寸符合定义；纯色图经 Nearest/Bilinear/Bicubic/Lanczos3 缩放后颜色误差 ≤ 1；所有采样器的输出都满足 `rgb ≤ a`（预乘合法，负权重核也不例外）。
- 输出的 PNG 能被 Windows 照片查看器、Chrome、`pngcheck` 正常打开（无 CRC / 滤波器错误）。

**数值**

- 矩形填充（100×100）实际覆盖面积误差 < 0.2%。
- 半径 100 的圆填充面积 vs `πr²` 误差 < 1%（AA 边缘代价）。
- 半径 50 的半圆弧填充面积 vs `πr²/2` 误差 < 2%；圆角矩形面积 vs `wh - (4-π)r²` 误差 < 2%（含半径超限夹到 `min(w,h)/2` 的胶囊形）。
- 线性渐变中点色误差 ≤ 16/255（红→蓝中点约 `(128, 0, 128)`）；透明→不透明渐变中点必须是「白色半透明」而不是灰；同一条与渐变轴垂直的列上像素逐字节相等。
- 文字验收：`DrawText`（字形掩码）与 `FillText`（轮廓光栅化）两条路径的墨迹包围盒对齐 ±3 px，墨水量比值在 0.6–1.5 之间（hinting 造成的固有差异）；`MeasureText` 与逐字形前进宽度之和相差 < 0.01 px；折行后每行宽度 ≤ `MaxWidth`；对齐偏移符合 `(MaxWidth - 行宽) × {0, 0.5, 1}`。
- `RowBlender` 在 AVX2 / SSE / 标量三档输出逐字节一致。
- 水平/垂直线、整像素对齐矩形的边缘 100% 覆盖，无灰边。
- **描边**：宽 w、长 L 的水平线，内部行整行覆盖（wa=255），外侧 `floor(y+w/2)+2` 起全白；闭合方形描边中心保持原色。端点/连接按样式分别验收：`Round` 端帽在距端点 `w/2 - 1` 内有墨水、`w/2 + 2` 外全白；`Butt` 在 `x < 端点` 处无墨水；`Square` 在 `端点 + w/2` 内为实色；90° 折线上 `Miter` 的尖角伸到 `r·√2`（`r·1.2` 处有墨水），`Bevel`/`Round` 在 `r + 1` 外全白；`MiterLimit < 1/sin(θ/2)` 时必须退回 bevel。
- **虚线**：线宽 4、图案 `[10, 10]` 的水平线，`[0,40)` 实、`[40,80)` 虚、`[80,120)` 实……；`DashOffset` 整体平移一个相位；线宽翻倍时实/虚段同比翻倍（图案单位是线宽倍数）；闭合方形四条边都出现缺口；图案长于周长时退化为与实线描边逐像素一致。
- **变换**：`PushTransform(translate)` 后填充矩形落在平移后的位置，原位置保持空白；`translate ∘ rotate(90°)` 的落点符合「先旋转再平移」；`ResetTransform` 后恢复恒等；`PushTransform(scale(2))` 下 `Pen.Width=5` 实际覆盖 10 px；缩放 2 倍的文字墨迹包围盒与未缩放时成 2 倍关系（±3 px）。
- **裁剪**：`PushClip` 矩形后填充整张画布，掩码外逐像素保持原色；两层裁剪的交集只有 `min` 区域可见；斜边上的中间值数量 > 20（掩码本身带 AA）；裁剪掩码同样吃当前变换（平移后可见区跟着平移）。
- **笔刷跟随变换**：`scale(2,1)` 下画布 `x=2u` 的颜色等于未缩放时 `x=u`（±3）；旋转 90° 后线性渐变从横向变成纵向；`PatternBrush` 平移后平铺相位跟着平移；用过的笔刷对象本身不被改写（同一实例在恒等变换下仍是原样）。
- **基准与 AOT**：`dotnet run -c Release --project benchmarks/ImgProcSharp.Benchmarks -- --filter "*"` 能跑出表格（`ShortRunJob`）；`dotnet publish samples/ImgProcSharp.AotSmoke -c Release -r win-x64` 后运行 exe，输出与 `dotnet run` 逐字节相同。
- **已知差距**（不算失败，写在这里避免误解）：非等比缩放下笔尖仍按几何平均取圆笔尖（不生成椭圆笔尖）；虚线只在 flatten 之后的折线上按弧长切分，极端密集的曲线（弦长 ≪ 虚线段长）会有亚像素级的段长误差；裁剪不做软裁剪（掩码直接乘 coverage），也不支持「反裁剪」；笔刷变换对自定义 `GradientBrush` 派生类无效（内置的线性/径向/图案都跟随）。

**工程**

- `dotnet build` 通过 net8.0 与 net10.0 双目标，0 warning（核心工程的 `TreatWarningsAsErrors` 建议开启，见 misc §L）。
- `dotnet publish -r win-x64 -p:PublishAot=true` 的 smoke 工程运行结果与 JIT 一致。
- `dotnet pack` 出来的 nupkg：`lib/net10.0/ImgProcSharp.dll` 只有 1 个，字体在 dll 内嵌，`Sdcb.SimdCodecs.Abstractions` 是唯一 NuGet 依赖（不是内嵌 dll），`licenses/` 带字体许可，README 存在。

**健壮性**

- 0 宽/0 高画布、超大尺寸（> `int.MaxValue` 字节）抛 `ArgumentException` 而不是 `OverflowException`/`OOM`。
- 非法路径（NaN/Inf 坐标）不崩：直接跳过该段。

---

## 10. 风险

| 风险 | 影响 | 应对 |
|---|---|---|
| 引入 `Sdcb.SimdCodecs` 2.0（大版本） | 上游 API 变动 | 解码只经 `ImageDecoderBridge.cs`，2.0 迁移实测只动了这一个文件；0.2.1 起再收窄到只依赖 `Abstractions`，格式实现由调用方注册（`CodecRegistry` / `EncoderRegistry`），本库不再随包携带任何格式。编码依赖子包的 `PngWriter`/`JpegEncoder`，不自研。见 misc §B / §Q |
| 调用方拿不到「一键存图」 | 需要多写一行 | 0.2.1 起 `Image.Save` / `Canvas.Save` / `Encode` 已是一行；底下只调 `Abstractions` 的编码契约，实现来自调用方注册的子包，本库不背编码实现（`EncoderRegistry` 见 §12.2-16） |
| 调用方忘了注册解码器 / 编码器 | 升级后 `Image.Load` / `Save` 直接失败 | 异常消息写清「引哪个包、调哪个方法」，README 顶部示例第一行就是注册；`CodecRegistry.RegisteredFormats` / `EncoderRegistry.RegisteredFormats` 可用于自检 |
| 预乘 vs 直通 alpha 决策 | 影响所有混合内核与边界代码 | 已在 §6.1 定为预乘，M0 定稿后不再改 |
| GlyphRaster 只有二次贝塞尔、整数笔位 | 旋转/缩放文字必须走路径慢路径 | 慢路径在 M3 就实现（`GetTextPaths` 已具备） |
| 描边的工作量易失控（join/dash/自交/退化） | 排期风险 | 先用「圆头端点 + 圆角连接」打通（`StrokeOutline`），M6 再补 Miter/Bevel/Square/Dash；实际落地时几何部分只多出「外侧法向 + 闭式 miter 交点」和「按弧长切段」两块，没有引入新的光栅化路径 |
| 内嵌字体 2.0 MB | 包体积 | v1 接受；后续可拆 `ImgProcSharp.Fonts`（misc §E） |
| 色彩管理缺失 | 带 ICC 的图（Display P3）颜色会偏 | v1 明确记录为已知限制（misc §K），README 写清 |

---

## 11. 打包与发布检查单

1. `PublicAPI.Shipped/Unshipped.txt`：若开启分析器（misc §L），首次发布前把全部 API 写进 `PublicAPI.Shipped.txt`。只有根命名空间 `ImgProcSharp` 的类型参与；`ImgProcSharp.Text` 全是 `internal`，不在清单里。
2. 版本号 0.2.0，`PackageLicenseExpression=Apache-2.0` 与本仓库 LICENSE 一致。
3. 包内检查：`unzip -l` 看文件清单（dll 1 个、无 `Core.dll`、无 `Sdcb.SimdCodecs.dll`），字体许可在 `licenses/`。
4. README 写明：net10 限定、字体大小、无色彩管理、字体许可。
5. 打完包用 `dotnet package` 在独立空项目里做一次真实 `PackageReference` 验证（防止源码内嵌漏文件）。

---

## 12. 性能优化记录

### 12.1 测量口径

- 绝对值噪声很大：同一段标量 `PremultiplyInPlace` 在两轮 `Stopwatch` 测量里分别得到 4.7 ms 与 38.7 ms（8×），判断是 CPU 核心调度与 JIT 分层所致。**只信同一次运行内的相对对比**，正式数据走 `BenchmarkDotNet` 的 `--job default`，不用 `ShortRunJob` 的单轮结果下结论。
- 下表是 1920×1080（200 万像素）本机实测，仅用于横向比较。

| 操作 | 耗时 | 每像素 | 备注 |
|---|---|---|---|
| `PremultiplyInPlace`（标量） | 38.7 ms | 18.7 ns | 解码路径 |
| `PremultiplyInPlace`（`Vector128` 原型） | 9.5 ms | 4.8 ns | 4×，逐字节相同 |
| `CopyToStraight`（半透明） | 5.6 ms | 2.7 ns | 每像素 3 次整数除法 |
| `CopyToStraight`（不透明） | 0.58 ms | 0.28 ns | `IsFullyOpaque` 后整块 memcpy；此前 3.12 ms |
| `Unpremultiply(src,dst)` | 10.6 ms | 5.1 ns | 同上，多一次缓冲 |
| `IsFullyOpaque` | 0.27 ms | 0.13 ns | `Vector128` 洗 alpha 通道，接近带宽极限 |
| `FlipHorizontal` | 0.96 ms | 0.48 ns | 1080p；1024×768 优化后 216 µs（§12.2-11） |
| `Rotate90` | 7.0 ms | 3.4 ns | 1080p；1024×768 优化后 1.06 ms（§12.2-11） |
| `Canvas.Clear` | 0.50 ms | 0.24 ns | `uint` 广播填充，接近带宽极限 |
| `Fill` 纯色矩形全覆盖（优化前） | 5.15 ms | 2.5 ns | 比 `Clear` 慢 10× |
| `Fill` 线性渐变全覆盖 | 35.2 ms | 17 ns | 差分游标后 1024×768 实测 8.5 ms（11.3 ns/px） |

同一进程内的 A/B（预热 1 轮 + 7 轮中位数，可靠性远高于上表）：

| 操作 | 耗时 | 每像素 | 对比 |
|---|---|---|---|
| `Fill` 不透明纯色整幅矩形 | 1.03 ms | 0.5 ns | 直写路径，接近 `Clear` |
| `Fill` 半透明纯色整幅矩形 | 25.3 ms | 12.6 ns | 仍走 `RowBlender`，当前最大热点 |
| `PremultiplyInPlace`（标量） | 4.66 ms | 2.3 ns | 参照 |
| `PremultiplyInPlace`（SIMD） | 1.04 ms | 0.5 ns | 4.5× |
| `CopyToStraight`（整块 vs 逐行） | 10.80 ms | 0.5 ns | 26.8 → 10.8 ms（2.5×），alpha=180 数据 |
| `CopyToPremultiplied`（整块） | 0.30 ms | 0.15 ns | 纯 memcpy |
| `Image.Load` PNG 1080p（`TryDecodeInto` 直写） | 10.9 ms | — | 2.0 起 ICC 由 `TryReadMetadata` 免费提供；7.9 MB/次；不透明源再省 1.04 ms 预乘 |
| ~~`Image.Load` PNG 1080p（`RequireIccProfile=true`）~~ | 已废弃 | — | 以前为拿 ICC 要走完整解码 + 逐行转换（16.1 ms / 15.8 MB）；2.0 后只有 HDR/float 回退才会经过 |

### 12.2 已落地

1. **`RowBlender` 的 coverage 装载**：`Vector128.Create(coverage[i], …)` 每 4 像素生成 16 条标量插入（`pinsrb`），比后面的算术还贵；改为 `Vector128.LoadUnsafe` 一次拖 16 字节。尾部不足 16 像素时仍按 4 像素一组走向量（`Create` 只读到 `coverage[i + 3]`，不越界），只剩 0–3 像素走标量，窄行不会退化。语义不变，逐字节对拍由 `RowBlenderSimdTests`（1/2/3/7/8/33/100 像素）守住。
2. **不透明纯色全覆盖直写**：`color.A == 255` 且整行 coverage 全 255 时，source-over 在数学上就是 `dst = src`，于是跳过 `Mul255` 走 `Fill`。实测 1080p 整幅不透明填充 5.15 ms 量级 → **1.03 ms**（同进程 A/B，作为对照的半透明同色是 25.3 ms）。顺带修正 misc §U 的旧结论「1.6 ns/px 已到带宽极限」：`Clear` 能到 0.24 ns/px，纯色填充仍有空间。对拍测试 `OpaqueRectangleFastPathMatchesGenericPath` 覆盖整图/内缩/小数边界/负坐标四种矩形。
3. **`PremultiplyInPlace` SIMD 化**：每轮 4 像素，字节加宽到 ushort、alpha 广播到 RGBA、定点除 255、`ConditionalSelect` 还原 alpha、`Narrow` 回字节；非原地的 `Premultiply(src, dst)` 共用同一内核。`a == 255` 不再需要分支（定点结果天然等于原值），尾部走标量。实测 1080p 全图 **4.66 ms → 1.04 ms（4.5×）**；`PremultiplyInPlaceMatchesScalarReference`（1/3/4/5/20/1000 像素）与 `PremultiplyIntoDestinationMatchesInPlace` 守住逐字节一致。
4. **解码直写画布**：`decoded.ToRgba8()` 每次都分配一整幅 `W*H*4`（1080p 8.3 MB，直接进 LOH）再整图拷进画布。改为 `IDecodedImage.TryCopyTo(canvas.Buffer, canvas.Stride, PixelFormat.Rgba32)` 逐行转换直接落进画布缓冲；`SampleFormat.Float32` 没有默认的 8 位投影，保留 `ToRgba8` 回退。`decoded` 用 `using IDisposable?` 收口（当前未指定 pool 时可空，换成池化实现后不会泄漏）。实测 1080p PNG 加载 22.6 → 19.7 ms（PNG 解压占大头），峰值少 8.3 MB。`PixelConverterParityTests` 对 1/2/3/4 通道 × UInt8/UInt16 与 `ToRgba8()` 逐字节对拍，并验证 stride 填充不被写脏。
5. **导出整块化**：`CopyToStraight` / `CopyToPremultiplied` 在 `destinationStride == Stride` 时一次整块进内核。根因不是除法，而是逐行调用让内核的小循环反复重新进入（1080p 1080 次），实测**半透明导出 26.8 → 10.8 ms（2.5×）**，`CopyToPremultiplied` 0.30 ms。带行填充时仍走逐行路径，`CopyToStraightBulkPathMatchesRowByRowPath` 对拍两条路径。
6. **`TryDecodeInto` 直写成为默认**：`TryDecode` 的解码器输出数组（1080p 8.3 MB）在直写路径下一个字节都不需要——`ImageCodecs.TryDecodeInto(data, canvas.Buffer, canvas.Stride, PixelFormat.Rgba32)` 让 PNG/JPEG 解码器把像素直接写进画布（JPEG 文档称其为 genuine zero-copy decode），之后对带 alpha 的图跑一趟 `PremultiplyInPlace`（不透明图跳过，见第 7 条）。当时 `TryDecodeInto` 不返回 ICC，ICC 只能尽力而为（JPEG 有 `JpegDecoder.TryReadIccProfile` 特例，PNG/WebP 默认留空，要 ICC 就 `RequireIccProfile = true` 回慢路径）——这一条已被 2.0 的 `TryReadMetadata` 解决，见第 14 条。解码器拒绝目标格式或拒绝 8 位投影（cICP PQ/HLG、float32）时仍会自动回退。实测 1080p PNG：**16.12 → 10.88 ms，每次分配 15.83 → 7.92 MB**。`DirectDecodeMatchesRasterDecode`（8-bit）与 `DirectDecodeMatchesRasterDecodeFor16BitPng`（16-bit）逐字节对拍两条路径，`PngIccComesFromHeader` 与 `DefaultDecodeTakesIccFromJpegHeader` 守住 ICC 语义。

7. **不透明图跳过预乘**：`TryDecodeInto` 写进画布的是直通像素，而 `PremultiplyInPlace` 在 alpha=255 时是恒等变换，此前所有图仍要扫一遍全图（1080p 8.3 MB 读 + 8.3 MB 写，1.04 ms）。改为按来源判断：JPEG 没有 alpha 通道直接跳过；`ImageInfo.Channels` 为 2/4 必定带 alpha 直接预乘；1/3 通道的格式（PNG 的 `tRNS`/palette、GIF 透明索引）先 `IsFullyOpaque()` 实测再决定，避免按格式猜错。`IsFullyOpaque` 同时 SIMD 化：每轮 4 像素把 alpha 洗成连续 16 字节与 255 比对，1080p **0.93 → 0.27 ms**，8.3 MB / 269 µs ≈ 31 GB/s，已到带宽水平。`Premultiply(Canvas)` 在行无 padding 时整块进内核，省掉 1080 次逐行调用。`OpaqueJpegMatchesRasterDecode` / `OpaqueRgbPngMatchesRasterDecode` 逐字节对拍直写与慢路径，`IsFullyOpaqueSeesTransparencyInSimdBody` / `IsFullyOpaqueChecksTailPixels` 守住 SIMD 主体与尾部。

8. **不透明导出走 memcpy**：预乘画布在 alpha=255 时直通值与预乘值相同，`CopyToStraight` 此前仍逐像素 `Unpremultiply`（3 次分支 + 3 次字节写）。现在先 `IsFullyOpaque()`（SIMD，0.27 ms/1080p），全不透明直接转 `CopyToPremultiplied` 整块拷贝：1080p **3.12 → 0.58 ms（5.4×）**。半透明图多付一次 0.27 ms 扫描，相对 5–11 ms 的导出可忽略。`CopyToStraightOpaqueMatchesUnpremultiplyPath` 用 `PixelOps.Unpremultiply` 逐行结果对拍快路径。

9. **半透明满覆盖填充特化**：整行 coverage 恒为 255 时 `a` 就是 `srcA`、源项是常量，每像素从 4 次 `/255` 降到 1 次。新增 `RowBlender.OverSolidFullRow`（`Vector128`，一次 4 像素），`RectangleFill` 的整行全覆盖分支改用它。1024×736 实测 **560 → 235 µs（2.4×）**；`FullRowMatchesGenericFullCoverage`（1/3/4/5/37/100 像素 × 20 组随机数据）与 `OverSolid(dst, color, 全 255)` 逐字节对拍。

10. **图案平铺去掉逐像素取模**：`PatternBrush` 原先每像素一次 `Mod(...)`（`idiv`，~20–40 周期）。瓦片索引在行内是「递增到边界回绕」，改为起步取一次模、之后按瓦片内连续段整段处理，段内走新增的 `RowBlender.OverPattern`（`Vector128`，源与覆盖率都从内存取，复用 `Blend` 内核）。1024×768 平铺实测 **43.0 → 6.8 ms（6.3×）**；`PatternMatchesScalar` 对拍向量/标量两条路径。

11. **整图变换按偏移搬移**：`FlipHorizontal` / `Rotate90` / `Rotate180` / `Rotate270` 的双重循环里每次都调 `GetRow` + `Span.Slice`（等于每像素一次方法调用加边界检查），改为 `MemoryMarshal.GetReference` + `Unsafe.ReadUnaligned<uint>` 直接按 `y * stride + x * 4` 取址，像素按 32 位搬移。1024×768 实测 Flip **246 → 216 µs**、Rotate90 **1433 → 1064 µs**、Rotate180 **707 → 620 µs**；剩余开销是跨行写导致的 cache miss，再往下要分块转置（未做，收益与代码量不成比例）。

12. **导出快路径与通道重排向量化**：`ToBgra32` / `ToRgb24` / `ToGray8` 先 `IsFullyOpaque()`，不透明时免掉每像素 3 次 `Unpremultiply`；BGRA 用一条 shuffle 完成 4 像素 R/B 互换，RGB24 用 shuffle 把 16 字节压成 12 字节（尾部留 4 字节给标量，避免越界写）。1024×768 实测 ToBgra32 **1.62 → 0.62 ms（2.6×）**、ToRgb24 **1.28 → 0.37 ms（3.4×）**、ToGray8 1.17 → 1.01 ms（灰度加权和 SIMD 化性价比低，只做了免除法）。`CopyToStraight` 增加 `bool? assumeOpaque` 重载：`true` 跳过探测、`false` 强制去预乘、`null` 自动判断。

13. **重采样按目标行分带**：中间缓冲原为 `dstW × srcH × 4 float`（4K→1080p 66 MB，直接进 LOH）。改为 8 MB 预算的带：扫一遍 `KernelTable` 求出单行最大窗口，按 `MinSourceRow(destinationY)` 起带、`MaxSourceRow < bandEnd` 收行，累加顺序与整图缓冲完全一致。4K→1080p Lanczos3 实测 **72.1 → 16.9 MB（−77%）**，耗时 133.6 → 133.0 ms（重叠重算 < 5%，被缓存收益抵消）。`BandedMatchesSingleBuffer` 用 `budgetBytes = 0` 强制分带，与 `int.MaxValue` 的单缓冲逐字节对拍。

14. **迁移到 `Sdcb.SimdCodecs` 2.0**（真实收益三处，全部落在加载/保存两端）：
   - **ICC 不再需要完整解码**：2.0 的 `ImageCodecs.TryReadMetadata` 对任何格式都只扫元数据段（JPEG APP2、PNG `iCCP`、WebP `ICCP`、TIFF IFD），于是 `TryDecodeInto` 直写 + 文件头 ICC 可以同时成立。`ImageDecodeOptions.RequireIccProfile` 与 `JpegDecoder.TryReadIccProfile` 特例一起删掉（属性标 `[Obsolete]` 保留源码兼容），PNG/WebP/GIF/BMP 的 ICC 从「留空」变成「免费」。`PngIccComesFromHeader` 与 `DefaultDecodeTakesIccFromJpegHeader` 守住语义，`DirectDecodeMatchesRasterDecode`(/16-bit) 继续逐字节对拍直写与慢路径。
   - **文件加载走内存映射**：`Image.Load(path)` 从 `File.ReadAllBytes` + `TryDecodeInto` 改成内存映射（2.0.0 用 `ImageDecoder.Open(path)` + `ImageFrame.CopyPixels`，0.2.1 起换成同属 `Abstractions` 的 `ByteSource.FromFile`），编码字节不进托管堆，12 MP JPEG 省掉一次 5–10 MB 的分配与整块拷贝（OS 页缓存按需映射给解码器）。`LoadFromFileMatchesLoadFromMemory` 逐字节比对文件与内存两条入口。
   - **保存不再需要 `CopyToStraight`**：不透明画布的预乘缓冲逐字节等于直通缓冲，直接把 `canvas.Buffer` 交给 `PngWriter.Encode` / `JpegEncoder.Encode`；PNG 再加 `PngWriteOptions { DiscardAlpha = true }`，alpha 通道不参与 deflate。800×600 渐变实测编码 **184.8 → 149.7 ms（−19%）**，省掉一张 `W*H*4` 数组；带透明的画布 `Canvas.CopyToStraight` 摊成直通再编（0.2.1 起；0.2.0 用的是元包 `ImageCodecs.Encode` 的预乘直编；0.2.1 的 `Canvas.Encode` / `Save` 扩展又把这块收回到一行，见 §12.2-16）。示例与基准的保存路径已全部改走这条（`docs/compare.jpg` 与迁移前逐字节相同，说明零拷贝路径没有改变输出）。
   - **没做的**：把 `TryDecodeInto` 目标换成 `PixelFormat.Rgba32Premultiplied` 看似能省掉预乘那一趟，实测**是负优化**——`PixelFormat.IsBasic8` 只认 5 种直通格式，预乘目标会让每个编解码器退回 `ImageDecoderFallback.TryDecodeInto`（完整解码 + 转换，多一张整图缓冲），所以目标仍是 `Rgba32`、预乘留在本地做。2.0 的 `DecodeOptions`（缩放/区域解码）也没接到公开 API 上，见 §12.3。
15. **依赖收窄到 `Abstractions`，格式由调用方注册**（0.2.1）：`PackageReference` 从元包 `Sdcb.SimdCodecs` 换成 `Sdcb.SimdCodecs.Abstractions`（0.16 MB 契约层），`ImageDecoderBridge.cs` 里的门面调用（`ImageCodecs.TryReadInfo` / `TryDecodeInto` / `TryDecode` / `TryReadMetadata` / `ImageDecoder.Open`）全部换成自建 `CodecRegistry` 查到的 `ImageDecoderRegistration` 委托。收益：NuGet 依赖不再传递 12 个格式子包（dll 合计 ≈3.1 MB），只 PNG+JPEG 的消费方从 ≈3.1 MB 降到 ≈0.95 MB；代价：`Image.Load` 前必须 `CodecRegistry.Register(...)`，未注册时抛 `InvalidDataException` 并附注册示例。文件路径改用 `ByteSource.FromFile`（同属 `Abstractions`，仍是内存映射）；ICC 交出去前 `ToArray()` 复制一份，避免 `Image` 持有已释放的映射切片（此前靠 `ImageDecoder` 内部实现兜底）。示例/测试/基准各自引 `.Png` + `.Jpeg` 并注册，测试用 `[ModuleInitializer]`。
16. **导出收成一行扩展方法**（0.2.1）：收窄依赖后调用方要自己写 `Create` → `AddFrame` → `WriteRows` → `Commit`，半透明画布还得先 `CopyToStraight` 摊一张 `W*H*4`。新增 `EncoderRegistry`（按 `ImageFormat` 查 `ImageEncoderRegistration`，同格式后注册覆盖先注册）与 `ImageExportExtensions`（`Canvas` / `Image` 上的 `Encode` / `Save`）：把 `canvas.Buffer` + `canvas.Stride` 按 `PixelFormat.Rgba32Premultiplied` 直接交给编码器，unpremultiply 在编码器内部完成，调用方没有临时数组。`EncodePngMatchesStraightConversion` 对拍「`CopyToStraight` + `PngWriter.Encode`」逐字节相同（半透明样本含 a = 0/64/128/255），`StridedCanvasEncodesRows` 守住非紧凑 stride，`SaveRejectsUnknownExtension` / `EncodeWithoutEncoderExplainsHowToRegister` 守住两条错误消息；AOT 冒烟额外跑一次 `image.Canvas.Encode(ImageFormat.Png)` 与 `PngWriter` 输出比对，确认 AOT 下「查注册表 → 调编码器委托 → 会话式写入」整条链路可用。注意这里和 §12.2-14 的「预乘目标」结论不冲突：解码方向不能指定预乘（会退回 fallback），编码方向则必须显式声明预乘，否则编码器会把预乘值当直通像素用。

### 12.3 待评估（按性价比排序）

| 项 | 现状 | 方案 | 预估 |
|---|---|---|---|
| ~~`Unpremultiply` 查表~~ | 已实测否决 | 256×256 `byte` 表（64 KB）替掉每像素 3 次 `idiv` | **负优化**：27.5 vs 10.8 ms；瓶颈不是除法，而是逐行重新进入内核（已由整块调用解决） |
| ~~`Rotate90` / `FlipHorizontal`~~ | 已落地（见 §12.2-11） | ref + 32 位搬移 | 1.2–1.35×，已到 cache 边界 |
| ~~`PatternBrush.Blend`~~ | 已落地（见 §12.2-10） | 分段步进 + 段内向量内核 | **6.3×** |
| ~~半透明填充~~ | 已落地（见 §12.2-9） | `OverSolidFullRow` 常量源特化 | **2.4×** |
| ~~解码入口 `ToRgba8()` / 直写~~ | 已落地（见 §12.2-4/6） | 默认 `TryDecodeInto` 直写；HDR/float 才回退 `TryCopyTo` | 1080p 16.1 → 10.9 ms，分配少 7.9 MB |
| ~~编码前 `CopyToStraight`~~ | 已落地（见 §12.2-14） | 不透明画布直喂编码器 + `DiscardAlpha` | 800×600 PNG 184.8 → 149.7 ms（−19%） |
| ~~文件加载 `File.ReadAllBytes`~~ | 已落地（见 §12.2-14/15） | `ByteSource.FromFile(path)` 内存映射 | 12 MP JPEG 省一次 5–10 MB 分配与拷贝 |
| 缩放/区域解码（2.0 `DecodeOptions`） | 未暴露 | JPEG 可在 DCT 域直接出 1/2…1/8 图、只解一块区域 | 缩略图流水线收益极大（1/8 = 少算 98% 像素）；要定尺寸协商（`TryGetClosestSize`）与 `Orientation` 交互，留到 0.4 |
| ~~`Resizer` 中间缓冲~~ | 已落地（见 §12.2-13） | 8 MB 预算分带 | 4K 分配 −77%，速度不变 |
| 渐变混合内核 | 11.3 ns/px（1024×768 实测 8.5 ms） | 2 停特化、覆盖 255 且不透明时跳过 `BlendPixel` | 已做过差分游标/预计算倒数；再往下要改 `t` 的逐像素累加顺序，会破坏逐字节一致（§12.4），不做 |
| `ScanlineRasterizer` | 每子扫描线排序 + 浮点累加 | 定点覆盖 + AET 增量排序 | 中等，改动面大 |

### 12.4 明确不做

- `Canvas.GetRow` 去掉边界检查：每行几纳秒，1080 行不到 5 µs。
- 全面升级 `Vector256`：混合内核 4→8 像素/轮约 1.5×，代码量与回退路径翻倍。
- 字体/字形相关路径（`TextDrawing`、`GlyphRaster`、`TextFont`）暂不优化。
- 影响像素结果的改写：所有 SIMD 版本必须与标量逐字节相同，否则 AOT 参考哈希（misc §Y）与既有基线图会失效。
