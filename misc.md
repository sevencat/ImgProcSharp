# 杂项、决策记录与坑

`plan.md` 放计划和设计，这里放其余全部：许可证与来源、依赖现状、拷贝改造清单、算法笔记、限制、未决问题、被否决的方案。

---

## A. 许可证与代码来源

| 项目 | 许可 | 处理 |
|---|---|---|
| 本仓库 | Apache-2.0（`LICENSE` 已存在） | 保持 |
| `Sdcb.SimdCodecs` | MIT | 通过 NuGet 引用 1.4.0（只用解码），不拷源码 → 无许可证冲突，也不需要在包内附它的 NOTICE |
| PNG / JPEG 编码 | 不引入 | 用 `Sdcb.SimdCodecs.Png.PngWriter` 与 `Sdcb.SimdCodecs.Jpeg.JpegEncoder`（随解码依赖已存在），本仓库不含编码代码 |
| GlyphRaster（`F:\code\zlmsharp\src\GlyphRaster`） | 作者自有代码（zlmsharp 仓库内） | 直接拷入 `ImgProcSharp.Core/Text`，保留文件头注释 |
| 文泉驿微米黑 | Apache-2.0 / GPLv3 双许可，**含字体嵌入例外** | 随包分发，必须带 `licenses/LICENSE-WenQuanYiMicroHei.txt`；文件内容不得修改 |

Clean-room 声明（写进 README）：

- 本库的 API **形状**参考了 SixLabors.ImageSharp.Drawing 的公开文档（`Fill` / `Draw` / `Mutate` / `IBrush` / `IPen` 这类命名），**实现全部自研**。
- 未阅读、未反编译、未逐行对照 ImageSharp / SkiaSharp 的源码。若本库最终并入 `SimdCodecs` 仓库，这条是 `AGENTS.md` 的硬性要求。
- 算法来源：字形光栅化沿用自有 GlyphRaster；通用路径填充是扫描线 + 超采样的教科书算法（Foley/van Dam、Abrash 的 coverage 思路）；描边是折线偏移近似。

---

## B. `Sdcb.SimdCodecs` 依赖：只取解码，固定 1.4.0

### 查证到的事实

- nuget.org 上 `Sdcb.SimdCodecs` 只有 **1.0.1 / 1.1.0 / 1.2.0 / 1.3.0 / 1.4.0**（`dotnet package search --exact-match` 实测），**没有 2.0**。
- `F:\code\SimdCodecs` 工作区 tag 最高 **v1.4.0**，`VersionMajorMinor` 已是 **2.0**，即 2.0 未发版。
- 1.4.0 的**门面**只有解码（`SimdCodecs` 类里没有 `Encode`）；但格式子包自带编码器：`Sdcb.SimdCodecs.Png.PngWriter.Encode(rgba, w, h, options)` 与 `Sdcb.SimdCodecs.Jpeg.JpegEncoder.Encode(pixels, w, h, channels, options)` 都是公开 API，且随元包传递引用（只用 `PackageReference Sdcb.SimdCodecs` 就能编过）。这一点一开始按「1.4.0 完全没有编码」处理过，实测是错的。
- 门面**有** `SimdCodecs.TryReadInfo(data, out ImageInfo)`，`ImageInfo` 带 `Format` / `Width` / `Height` / `Orientation` / `FrameCount`，所以 EXIF 方向不用自己解析。

因此 v1 不自己写编码器（见 §Q），**也不被 2.0 的发版节奏阻塞**。

### 1.4.0 能提供什么（已核对 v1.4.0 源码）

```csharp
// 门面（Sdcb.SimdCodecs）
SimdCodecs.DetectFormat(ReadOnlySpan<byte>) -> ImageFormat
SimdCodecs.TryDecode(ReadOnlySpan<byte> data, out IDecodedImage? image) -> bool
SimdCodecs.TryDecodeInto(ReadOnlySpan<byte> data, Span<byte> dest, int stride, ImagePixelFormat format) -> bool
SimdCodecs.TryOpenFrames(ReadOnlyMemory<byte> data, out IImageFrameReader? reader) -> bool
SimdCodecs.IsDecodable(ImageFormat) / CanDecode(ReadOnlySpan<byte>)

// IDecodedImage（保真层）
int Width, Height, Channels; SampleFormat SampleFormat; ReadOnlySpan<byte> Pixels;
ReadOnlySpan<byte> IccProfile;  // 有 ICC
byte[] ToRgba8();               // display tier：4 通道 RGBA8，紧密排列
```

- `ImagePixelFormat` 在 1.4.0 是 **enum**（`Rgba32/Bgra32/Rgb24/Bgr24/Gray8`），2.0 改成了 `readonly struct PixelFormat`。
- 1.4.0 **没有** Exif / Xmp 元数据 API（只有 `IccProfile`），但 `ImageInfo.Orientation` 已经覆盖了「把图转正」这个唯一需求。
- `ImageInfo.Orientation` 是 `ImageOrientation` 枚举（`Normal`/`FlipHorizontal`/`Rotate180`/`FlipVertical`/`Transpose`/`Rotate90`/`Transverse`/`Rotate270`），与 EXIF 0x0112 的 1–8 一一对应。

### 取用方式（v1）

```
ImageDecoderBridge.Decode(byte[] data) -> Image
    SimdCodecs.TryReadInfo(data, out info)          // Format / Orientation / 尺寸
    SimdCodecs.TryDecodeInto(data, canvas, Rgba32)  // 直写画布，无中间数组
    不透明图跳过，否则 premultiply → Canvas          // plan §12.2-7
```

代价：比 2.0 的 `ImageDecoder.Open + ImageFrame.CopyPixels` 多一次整图拷贝（12 MP ≈ 5–10 ms），换来的是「今天就能 `PackageReference 1.4.0`」。v1 接受。

### 升级到 2.0 时只改一个文件

`ImageDecoderBridge.cs` 里的两个入口改成：

```csharp
using var dec = ImageDecoder.Open(data);          // 流式，不整图分配
var frame = dec.GetFrame(0);
frame.CopyPixels(null, PixelFormat.Rgba32, canvas.Buffer, canvas.Stride);
metadata = dec.Metadata;                          // Exif / Icc / Xmp 都回来了
orientation = ExifReader.FromBytes(metadata.Exif)?.Orientation;
```

同时可以删掉 `ExifOrientationReader.cs`，并顺手恢复 Xmp/Exif 透传。除此之外没有任何文件需要动——`ImagePixelFormat` → `PixelFormat` 的枚举/结构体差异也全被这个文件吸收。

验证：`dotnet list package --include-transitive` 确认只有 NuGet 依赖；`unzip -l *.nupkg` 确认 `Sdcb.SimdCodecs.dll` 没被塞进 `lib/`（它是 NuGet 依赖，不该内嵌）。

---

## C. GlyphRaster 拷贝清单与改造点

### 要拷的 12 个源文件 + 2 个资源

```
EmbeddedFonts.cs          2.6 KB   gzip 解压 + 资源读取
TrueTypeFont.cs          15.3 KB   sfnt 目录、head/hhea/maxp/hmtx/cmap/loca
TrueTypeFont.Glyphs.cs    8.1 KB   cmap 解析、复合字形、轮廓读取
GlyphOutline.cs           0.9 KB   轮廓点模型
GlyphRasterizer.cs        9.9 KB   字形 → A8 覆盖位图（扫线 + 纵向 4x 超采样）
GlyphMask.cs              1.9 KB   A8 位图 + OffsetX/OffsetY/Advance
GlyphPath.cs              3.1 KB   轮廓 → PathSegment 序列
PathCollection.cs        11.9 KB   VectorPath/PathCollection/PathBounds/PathPoint/PathSegment/PathVerb
TextFont.cs               9.1 KB   字体门面 + (码点,字号) mask 缓存
TextFont.Layout.cs        5.4 KB   排版 / 测量 / 整段文字矢量路径
TextLayout.cs             1.8 KB   TextLayoutOptions / PositionedGlyph / TextLayout
TextPainter.cs            4.6 KB   mask 混合到 RGB24 / BGRA32 缓冲
Fonts/WenQuanYiMicroHei.ttf.gz        2.0 MB
Fonts/LICENSE-WenQuanYiMicroHei.txt   575 B
```

### 命名空间与可见性（统一规则）

| 命名空间 | 放什么 | 可见性 |
|---|---|---|
| `ImgProcSharp` | 全部公开 API：`Canvas`、`Color`、`Path`、`Brush`、`Pen`、`DrawingContext`、`Image`、`TextFont`、`TextOptions`、`TextAlign` | `public` |
| `ImgProcSharp.Text` | 从 GlyphRaster 拷来的字形引擎：`TrueTypeFont`、`GlyphOutline`、`GlyphRasterizer`、`GlyphMask`、`GlyphPath`、`VectorPath`、`PathCollection`、`PathVerb`、`EmbeddedFonts`、`BuiltInFont`、`TextLayout`、`TextLayoutOptions`、`PositionedGlyph`、`TextPainter`、`RgbColor` | `internal` |

一条规则就够：**消费者只 `using ImgProcSharp;`，引擎细节不出现**。好处除了 API 干净，还让「不用改名」成立——`PathCollection`/`VectorPath` 与核心 `Path` 分处两个命名空间且不可见，没有 `CS0104` 歧义风险。

### 逐文件动作

| 文件 | namespace | 额外改动 |
|---|---|---|
| `TextFont.cs`、`TextFont.Layout.cs` | `ImgProcSharp`（+ `using ImgProcSharp.Text;`） | 类保持 `public`；`GetGlyphMask/GetGlyphPaths/Layout/GetTextPaths` 降 `internal`；`LoadBuiltIn(BuiltInFont)` 降 `internal`（只留 `BuiltIn`） |
| `TrueTypeFont.cs`、`TrueTypeFont.Glyphs.cs` | `ImgProcSharp.Text` | 全部类型 `internal`；`partial` 声明同步改 |
| `GlyphOutline.cs`、`GlyphRasterizer.cs`、`GlyphMask.cs`、`GlyphPath.cs` | `ImgProcSharp.Text` | 类型 `internal`；`GlyphMask` 的 padding 字段保留（混合内核对齐用） |
| `PathCollection.cs` | `ImgProcSharp.Text` | `VectorPath/PathCollection/PathVerb/PathPoint/PathSegment/PathBounds` 全 `internal` |
| `TextLayout.cs` | `ImgProcSharp.Text` | `TextLayout/TextLayoutOptions/PositionedGlyph` 全 `internal` |
| `TextPainter.cs` | `ImgProcSharp.Text` | `internal`；**保留但只作测试基准**（验收里逐字节比对用），不参与绘制路径；`RgbColor` 一并 internal |
| `EmbeddedFonts.cs` | `ImgProcSharp.Text` | 资源名 → `ImgProcSharp.Text.Fonts.WenQuanYiMicroHei.ttf.gz` |

其余动作：

1. **`InternalsVisibleTo`**：GlyphRaster.csproj 里的 `ZlmSharp.Tests` 不要带过来；由打包工程给 `ImgProcSharp.SimdCodecs.Tests` 声明，核心工程给 `ImgProcSharp.Core.Tests` 声明。
2. **丢弃**：`GeneratePackageOnBuild`、`PackageId Sevencat.GlyphRaster`、`Authors` 等打包属性一律不带；核心工程 `IsPackable=false`。
3. **语言版本**：源码是 C# 12（zlmsharp `LangVersion 12.0`，全文件无 `field` 标识符，已核查），在 net10/LangVersion 14 下可正常编译；核心工程保持 12.0 以免引入 net8 编不过的语法。

### 与核心几何的桥接

引擎的 `PathCollection`（二次贝塞尔 + `PathVerb`）不能直接喂核心光栅化器，需要两个 `internal` 转换，放在 `Paths/GlyphPathBridge.cs`：

```csharp
internal static class GlyphPathBridge
{
    internal static Path FromGlyphPath(Text.GlyphPath glyphPath);      // 单字形
    internal static Path FromPathCollection(Text.PathCollection paths); // 整段文字（慢路径）
}
```

转换只做「二次贝塞尔 → 核心 `Segment`」，不做坐标系变换；缩放/旋转由 `DrawingContext` 的变换矩阵负责。

---

## D. 为什么内部用预乘 alpha

`over` 在预乘空间里是纯线性运算：

```
out = src_premult + dst_premult * (1 - src_a)
```

逐通道展开只有乘加，没有除法；直通 alpha 则需要

```
outA = sa + da*(1-sa)
outC = (srcC*sa + dstC*da*(1-sa)) / outA        ← 除法 + outA==0 特例
```

在 8-bit 上每像素两次除法，代价高且要处理 alpha=0。ImageSharp/Skia 内部同样是预乘。

代价与处理：

- 存图前需要 unpremultiply（`c/a`），只在边界发生一次；**画布本来全不透明时（从 JPEG 读入、或先 `Fill` 了不透明底色）可以整段跳过**——已落地为 `CopyToStraight` 里的 `IsFullyOpaque()` 快路径（plan §12.2-8）。
- 8-bit 预乘有精度损失（低 alpha 的深色会丢色阶）。缓解：unpremultiply 用 `(c * 255 + a/2) / a` 的定点实现，并对 `a < 8` 的像素直接输出 `(0,0,0,0)`，避免噪声被放大。
- 定点乘 255 的常用加速：`Div255(x) = t = x + 0x80; (t + (t >> 8)) >> 8`，比 `(x + 127) / 255` 快且四舍五入等价。

---

## E. 内嵌字体

- 文件：`WenQuanYiMicroHei.ttf.gz`，gzip **2,104,595 B**（原始 TTF 4.35 MB），来源与 MD5 记录在 `LICENSE-WenQuanYiMicroHei.txt`（OpenCV 5 `WITH_UNIFONT` 用的同一个文件）。
- 覆盖中日韩，缺彩色 emoji、缺部分生僻字。
- 三种方案：

| 方案 | 优点 | 缺点 |
|---|---|---|
| **v1：随主包内嵌** | 开箱可用，无 IO、无路径问题，AOT 安全 | 包大 2 MB |
| 独立 `ImgProcSharp.Fonts` 包 | 主包轻 | 用户要多装一个包，默认字体还得有回退 |
| 运行时加载 ttf 文件 | 包最小 | 用户必须自备字体，示例跑不起来 |

- 后续优化：加一个 `TextFont.FromFile(path)`（GlyphRaster 已有 `LoadFromFile` 的能力），让不想要内置字体的人可以裁剪——但如果主包内嵌了资源，裁剪要靠 MSBuild 开关（`-p:ImgProcSharpNoBuiltInFont=true` 时排除 `EmbeddedResource` 并把 `TextFont.BuiltIn` 改成抛异常），成本不低，0.2 再说。
- 字体许可要求：分发时保留版权与许可文本 → `PackagePath="licenses"`；若将来内置第二种字体（如思源黑体 SIL OFL），同样处理并注意 OFL 的保留名条款。

---

## F. 光栅化算法笔记

GlyphRaster 的 `GlyphRasterizer` 是「每个字形、A8 位图」定制的，直接拿来画任意路径需要泛化三层：

1. **输入泛化**：字形轮廓（二次贝塞尔 + 隐含闭合）→ 任意折线（`Flatten` 之后的点列，可开可闭，可有多个子路径）。
2. **填充规则**：字形固定用非零环绕；通用路径要支持 `NonZero` / `EvenOdd`（描边展开后的自交区域必须靠非零自然并集）。
3. **输出复用**：字形缓存 mask；通用绘制要池化一块 `Width×Height` 的 coverage 缓冲，逐次清零复用。

其余保持不变：纵向 4 次超采样 + 横向按边精确积分覆盖 → 8-bit coverage。误差与性能预期：

- 水平/垂直线、整像素矩形：cov 精确 255/0，无灰边。
- 45° 斜线：单像素误差 ≤ 1/255 量级，边缘视觉可接受。
- 1000×1000 的实心圆：约 1–3 ms（标量）/ 0.5–1 ms（SIMD 混合阶段）；瓶颈通常在混合而非扫描线，除非路径极复杂。

优化顺序（不要过早）：先正确 → 再 profile → 只优化混合阶段与「覆盖行跳过（整行 cov 全 0 或全 255）」。

---

## G. 描边算法笔记

> **落地情况**：M5 先做了圆头端点 + 圆角连接，M6 补齐了 Miter/Bevel/Square cap 与 Dash（见 §Z）。下面的伪代码是设计时的「完整目标」，`StrokeOutline.Build(flattened, width, style)` 现在与它一一对应。

`StrokeExpander.Expand(path, pen) → Path`（同向闭环，喂给 `NonZero`）：

```
pts = path.Flatten(0.25) 的每段点列
for 每段 (a,b):
    n = normalize(perp(b-a)) * (width/2)
    加入四边形 [a+n, b+n, b-n, a-n]（同向）
for 每个接头（含闭合路径首尾）:
    Miter 且 夹角 > MiterLimit → 退化为 Bevel
    Miter: 加入尖角四边形（1/sin(θ/2) 延伸，超限则 Bevel）
    Bevel: 加入三角形
    Round: 加入以接头为圆心、半径 width/2 的扇形（按角度细分）
for 开放路径两端:
    Butt: 什么都不加
    Square: 沿切向延伸 width/2 的矩形
    Round: 半圆
```

关键点：

- **不做多边形布尔运算**：所有子形状同向 + 非零环绕 = 自动并集。这是整个方案能落地的原因。
- 退化情况：零长段（`a == b`）跳过；宽度 ≤ 0 不画；`NaN/Inf` 坐标跳过整段（不崩）。
- dash：把 flatten 后的折线按「累计弧长 / 图案」切成若干开段，每段独立做上面流程（两端按 `LineCap` 补端帽，段内折角仍用 `LineJoin`）。闭合路径上跨过起点的两段要合并，否则起点会被当成两个端点各补一次端帽。
- 特化快路径（外矩形 + 内矩形 → 环带、两个圆环、4 直条 + 4 扇环）不做：M7 基准显示描边成本主要花在扫描线上（已加活动边表），而通用展开的边数与之同量级；多一条独立几何路径就要多一份 AA/裁剪/半透明一致性的验证成本，收益不划算。

---

## H. 文字的已知限制（继承自 GlyphRaster）

| 限制 | 后果 | 补救 |
|---|---|---|
| 笔位取整 `(int)MathF.Round(penX)` | 文字不能亚像素定位，缩放后间距不均 | 需要时走矢量慢路径（`GetTextPaths`）再自己按浮点平移 |
| 无 kerning（不读 `kern`/GPOS） | 英文 `AV`、`To` 间距偏松 | 需要时自建 kerning 表（GlyphRaster 未读，需扩展解析器） |
| 无 GSUB 整形 | 阿拉伯文/印度语系不连写 | v1 不支持，文档写明 |
| 无 hinting | 小字号（<10px）笔画发虚 | 用更大的字号画再缩，或接受 |
| 不支持 CFF/OTF（`OTTO`） | `.otf` / 部分 `.ttc` 字体加载抛 `NotSupportedException` | 文档写明只能用 TrueType 轮廓的 `.ttf` |
| 无彩色字形（CBDT/COLR） | emoji 只能画成黑白或空白 | v1 不支持 |
| 无双向文本（bidi） | 混排 RTL 会顺序错 | v1 不支持，调用方自己切分 |
| 掩码是轴对齐 A8 位图 | 不能旋转/斜切，只能平移 | `Transform` 非恒等时 `DrawText` 自动转 `FillText` 轮廓路径（§X） |

---

## I. 相对 ImageSharp.Drawing 的差距（v1 取舍依据）

| 能力 | ImageSharp.Drawing | ImgProcSharp v1 |
|---|---|---|
| 填充、描边、AA | ✔ | ✔ |
| 描边样式（join / cap / miter limit / dash） | ✔ | ✔（M6，§Z） |
| 路径（line/quad/cubic/arc） | ✔ | ✔（arc 转 cubic） |
| 渐变（线性/径向/扫描） | ✔ | 线性/径向；扫描渐变 0.2 |
| 图案笔刷 / 图片笔刷 | ✔ | 图案；图片笔刷 0.2 |
| 混合模式 | ✔（多种） | 只有 source-over |
| 路径裁剪 | ✔ | M7 |
| 文字 | 完整整形/字体集合 | GlyphRaster 子集（见 §H） |
| 滤镜/卷积/模糊 | ✔（大量） | 不做（那是另一层） |
| 多帧/动画 | ✔ | 只首帧 |
| 色彩管理 | ✔ | 不做 |
| 变换矩阵 | ✔ | `Matrix3x2` 一处；线宽随缩放、笔刷随逆矩阵采样（§X、§Y） |

这张表也是「不追求全量」的书面依据：把 ImageSharp.Drawing 全量对齐是几个月量级，而「画框、画字、存图」只需要上表 ≤ 6 个 ✔。

---

## J. 像素格式转换矩阵

| 场景 | 走哪条 |
|---|---|
| 解码 → 画布 | 默认 `TryDecodeInto(..., ImagePixelFormat.Rgba32)` 直写画布缓冲；`RequireIccProfile = true` 时 `TryDecode` → `PixelConverter.TryConvert` 逐行转换 |
| 画布 → PNG | `Canvas.CopyToStraight` → `Sdcb.SimdCodecs.Png.PngWriter.Encode`（调用方/测试侧，不进本库） |
| 需要 16 位保真 | v1 不支持，文档写明「解码即降到 8 位、输出即 8 位」 |
| 灰度图输入 | `ToRgba8()` 负责展成 4 通道，画布不用区分 |
| 输出无 alpha 需求 | 全不透明时可跳过 unpremultiply，且 PNG 仍是 RGBA（文件略大，逻辑零分支） |
| 内部其他格式（Bgra32 等） | `Canvas` 只存 Rgba32；提供 `PixelOps.ToBgra32/ToRgb24/ToGray8` 供调用方导出 |

`SimdCodecs.TryReadInfo` 在 1.4.0 门面里确实有，所以 `TryDecodeInto(..., ImagePixelFormat)` 可用，并且已经是默认路径。它唯一的缺口是 ICC：JPEG 能用 `JpegDecoder.TryReadIccProfile` 只扫 APP2 段补上，PNG 的 `PngReader.ReadHeader` 是 internal、WebP 的 ICCP 要先解码，因此 ICC 做成 `RequireIccProfile` 开关（见 plan §12.2-6）。

---

## K. 色彩管理缺失的具体后果

`ToRgba8()` 是「display tier」：把原生位深/色彩空间转成 8 位 RGBA，**不做 ICC → sRGB 的变换**。因此：

- 带 Display P3 / AdobeRGB ICC 的图，画出来的颜色与浏览器/看图软件相比会偏（通常偏艳）。
- 我们存图时把 ICC 原样写进 `iCCP` 块，所以「偏」在链条上是一致的——看图软件会按原 ICC 解释。
- 如果调用方要求严格一致：在 `Load` 后自行做一次 ICC→sRGB 转换（本库不做；需要 ICC 时用 `RequireIccProfile = true` 加载），输出时不再写 ICC、改让 `sRGB` 块生效。
- 文档里必须写这条，否则第一个 issue 就是「颜色不对」。

---

## L. 分析器、AOT 与警告策略

- **`TreatWarningsAsErrors`**：核心工程建议**开**（新代码，标准自己定）；打包工程**不开**——它编译的是同一批源码，但环境是 net10 SDK，分析器规则集比 net8 严（例如 CA1515「考虑把类型设为 internal」），会让「拷进来的老代码」直接变成错误。折中：打包工程 `WarningsAsErrors` 只针对 `CS` 系列。
- **`IsAotCompatible=true`** 两个工程都开。GlyphRaster 是纯托管、无反射、无动态代码（只读资源 + `GZipStream`），AOT 安全；新增代码别引入 `Activator`、`Type.GetType`、序列化。
- **`PublicAPI` 分析器**：本仓库是独立仓库，只有打包工程对外，建议**只给 `ImgProcSharp.SimdCodecs` 开**，并且把核心的公开类型也列进它的 `PublicAPI.Shipped.txt`（因为它们确实出现在最终程序集的公开 API 里）。核心工程不开，避免两份清单。
  - 若将来并入 SimdCodecs 仓库：注意它的 `Directory.Build.props` 用「路径含 `/src/`」判断给所有工程注入分析器。那时把核心放到 `core/` 而不是 `src/` 下，否则自动获得第二份 API 清单更麻烦。
- **`NoWarn`**：`CS1591`（缺 XML 注释）忽略，其余不忽略。
- 允许 `unsafe`：混合内核需要 `Unsafe.As`/`Span` 指针；两工程都 `AllowUnsafeBlocks=true`（打包工程也必须，否则编不过）。

---

## M. 示例图片与测试素材

- 需要一张输入图 `samples/assets/sample.jpg`，要求：**CC0 / Public Domain**，尺寸 ≤ 1600×900，文件 ≤ 300 KB，含人像或风景（文字叠加效果明显）。
  - 建议来源：Wikimedia Commons 的 CC0 分类、Pexels/Unsplash 的 CC0 声明图（记录 URL + 作者 + 许可 + SHA-256 到 `samples/assets/README.md`）。
- 备选（完全零风险）：**用代码生成输入图**（渐变 + 几个色块），不提交任何二进制。缺点是不像真实照片，效果说服力弱。
- 已按「两者都做」落地：`samples/ImgProcSharp.Samples` 先找 `samples/assets/sample.jpg`，找不到就 `BuildSamplePhoto()` 合成一张 1280×720 的风景（天空渐变 + 太阳光晕 + 两层山脊 + 波光图案笔刷），**并用 `JpegEncoder` 编码成真 JPEG 字节**再交给 `Image.Load` 解码——这样即便没有素材，走的也是真实的「JPEG 字节 → 解码 → 绘制 → 编码」链路，而不是绕过解码器直接拿内存画布。
- 测试素材规则（沿用 SimdCodecs 的习惯）：大图不入库，运行期下载 + 校验哈希；小图（≤300 KB）允许入库但要记来源。测试里生成的图（棋盘、噪点、纯色）优先于素材文件。

---

## N. 性能目标（不过早优化）

| 操作 | 目标（1920×1080，Ryzen 笔记本量级） |
|---|---|
| 覆盖全画布的实心矩形 | < 3 ms |
| 100 个文字字形（字号 32） | < 5 ms |
| 1000 段折线描边（宽 2） | < 15 ms |
| 解码 4000×3000 JPEG + 画 + 存 PNG | 与「只解码 + 只编码」相比额外开销 < 15% |

方法：先正确，再用简单 Stopwatch 基准；只有端到端数字能决定优化方向。混合内核是唯一值得上 SIMD 的地方（形状绘制的时间大部分花在混合，而不是扫描线）。

---

## O. 未决问题

1. **输出要不要在 0.1 就支持 JPEG？** 结论：支持，但不自己实现——直接用 1.4.0 的 `Sdcb.SimdCodecs.Jpeg.JpegEncoder`（§Q），示例的照片管线就走这条链，不必等 2.0 的门面 `Encode`。
2. **示例图**：提交 CC0 照片，还是代码生成？（见 §M）
3. **`Mutate` 的返回类型**：返回 `Image`（可链式 `Save`）还是 `void`（更干净）？当前计划返回 `Image`。
4. **文字快路径的亚像素**：是否在 `GlyphMask` 混合时按 `penX` 的小数部分做水平线性插值（≈0.3 天，能明显改善字距）？v1 先不做，留 0.2。
5. **是否暴露 `Canvas`（预乘）给用户直接写像素**：暴露就得文档解释预乘；不暴露则高级用户受限。当前计划暴露 + 文档说明。
6. **描边 dash 要不要进 v1**：v1 不进（M5 只有圆角连接 + 圆头端点），M6 已补齐，图案单位取「线宽倍数」与 ImageSharp 一致（§Z）。
7. **`PatternBrush` 的平铺相位**：跟随变换矩阵还是屏幕空间？当前计划跟随变换（与渐变一致）。
8. **包名**：`ImgProcSharp`（当前）vs `Sdcb.SimdCodecs.Drawing`（与其他包同族）。若最终并入 SimdCodecs 组织，建议后者；独立仓库用前者。名称影响命名空间（`ImgProcSharp` vs `Sdcb.SimdCodecs.Drawing`），越早定越好。

---

## P. 被否决的方案

1. **`ProjectReference` 核心工程 + `PrivateAssets=all` 打包**：`PrivateAssets` 只影响 NuGet 依赖清单，不会把 dll 编进自己的程序集；消费者运行时仍需要那个程序集 → 直接崩。真正的「源码内嵌」只有 `Compile Include` 一条路。
2. **核心与扩展各发一个包（方案 B/C）**：同一类型出现在两个程序集里，消费者同时安装即 `CS0433`。除非给所有类型套 `#if` 改命名空间 + 降级为 `internal`（SimdCodecs 的 `src/Shared` 就是这么干的），维护成本高于收益。既然 SimdCodecs 本身就是 net10-only，net8 用户注定用不了扩展包，发两个包没有意义。
3. **核心直接用 `RasterImage` 当画布**：`RasterImage.Pixels` 是紧密排列、无 stride、且暴露为只读 span；绘图需要任意 stride 与可写缓冲。改为自建 `Canvas`，需要 `IDecodedImage` 时再 `RasterImage.Wrap` 零拷贝包装。
4. **等 SimdCodecs 2.0 的编码 API 再开工**：2.0 未发版（nuget 最高 1.4.0），但 1.4.0 的 `PngWriter` 已经够用，没必要等；也不需要自研（§Q）。
5. **用 SkiaSharp / System.Drawing 画**：引入原生依赖，破坏 AOT/跨平台/纯托管的前提（这也是 GlyphRaster 存在的理由）。
6. **画布支持全部 `PixelFormat`**：混合内核要写 8 套（3/4 通道 × 8/16 位 × 直通/预乘），收益（省一次转换）远小于成本。v1 单格式，边界转换。
7. **把 GlyphRaster 的 `PathCollection` 直接当核心路径类型**：只有二次贝塞尔（无 cubic）、且 `Transform` 每次分配大量对象；核心另建 `Path`，另提供转换。
8. **自己做 JPEG 编码**：baseline JPEG 要 DCT、量化表、哈夫曼表、色度下采样，600+ 行，画质调优是无底洞。v1 出 PNG。
9. **BMP / QOI / PPM 当唯一输出**：编码确实只要 30 行，但 BMP 体积是 PNG 的 3–10 倍，QOI/PPM 几乎没有查看器支持——示例产出没人能直接打开，等于没做完。
10. **System.Drawing.Common / SixLabors.ImageSharp 编码**：前者 Windows-only 且官方已不建议跨平台使用，后者被明确排除。

---

## Q. 输出编码：不自研，直接用 1.4.0 的 PngWriter / JpegEncoder

### 结论

`Sdcb.SimdCodecs` 1.4.0 的格式子包自带编码器（`Sdcb.SimdCodecs.Png.PngWriter`、`Sdcb.SimdCodecs.Jpeg.JpegEncoder`，都是公开 API，随元包传递引用），「画布 → 文件」不需要本库实现：

```csharp
byte[] straight = new byte[canvas.Width * canvas.Height * 4];
canvas.CopyToStraight(straight, canvas.Width * 4);

byte[] png = PngWriter.Encode(straight, canvas.Width, canvas.Height);                       // 无损
byte[] jpg = JpegEncoder.Encode(straight, canvas.Width, canvas.Height, 4, new JpegEncodeOptions
{
    Quality = 92,                 // 1..100
    OptimizeHuffman = true,
    IccProfile = image.IccProfile, // 原样带走色彩空间，空则写不进段
});
```

- **不放进公开 API**：v1 没有 `Image.Save` / `EncodePng` / `EncodeJpeg`。多格式输出、`PngOptions`（压缩级别、iCCP、sRGB 块）、JPEG 的 `Subsampling`/`Progressive` 等真实需求出现再包一层，不为「可能的调用方」提前维护编码器。
- **只用在测试与示例里**：解码测试需要 PNG 素材，直接用 `PngWriter` 现造，省掉 `assets/` 里的二进制文件；示例的照片管线则用 `JpegEncoder` 造素材 + 出成品，一条命令跑通「解码 → 绘制 → 编码」。
- **为什么不自研**：原本计划手写 200 行（IHDR/IDAT/IEND + 滤波 + CRC32 + `ZLibStream`）。1.4.0 已有公开 `PngWriter` 后，自研的唯一收益是「不依赖第三方编码」——但包本来就依赖 `Sdcb.SimdCodecs` 解码，多这一个类型不增加依赖面，反而多 200 行要维护、要调滤波启发式、要背预览器兼容性。已落地过的 `PngCrc32.cs` 一并删掉。JPEG 更极端：baseline DCT + 量化表 + 霍夫曼表是几千行的坑，绝不自研。

### 为什么示例产出 PNG 与 JPEG 各一张

| 格式 | 体积 | 查看器支持 | 说明 |
|---|---|---|---|
| **PNG** | 中 | 全平台原生 | 矢量海报与对比拼图用它（`result.png`、`compare.png`），`PngWriter` 一行 |
| BMP | 大 | 全平台 | 32 bpp 无压缩，示例图能到几 MB，不选 |
| **JPEG** | 小 | 全平台 | 照片管线用它（`photo.jpg`），拼图的 JPG 版（`compare.jpg`）则当 README 首页预览；有损压缩会放大描边锯齿，正好当「肉眼验收」的对照，与原图并排拼在一张图里对比 |

### 用法与注意点

- **直通像素**：两个编码器都收非预乘 RGBA8，`Canvas` 是预乘的，必须先 `CopyToStraight`（内部做 unpremultiply；`a<255` 时有 ±1 量化误差，这正是解码保真测试允许 ±1 的来源）。
- **JPEG 的入参**：`Encode(pixels, w, h, channels, options)` 的 `channels` 给 4 就按 RGBA 逐行读、alpha 直接忽略；`stride` 必须是 `w * channels`（紧密排列，`straight` 正好满足）。给 3 则要求 RGB 缓冲，需要自己拆通道。
- **不要拿它做逐字节回归基准**：PNG 不同版本可能选不同行滤波，JPEG 的熵编码、量化表也可能变；断言要落在「解码回来的像素」上。
- **方向**：`Image.Load` 之后先 `ApplyOrientation()` 再画，`JpegEncoder` 不会替你写 EXIF 方向（`JpegEncodeOptions.Exif` 传原图 EXIF 会连旧方向一起写进去，示例故意不传）。

### 验证方式

```
PngWriter 写 PNG → Image.Load 解码 → 与原始 Canvas 逐像素比对（±1）
JpegEncoder 写 JPEG → Image.Load 解码 → 尺寸/朝向正确、像素差在 lossy 预期内（示例肉眼验收）
```

第一条往返覆盖解码桥的尺寸、格式、行切片、预乘；编码器本身是第三方库，不在本库的单元测试责任内，所以 JPEG 只做端到端示例，不写像素级断言。

---

## R. 实现笔记（M0–M1 已落地）

- **CPM**：开 `ManagePackageVersionsCentrally` 后，`PackageReference` 上再写 `Version` 会报 NU1008；版本只在 `Directory.Packages.props` 出现一次。
- **`.slnx`** 需要 SDK ≥ 9.0.200（实测 10.0.101 正常），`dotnet build/test` 直接吃它。
- **验证嵌入资源名**不必写代码：PowerShell `[Reflection.Assembly]::LoadFile('...ImgProcSharp.dll').GetManifestResourceNames()` 能在 net10 dll 上直接查到 `ImgProcSharp.Text.Fonts.WenQuanYiMicroHei.ttf.gz`。
- **CA2014（`stackalloc` 在循环里）**：`FlipHorizontal` 把 4 字节临时缓冲提到循环外；`FlipVertical` 需要整行缓冲，宽度大时栈会炸，改用 `ArrayPool<byte>`。核心工程开了 `TreatWarningsAsErrors`，这类问题在 CI 之前就被拦下。
- **`Div255` 的定式**：`t = x + 128; (t + (t >> 8)) >> 8`，在 0–65025 上与「四舍五入」完全一致（单测钉住 127/128/255/65025 四个边界）。标量与 SIMD 共用同一个定式，才可能逐字节相等。
- **预乘往返的容差不是 ±1**：8 位预乘在 alpha=a 时的量化步长约 255/a，a=8 时误差可以到 30。测试里容差写成 `255 / a + 1`，`a < 8` 按 §F 归零，别写成 `InRange(..., 0, 1)`。
- **SIMD 的形状**：`Vector128<ushort>`，一轮 4 像素——16 字节经 `WidenLower/WidenUpper` 变成两个 8 通道向量，`Vector128.Shuffle` 把 4 个 coverage 展开成「每像素 4 通道」，`Narrow` 收回 16 字节一次写出。标量/SIMD 各留 `internal` 入口，随机数据逐字节比对（含 1/3/7 这类尾部长度）。
- **暂不做 `Vector256`**：有了基准数字再决定（§N），现在两条路径已经够验证语义。
- **`Canvas.FillRectangle`** 是 M1 的临时入口（`internal`，仅供测试与 M2 前的自检），M2 起由 `DrawingContext` 承担；`RowBlender.OverSolid` 只认预乘行，裁剪由调用方做。

---

## S. 缩放：接口对齐 ImageSharp，语义自己定死

用户要求「接口参照 ImageSharp（SixLabors）Drawing」，缩放是第一个按这条规则落地的能力。原则：**名字对齐、行为写清、不假装支持**。

### 接口映射

| ImageSharp / ImageSharp.Drawing | 本库 | 说明 |
|---|---|---|
| `ctx.Resize(ResizeOptions)` | `Canvas.Resize(ResizeOptions)` | 本库 `Canvas` 尺寸不可变，返回新画布；ext 的 `IImageProcessingContext.Resize` 换掉 `Image` 内部画布 |
| `ctx.Resize(int width, int height)` | `Canvas.Resize(int, int)` | 语义 = `Stretch` |
| `ResizeOptions.Size` | `ResizeOptions.Size`（`Size` 结构） | 目标框 |
| `ResizeMode.{Crop,Pad,BoxPad,Max,Min,Stretch}` | 同名同序 | `Max` 是默认值，与 ImageSharp 一致 |
| `IResampler`、`KnownResamplers` | 同名 | 本库 `IResampler` 只有 `Radius`/`GetValue`，归一化与边界由 `Resizer` 负责 |
| `ResizeOptions.Compand` | 保留字段，`true` 抛 `NotSupportedException` | 不静默忽略 |
| `ResizeOptions.PadColor` | 默认 `Transparent`（ImageSharp 是白色） | 差异点：透明画布更符合本库默认；文档写明 |
| `Image.Mutate(x => x.Resize(...).Fill(...))` | 同形 | M4 落地，`IImageProcessingContext` 见 plan §7 |

### 模式语义（写进 XML 注释，也写进测试）

| Mode | 缩放比例 | 输出尺寸 | 留白/裁剪 |
|---|---|---|---|
| `Stretch` | 各轴独立 | 目标框 | 无 |
| `Max` | `min(sx, sy)` | 等比缩小后的尺寸 | 无（可能小于目标框） |
| `Min` | `max(sx, sy)` | 等比放大后的尺寸 | 无（可能大于目标框） |
| `Crop` | `max(sx, sy)` | 目标框 | 居中裁掉溢出部分 |
| `Pad` | `min(sx, sy)`，允许放大 | 目标框 | 居中留白，`PadColor` |
| `BoxPad` | `min(sx, sy, 1)`，只缩不放 | 目标框 | 居中留白，`PadColor` |

### 实现决策

- **只在预乘空间插值**：直通空间的加权平均会把「透明黑」当成黑色参与计算，半透明边缘出现黑边/彩边。预乘空间里透明像素的 RGB 本来就是 0，加权平均天然正确（这就是 misc §D 之外的第二个收益）。
- **负权重必须收口**：Lanczos3 的权重有负值，累加后可能出现 `rgb > a` 或负数。输出时统一 `clamp(v, 0, a)`、`a` 自身 clamp 到 `[0,255]`，保证画布「预乘合法性」这条不变量。
- **两趟分离 + float 中间缓冲**：先水平（行内顺序访问）再垂直。中间缓冲 `float[dstW × srcH × 4]`，4K→8K 约 530 MB，超 `int` 上限直接抛 `InvalidOperationException`（提示分块），不做静默降级。
- **同尺寸直接 `Clone`**：`Max`/`Crop` 在极端比例下会算出与原图相同的尺寸，此时不重采样，避免白丢一次精度（测试 `SameSizeIsLossless` 守住）。
- **`NearestNeighbour` 单独快路径**：通用核路径在 `radius = 0.5` 时窗口可能覆盖两个像素，得到的是「盒式平均」而不是严格的整像素复制。像素画放大必须逐像素，因此 `Resizer` 对 `NearestNeighbourResampler` 走整数映射。
- **不做 sRGB 线性化（`Compand`）**：需要 gamma 查表与往返，且与「绘图库」定位无关。设为 `true` 会抛异常，比默默按 sRGB 处理更诚实。
- **不做 SIMD**：重采样是浮点累加，`Vector<float>` 能加速但要处理跨行 gather，v1 先用标量；§N 的端到端基准出来再决定。
- 采样核的窗口先 clamp 到 `[0, srcLen-1]` 再归一化（截断而非边缘延拓），与 ImageSharp 的做法一致；因此边缘不会因权重丢失而变暗。

---

## T. 路径模型：为什么是封闭集合的 IPath

- **`IPath` 只有 `Bounds` 与 `Transform`**：光栅化需要「展平」，但展平要传 internal 的 `PathFlattener`，不能出现在 public 接口里。于是另设 internal `IFlattenable`，`Path`/`RectangularPolygon`/`Polygon`/`EllipsePolygon` 都实现它；`DrawingContext` 遇到既不是 `Path` 也不是 `IFlattenable` 的自定义 `IPath` 会抛 `ArgumentException`，错误信息直接指向 `PathBuilder`。
- **`Path` 与 `Rectangle` 并存**：`Rectangle` 是整数像素矩形（画布尺寸、裁剪、`Canvas.Create`），`RectangularPolygon` 才是可绘制轮廓。ImageSharp 里也是 `Rectangle`(int) 与 `RectangularPolygon`(float) 并存，命名对得上。
- **`Bounds` 用展平结果算**：三次贝塞尔的控制点凸包比真实曲线大，直接拿来算包围盒会让裁剪区域偏大；展平一次（默认 0.25 px）后取 min/max 既准确又不贵，且结果缓存。
- **`CloseFigure` 之后继续画线**：从子路径起点开始新的一条（Canvas/SVG 语义）。未闭合的子路径在填充时一律隐式闭合，展平器不补点，由光栅化器按「首尾相连」处理。
- **二次贝塞尔不带进 `Path`**：`PathBuilder.QuadraticTo` 按 2/3 规则转成 cubic，`Path` 只有三种命令，光栅化器少一个分支。
- **展平的 S 形保护**：只用「控制点到弦的距离 ≤ tolerance」会把 `(0,0)-(100,0)-(-100,0)-(0,0)` 这类曲线当直线。额外要求两个控制点在弦同侧（叉积同号），否则继续二分，最大深度 16。

---

## U. 光栅化与矩形快路径（M2 落地笔记）

- **4× 子采样是精度与成本的折中**：纵向 4 条子扫描线（0.125/0.375/0.625/0.875）各算一次横向精确覆盖，累加到 `ushort[w]`，除以 4 得 0–255。单行累加、逐行输出，内存只有 O(w)，不需要整幅缓冲。
- **FillRule 只影响「配对」，不影响覆盖计算**：`winding` 与 `count` 同时维护，`NonZero` 看 `winding != 0`，`EvenOdd` 看 `count & 1`，跨越判定统一成「从未填充进入填充」时记 startX，两种规则共用同一段填充代码。
- **顶点不重复计数**：边用 `[yMin, yMax)` 半开区间，水平边（`|dy| < 1e-6`）直接丢弃，避免行首/行尾交点成对出现导致的奇偶错误。
- **活动边表（M7 补）**：原本每条子扫描线都遍历全部边，复杂度是 O(行 × 边)。描边把一条 1000 段折线变成上万条边之后，这里就成了瓶颈（实测 43 ms）。现在边按 `StartRow` 排序、按行入表、`YMax <= rowTop` 时交换删除退出，复杂度降到 O(边 log 边 + Σ活动边)，同一用例 4.97 ms。像素输出逐字节不变（180 个用例前后一致）。
- **矩形快路径必须逐字节一致，不是「近似一致」**：慢路径对一条子扫描线的贡献是 `round(horizontal * 255)`，整行是 `(Σ + 2) / 4`；快路径用 `(coveredSubSamples * round(horizontal*255) + 2) / 4`，因为矩形在同一条子扫描线上的横向覆盖恒定，两者数学同构。测试用真矩形与 `PathBuilder` 构造的同坐标矩形跑全缓冲区比对（含 2.5/3.25 这类半像素边界、半透明色）。
- **快路径的省法**：内部行完全覆盖时连 coverage 都不读，直接把 255 行喂给 `RowBlender`；只有边界行做逐像素覆盖计算。省掉的是展平、建边表、coverage 缓冲分配三笔固定开销。
- **不做「整块 memcpy」的不透明特化**：`OverSolid` 在 `coverage = 255 && color.A = 255` 时每像素仍要乘一次，但保持单一混合内核能保证所有路径语义一致。M7 的基准里纯色矩形已经做到 1024×768 / 1.2 ms（1.6 ns/px，基本就是内存带宽），`CopyOpaque` 没有空间，不做。
- **`IBrush.Blend(canvas, region, coverage, stride)` 而不是逐像素回调**：回调每像素一次委托调用，渐变场景直接毁掉性能；整块 + stride 让实现可以按行做 SIMD。`TryGetSolidColor` 是给快路径留的窄接口，只有纯色返回 true。

---

## V. 文字：拷一个引擎，但只留一条混合路径（M3 落地笔记）

### 为什么整体拷 GlyphRaster 而不是自己写

中文要能画，就必须有 TrueType 解析（`loca`/`glyf`/`cmap`/`hmtx`/`kern`）、二次贝塞尔轮廓、A8 掩码光栅化和内置字体。这四件事加起来 2500 行左右，是「已被验证的确定工程量」，重写没有任何收益。所以 `F:\code\zlmsharp\src\GlyphRaster` 的 11 个文件整体拷入 `ImgProcSharp.Core/Text/`，只做三件改造：

1. 命名空间统一 `ImgProcSharp.Text`（`TextFont` 留在根 `ImgProcSharp`，因为它是对外门面）。
2. 所有引擎类型 `internal`，成员保持原样 —— 只改类型可见性，不动实现，减少拷入风险。
3. 资源名从 `GlyphRaster.Fonts.*` 改成 `ImgProcSharp.Text.Fonts.*`，与 csproj 的 `LogicalName` 对齐。

### 删掉 TextPainter

原引擎自带 `TextPainter`（`RgbColor` + 直接写 RGB24/BGRA 数组的混合循环）。**没拷**。理由：本库已经有 `RowBlender` + `IBrush` 这一条混合路径，再留一条「文字专用混合」意味着同一段半透明文字在两条路径下颜色不同，而这种不一致极难在测试里发现。文字的正确做法是把自己变成 coverage：`GlyphMask` → `CoverageBuffer` → `brush.Blend`，和形状完全同构。

顺带的好处：渐变文字、位图文字笔刷、未来加 `PushOpacity` 都自动支持，不需要在文字模块里再实现一遍。

### 位置语义：`at` 是行顶，不是基线

ImageSharp.Drawing 的 `DrawText(text, options, location)` 里 `location` 是文本盒左上角，不是基线。`TextLayout` 内部产出的是「相对行顶」的整数坐标（`ascent + OffsetY`），所以 `DrawText` 直接 `round(location) + glyph 坐标` 即可。第一版测试就因为把 `at` 当基线写，导致文本整体落到画布外（30 px 高的画布、基线写到 40）而失败 —— 这个语义必须写进 XML 注释，不能靠猜。

### 对齐放在排版层，不放在绘制层

`TextAlign` 需要「每行自然宽度」，而绘制层拿到的只有一串绝对坐标的字形。所以在 `TextFont.Layout` 里维护 `lineWidths`（换行时 push、结尾 push），最后统一算出每行偏移并按 `PositionedGlyph.Line` 平移。慢路径 `GetTextPaths` 用同一份 `ComputeLineOffsets`，保证快慢两条路径的对齐结果完全一致。

`Width` 保持「最宽行的自然宽度」：`MeasureText` 用它做布局计算，如果返回对齐后的视觉宽度，调用方再算居中就会双重偏移。

### 快慢路径的墨水量天然不同，测试不能要求逐字节一致

`DrawText` 用字体的 A8 掩码（**经过 hinting**，竖笔被拉到整像素），`FillText` 用轮廓展平后重新光栅化（**没有 hinting**）。24 px 字号下两者墨水量差 20% 是正常的，不是 bug。验收因此写成：

- 墨迹包围盒四边对齐 ±3 px；
- 墨水量比值落在 0.6–1.5；
- 直方图/覆盖率不做比较。

如果哪天要「两条路径像素级一致」，正确的做法是给 `GlyphRasterizer` 加 hinting 或者让掩码走无 hinting 渲染，而不是放宽光栅化精度。

### 字形重叠取 max，不取累加

同一行里两个字形可能重叠（负 kerning、斜体悬挑）。`BlendGlyphs` 写 coverage 时用 `if (src > dst) dst = src`，等价于「并集」：同一块墨水不会被画两遍而变黑。形状路径里的自交区域靠 fill rule 处理，文字没有这个概念，取 max 是唯一直观的选择。

### 每次 DrawText 分配一个 CoverageBuffer

文本包围盒通常比画布小得多（一行字 30×200），`ArrayPool` 租一块刚好大小的缓冲比「按画布尺寸池化、每次清零」更省。`CoverageBuffer` 的构造函数会清零，所以不需要额外 `Clear`。M7 基准显示 32 px 字号画 24 个字形只要 41 µs（0.9 KB 分配），缓存整段文本 coverage 的收益远小于「缓存失效/字形重排」的复杂度，因此不做；真嫌慢的话调用方应该缓存 `Image`/`Canvas` 结果而不是让绘制层记住文字。

---

## W. 弧、圆角矩形与渐变（M2 后段落地笔记）

### 弧用 SVG 端点参数化，并且只活在 builder 里

`ArcTo(end, radius, rotationDegrees, isLargeArc, sweepClockwise)` 照抄 SVG 的 `A` 命令语义：调用方给**终点**和半径，圆心由规范 F.6.5 的步骤算出来（两次坐标旋转 + 半径不足时等比放大 + 求圆心 + 求起止角）。理由：

- 调用方几乎总是「从 A 到 B、用半径 r 画弧」（圆角、饼图、箭头），圆心参数化要自己算弦长和角度，更难用；
- 「半径太小连不上两点」这种病态输入在规范里有确定行为（等比放大），不必自己发明规则；
- 与 SVG 路径互转、跟别的库对照都简单。

算出圆心后立刻按 ≤90° 拆成若干段三次贝塞尔（`alpha = 4/3 · tan(Δ/4)`），**命令序列里只有 cubic**。于是展平器、光栅化器、`Path.Transform` 全都不需要知道弧的存在。90° 单段三次贝塞尔的最大径向误差约 0.027%——半径 1000 px 时偏差 0.27 px，比 1/256 的覆盖率粒度粗一点点，但 1 px 的描边/填充早就看不出来了。想更狠可以拆 45°，命令数翻倍，不值得。

最后一段的终点强制写成调用方给的 `end`，而不是算出来的 `p1`：否则浮点误差会在闭合路径上留下 1/1000 px 的缝，`NonZero` 填充时偶发漏一个像素。

### 圆角矩形的半径夹在 builder 外部

`RoundRectanglePolygon` 构造时把半径夹到 `min(w,h)/2`。不夹会怎样：半径大于半宽时四个圆心交叉、弧互穿，`NonZero` 下填充结果变成自交怪形状（不是简单的「圆角太大」）。夹住之后超大半径自然退化成胶囊，与所有 UI 框架一致。

### 渐变必须在预乘空间插值

这是本次唯一一个「看起来能跑但结果不对」的坑：按直通色插值时 `Transparent → White` 的中点是 `rgba(128,128,128,128)`，画到图上就是一条**灰色脏带**——直通空间的「透明黑」在视觉上并不等价于「任何颜色的零 alpha」。所有正经渲染器（Skia / ImageSharp / Direct2D）都在预乘空间插值：停止点构造时转预乘浮点（`r * a / 255`），插值完再乘覆盖率混合。测试专门断言中点必须是「白色 + alpha≈128」而不是灰。

### 共享内核放基类，参数化留给子类

`GradientBrush` 负责停止点排序/夹紧、单停止点补齐、预乘浮点表、段查找、逐像素混合、抖动；子类只实现 `ParameterAt(x, y)`（线性是点积比长度平方，径向是椭圆归一化距离）。

取舍：`Blend` 里每像素有一次虚调用。实测 JIT 在 `Fill` 调用点（笔刷类型单态）会去虚化，暂时不手写 `sealed override` 的专用循环。M7 基准里渐变矩形是 13.3 ms / 1024×768（17 ns/px），主要成本在逐像素参数计算与预乘插值，去掉一次已被去虚化的调用不会改变量级，因此保持单一内循环，把优化预算留给「每行预计算参数增量」（见 §Y）。

### 抖动只动颜色，不动 alpha

`Dither = true` 时在参数 `t` 上加 `(bayer[y&3, x&3] - 7.5) / 255`。只影响颜色通道：alpha 参与 coverage 混合，抖动 alpha 会在边缘产生颗粒噪点，而色带只出现在颜色维度。幅度取 ±0.5/255 是「刚好打散 8 位量化台阶」的经典值，大了会变成可见噪点。抖动矩阵用**画布绝对坐标**取模，否则同一笔刷在不同 region 上错位，平铺时能看出接缝。

### 同色渐变仍然走纯色快路径

`TryGetSolidColor` 在所有停止点同色时返回 true。`Fill` 的矩形快路径靠这个接口决定能否整行不读 coverage；`LinearGradientBrush(red, red)` 能命中快路径，说明快慢分派没有把笔刷类型写死成 `is SolidBrush`。图案笔刷同理，但只对「全不透明且同色」报告纯色——瓦片只有预乘数据，半透明反预乘丢精度（`30 @ a=200` 反算回来是 31），拿近似值走快路径会让同一笔刷在不同路径下差 1。

---

## X. 描边与变换（M5 落地笔记）

### 四边形 ∪ 圆盘，交给 NonZero 求并集

> 下面是 M5 的形态（圆盘通吃连接与端帽）。M6 把「每个顶点补圆盘」换成了按样式生成的连接/端帽几何，但「同向闭环 → `NonZero` 求并集」这个核心没变，细节见 §Z。

`StrokeOutline.Build(flattened, width)` 的输出是一条「可以按 `NonZero` 一次填充」的轮廓：

```
for 每条线段 (a, b):
    n = perp(b - a) 归一化 × (width/2)
    追加四边形 [a+n, b+n, b-n, a-n]
for 每个顶点（含开放路径两端）:
    追加半径 width/2 的圆盘（环形点，闭合）
```

然后 `NonZero` 填充，重叠区域环绕数 ≥ 1，自然取并集。

- **为什么不做「描边专用光栅化器」**（按点到折线距离算覆盖率 / SDF）：那是第二条 AA 路径，要重新处理 coverage 量化、裁剪、笔刷混合、与 `Fill` 结果的一致性。现在描边只负责「生成轮廓」，AA / 裁剪 / 渐变色 / 半透明混合全部复用 `ScanlineRasterizer` + `IBrush`，代码量与风险都小一个量级。
- **绕向必须统一**：这是正确性前提。`NonZero` 下反向的轮廓在重叠处环绕数相消，接缝上会挖出「洞」或露出白缝。`AddClosed` 用鞋带公式算 `twiceArea`，负值就反向遍历点列，保证每个子路径都是正面积绕向。圆盘那一环只有 3 点以上才追加（`points.Length < 3` 直接丢）。
- **接缝不会二次叠加**：半透明描边在四边形与圆盘的重叠区不会变深，因为 coverage 是整条轮廓一次算出的一张图，不是「先画四边形再画圆盘」两次混合。这是「先求并集再光栅化」相对「逐段光栅化」的核心收益。
- **圆盘段数按弦高容差定**：`0.15 px`，`segments = ceil(π / acos(1 - tol/radius))`，再 clamp 到 `[8, 64]`。半径 1 px 时刚好 8 段（此时误差远小于 1/255 覆盖率粒度），半径大到一定程度就固定 64 段——再细下去边数线性涨，肉眼看不出。
- **退化与病态输入**：`|b - a|² < 1e-6` 的零长段跳过（否则归一化出 `NaN`）；`Pen.Width` 在构造时就拒绝 `≤0` 和 `Infinity`，`Draw` 里再兜一次（自定义 `IPen` 实现），判定写成 `!(width > 0)` 把 `NaN` 一起挡掉。
- **代价**：轮廓边数 ≈ 线段数 × 2 + 顶点数 × 圆盘段数。1000 段折线会生成上万条边（约 1.7 MB 分配），边表本身不便宜——但这是个**光栅化器**问题而不是描边问题：M7 给扫描线补了活动边表之后，同一用例从 43 ms 降到 4.97 ms（§U）。真要再往下压，优先考虑「只在内侧转角补扇形」或按距离场直出，而不是给描边单独开一条光栅化路径。

### 为什么 v1 只有圆角连接 + 圆头端点

`Step 1` 的圆盘同时干了两件事：顶点处补连接、开放路径端点处补端帽。所以「圆角 + 圆头」是**同一个子形状**，不需要额外分支，M5 用它最快打通了「轮廓生成 → NonZero → 光栅化 → 笔刷」这条链路。剩下的样式（Miter/Bevel/Square cap/Dash）放在 0.2，理由见 §Z 开头；接口当时只留 `IPen { Color, Width }`，正是因为**往接口上加带默认实现的属性不破坏现有实现**，而先加枚举再删就是 breaking change。

### 笔宽随变换缩放：`Width × sqrt(|det|)`

`Path` 会被 `Transform` 乘成画布坐标，线宽是纯标量、不会跟着走，所以 `Draw` 自己乘一个缩放因子：

| 变换 | det | 因子 | 结果 |
|---|---|---|---|
| `scale(2, 2)` | 4 | 2 | 线宽 2 倍，与「整体放大」的直觉一致 |
| `scale(2, 1)` | 2 | 1.41 | 几何平均：面积意义上的等效线宽 |
| `rotate(θ)` | 1 | 1 | 旋转不改线宽 |
| `scale(0, 0)` / 奇异 | 0 | — | 直接 `return`，不抛异常 |

非等比缩放下「正确」的答案其实是椭圆笔尖（Skia 就是这样），本库不做——那需要把描边轮廓按方向分轴缩放，圆盘变椭圆，连接处还要重新处理。取几何平均是一个折中，写进文档，不假装支持。

退化矩阵的处理是**静默返回**而不是抛异常：`scale(0, 0)` 下「什么都画不出来」是数学结论，不是调用方的错误。

### `Draw` 内部调 `FillCore`，不能调 `Fill`

`Fill` 的语义是「先 `path.Transform(Transform)` 再画」。而 `Draw` 里路径已经乘过变换、轮廓也已经按 `width × scale` 在画布坐标系里生成，再走一遍 `Fill` 会被**二次变换**（旋转两次、平移两倍）。

所以把「矩形快路径 + 光栅化 + 笔刷混合」拆成 private `FillCore(brush, path, rule)`：`Fill` = 变换 + `FillCore`，`Draw` = 变换路径 + 生成轮廓 + `FillCore`。这也顺带解释了为什么描边天然失去 `RectangularPolygon` 快路径——轮廓是 `Path`，不是矩形，这本来就该走慢路径。

### 文字在非恒等变换下自动降级到轮廓

字形掩码是轴对齐的 A8 位图，只能整体平移，不能旋转/斜切/非等比缩放。`DrawText` 开头判断 `!context.Transform.IsIdentity` 就转 `FillText`（`TextFont.GetTextPaths` → `GlyphPathBridge` → 通用填充）：

- 正确性优先：旋转徽章上的文字必须真的转过去，而不是原样贴上去。
- 代价是失去 hinting（见 §V「快慢路径的墨水量天然不同」），缩放后笔画粗细会有偏差；这在「已经做了旋转」的场景里完全可接受。
- 恒等变换时仍然走掩码快路径，常见用法（直接写一行字）没有回归。

`TextDrawing.GetTextPath(text, location, options)` 把同一条轮廓路径暴露出来，于是「描边文字」= `Draw(new Pen(color, w), TextDrawing.GetTextPath(...))`。文字模块不需要知道描边的存在，描边也不需要知道文字的存在。

### 已知差距（写进文档，不假装支持）

- **`Resize` 保留 `Transform`、丢弃裁剪**：ext 的 `Resize` 换了画布后 new 一个新的 `DrawingContext` 并 `SetTransform` 还原旧矩阵（`Rebind()`）。裁剪掩码和画布尺寸绑定，只能一起丢；`PopClip` 随后会抛异常，这是刻意的。
- **非等比缩放线宽取几何平均**，不是真椭圆笔尖。

> M7 补上的两件事写在 §Y：笔刷跟随变换、`PushClip`/`PopClip`。M6 的描边样式写在 §Z。

---

## Y. 裁剪与笔刷变换（M7 落地笔记）

### 裁剪用「画布大小的 A8 掩码」，不做几何求交

`PushClip(path)` 把轮廓按当前变换 flatten → 扫描线 → A8 掩码；之后每次混合前做一次 `coverage = Mul255(coverage, mask)`。

- **为什么不做路径求交**：多边形布尔要处理自交、重合边、退化环，出错方式多且难测；而掩码是纯栅格操作，与普通填充共用同一条光栅化路径，斜边天然 AA、与填充覆盖率同一种量化——不会出现「先填充后硬裁剪」的锯齿。
- **为什么乘 coverage 而不是乘像素**：coverage 是 0..255 的覆盖率，乘完仍是覆盖率，`RowBlender` / 渐变 / 图案笔刷完全不需要知道裁剪存在。半透明描边压在裁剪边界上也不会变深，因为乘法只作用一次。
- **代价**：每层 `W×H` 字节，1080p 约 2 MB/层；`PopClip` 立刻归还到 `ArrayPool`。层数通常 1–2。
- **为什么不做「只分配裁剪矩形大小」的紧凑掩码**：掩码要按画布行索引寻址（`y*W + x`），带偏移的掩码会把「取一行」拆成两段逻辑（跨行、部分覆盖），省下的内存不值得多这条分支。

### 三个入口共用一条混合路径

`BlendCoverage(brush, region, coverage)` = 乘掩码 → `brush.Blend(...)`。

| 入口 | 说明 |
|---|---|
| `FillCore` | 填充与描边共用（`Draw` 生成的轮廓也走这里） |
| `TextDrawing` 字形掩码快路径 | 若只改 `Fill`，文字会无视裁剪——而这类 bug 只有把字放到边界上才看得见 |
| `Draw(IPen, ...)` | 内部调 `FillCore`，自动获得裁剪 |

### 有裁剪时必须关掉矩形快路径

`RectangleFill` 整行直写像素、根本不读 coverage，所以裁剪会对它失效。判断写在 `FillCore` 开头：

```csharp
if (!clipped && path is RectangularPolygon && brush.TryGetSolidColor(out Color solid))
{
    RectangleFill.Fill(Canvas, path.Bounds, solid);
    return;
}
```

这也说明特化快路径的定位：它只是「同语义的更快实现」，任何新的绘制状态（裁剪、将来的混合模式）都必须让它显式退场，而不是让它「大概对」。

### 全零掩码也要压栈

`PushClip` 交不出任何像素（轮廓完全在画布外）时，压一层全零掩码而不是跳过：裁剪深度必须与 `PopClip` 一一对应，否则调用方的配对逻辑整条错位。顺带得到一个优化点——`AnyClipPixelInside(region)` 全 0 时直接跳过光栅化，填充完全落在裁剪外时连扫描线都不跑。

### `PopClip` 空栈抛异常

- 静默 no-op 会让「多 Pop 一次」的 bug 潜伏到画面差异；栈空直接 `InvalidOperationException` 是最便宜的定位手段。
- 对照：`Draw` 遇退化矩阵静默返回，因为那是数学结论（零面积），不是调用方写错。两种「安静/吵闹」的分界就在这。

### 笔刷跟随变换：逆矩阵 + 不可变副本

笔刷的几何（渐变起止点、径向圆心与半径、图案原点）都在**用户坐标系**里定义。采样时把画布像素中心映射回去：

```
user = (px + 0.5) * M⁻¹
```

- 逆矩阵在 `DrawingContext.Fill` 里算（`Matrix3x2.Invert`），写进笔刷**副本**的 `InverseTransform`。笔刷保持不可变，所以同一个 `LinearGradientBrush` 能在不同变换下复用、互不干扰（`BrushStaysUntransformedAfterUse` 专门盯这一点）。
- 副本只重建几何与色表（`Stops.ToArray()` 后重新排序预乘）。色标通常 2–4 个，成本远小于一次填充。
- 退化矩阵（`Invert` 返回 false）时不换副本：路径本身已是零面积，采样结果没人用。
- `GradientBrush.WithInverseTransform` 是 `internal virtual`，默认返回自身——外部派生类宁可「不跟随」也不要「半对」，内置的线性/径向覆盖它。`PatternBrush` 是 sealed，直接给实例方法。
- 图案相位：`(floor(userX) - originX) mod tileWidth`。用像素中心 `+0.5` 再 floor，等价于「这个像素落在哪个用户坐标格」；恒等变换时仍走整数快循环，逐字节与旧行为一致。

### `Pen` 为什么不用管笔刷

`Pen` 只有颜色和线宽：线宽缩放已经在生成轮廓时乘过 `sqrt(|det|)`，颜色无所谓坐标。将来若加 `IPen.Brush`，`FillCore` 之前已有统一的 `PrepareBrush` 入口，不需要再改 `Draw`。

### AOT

- `samples/ImgProcSharp.AotSmoke`（`PublishAot=true`）实测 win-x64 自包含 **9.8 MB**，输出
  `dims=320x200 png=10413 render=C763804986BB6615 decode=C763804986BB6615 ink=54828`，与 JIT 逐字节一致（0.2 渐变行游标优化后的参考值，见 §AA）。
- 库里没有运行时代码生成：字体解析、路径展平、扫描线都是数组 + 泛型实例化，`IsAotCompatible` 分析器 0 警告。
- 除了 `InvariantGlobalization=true` 没做额外裁剪配置（也没有文化相关格式化可用）。

### 基准

| 类 | 覆盖 |
|---|---|
| `FillBenchmarks` | 纯色矩形（快路径）、渐变矩形、渐变三角形 |
| `StrokeBenchmarks` | 1000 段折线、大圆描边 |
| `TextBenchmarks` | 掩码快路径、轮廓慢路径、`MeasureText` |
| `CodecBenchmarks` | `PngWriter.Encode`、`Image.Load`、解码+画+编码 |

实测（net10.0 / win-x64 / 本机，`ShortRunJob`，数字只用于对照 §N 的量级，不是承诺）：

| 用例 | 耗时 | 备注 |
|---|---|---|
| 纯色矩形 1024×768 | 1.2 ms | 矩形快路径，1.6 ns/px ≈ 内存带宽 |
| 线性渐变矩形 1024×768 | 8.6 ms | 11 ns/px，纯色的 7.1 倍（行游标前 13.3 ms / 10.9 倍，见 §AA） |
| 线性渐变三角形（半画布） | 4.3 ms | 与面积成正比，说明瓶颈在逐像素采样（优化前 6.5 ms） |
| 1000 段折线描边（宽 4） | 5.0 ms | 补活动边表前是 43 ms |
| 大圆描边（半径 320） | 1.2–1.4 ms | |
| 32 px 文字 24 字形（掩码） | 41 µs | 0.9 KB 分配 |
| 同上一行走轮廓慢路径 | 174 µs | 220 KB 分配，慢 4 倍 |
| `MeasureText` | 0.35 µs | |
| PNG 解码 800×600 | 2.5 ms | `Image.Load` |
| PNG 编码 800×600 | 12.6 ms | `PngWriter` |
| 解码 + 画 + 编码 | 15.2 ms | 编码占绝对大头 |

渐变仍是这里最贵的一项（7.1× 纯色），0.2 的第一刀已经砍过：每行预计算参数增量（§AA），而不是上 SIMD——SIMD 要重写整个混合内核，在这一层看不出收益，代码可读性代价却大得多。基准只用来「别把数量级搞错」，不进 CI 门禁——共享虚拟机的频率波动会让绝对值失去意义。

---

## Z. 描边样式（M6 落地笔记）

### 只补几何，不补光栅化

M6 加的是 `LineJoin`（Miter/Bevel/Round）、`LineCap`（Butt/Round/Square）、`MiterLimit` 和 `DashPattern`/`DashOffset`。四种东西最后都落在同一条产线上：**折线 → 四边形 + 连接 + 端帽 → 统一绕向 → `NonZero` 一次填充**。没有第二套 AA、第二套裁剪、第二套笔刷混合，所以半透明描边在连接处不会变深这个性质自动保留。

### 外侧法向：叉积决定，不查表

连接需要知道「哪一侧是外侧」。两条边的单位方向 `u`、`v` 的叉积 `cross = u.x·v.y - u.y·v.x`：

```csharp
float side = cross > 0f ? 1f : -1f;      // 左转取右侧，右转取左侧
float n1x = side * uy, n1y = -side * ux; // 外侧法向
```

有了外侧偏移点 `A = vertex + n1·r`、`B = vertex + n2·r`，三种连接就是三种多边形：

| 连接 | 追加的几何 | 外边界 |
|---|---|---|
| `Round` | 圆盘（M5 的老办法） | 圆弧 |
| `Bevel` | 三角形 `[vertex, A, B]` | 线段 A→B |
| `Miter` | 四边形 `[vertex, A, m, B]` | 折线 A→m→B |

共线（`|cross| < 1e-5`）和 180° 折返直接返回：两个四边形在顶点处已经贴合，补任何形状都是多余的。重复点没有切向可算，补圆盘兜底。

### Miter：闭式交点 + 比值判据

两条外侧偏移线的交点可以解出来，不必真的解 2×2 方程：

```
m = vertex + r · (n1 + n2) / (1 + n1·n2)
```

`|m - vertex| / r = 1 / sin(θ/2)`（θ 是两条边的夹角），这正是 SVG/Canvas 里 `miterLimit` 的定义。所以判断就一句：`|m - vertex|² > (r · MiterLimit)²` 时退回 bevel。

- `1 + n1·n2 → 0` 对应 180° 折返，此时 `|cross|` 也已经趋于 0，早被共线分支挡掉，不会除零。
- 默认 `MiterLimit = 4`（与 SVG 一致），90° 转角需要 `1/sin45° ≈ 1.414`，所以默认下方形/菱形都是尖角；把 limit 调到 1.2 就会看到它退回 bevel。

### 端帽

端帽的切向由「端点 → 内侧相邻点」给出，所以开放路径最后一个弯之后的走向就是端帽方向：

- `Butt`：不补（四边形本来就在端点处平着结束）；
- `Round`：补圆盘，内侧一半被四边形吸收；
- `Square`：补矩形，`[端点 ± 法向·r]` 再沿切向平移 `r`。

### 虚线 = 切段，不是光栅化分支

`DashPattern` 的单位是**线宽倍数**（与 ImageSharp.Drawing 一致）：线宽 4、图案 `[3, 2]` 就是 12 px 实、8 px 虚。`StrokeStyle.Create` 在生成几何前把它乘成像素，因此：

- 变换缩放线宽时，虚线段长同比缩放（测试 `DashAndJoinSurviveTransformScaling` 锁住这条）；
- 光栅化器完全不知道 dash 的存在。

切分按弧长走：`flatten → 累计长度 → 在每个图案边界插一个点 → 把「实」的区间收集成开折线`。每条开折线独立跑上面的四边形 + 连接 + 端帽流程，所以：

- dash 的每一段都有自己的端帽（圆头 dash 是胶囊形，不是方块）；
- 段内的折角仍然用 `LineJoin`（虚线沿着折线走时，转角不会缺一块）；
- 相位的正负与 SVG 的 `stroke-dashoffset` 同向，负值也能用；
- 奇数长度的图案按 SVG 语义重复一遍（`[4]` 等价于 `[4, 4]`），在 `Pen` 构造时归一化，绘制层不用再判断奇偶。

### 闭合路径：跨起点的两半要拼回去

闭合路径从 `points[0]` 开始走一圈，最后一个「实」段会结束在 `points[0]`，第一个「实」段也从 `points[0]` 开始——它们在几何上是同一条虚线被起点切开的两半。如果直接当两条开折线描边，起点处会各补一个端帽：圆头看起来是一个鼓包，方头看起来多出一个小方块。

所以切完段后做一次合并：若「起点在实线段内」且「终点也在实线段内」，把最后一段接在第一段前面（去掉重复的起点）。如果整圈都落在实线段内（图案比周长还长），合并结果首尾同点，直接按**闭合**轮廓描边——不补端帽，起点处补连接，与实线描边的像素结果完全一致。

### `IPen` 的默认成员：向后兼容的那一刀

`LineJoin`/`LineCap`/`MiterLimit`/`DashPattern`/`DashOffset` 全都有默认实现（圆角 / 圆头 / 4 / 实线 / 0），所以：

- 只实现 `Color` + `Width` 的自定义画笔行为不变（`MinimalPenUsesInterfaceDefaults` 锁住）；
- `Pen` 的样式用 `init` 属性设置，构造后不可变；
- `Draw` 对自定义 `IPen` 返回值做一次校验（枚举合法、`MiterLimit ≥ 1`、图案非负且不全 0、偏移有限），抛出 `ArgumentOutOfRangeException` 而不是在几何里算出 `NaN`。

虚线图案的数组由画笔持有，`Pen` 在 `init` 时复制一份；`IPen.DashPattern` 返回的数组**不要修改**（文档已写明）。

### 已知差距

- **曲线按 flatten 后的弦长打孔**：图案边界插在折线上，不是解析贝塞尔弧长。弦高容差 0.25 px 下，虚线段长的误差是亚像素级；极端密集的曲线会累积几十个边界点，但视觉上看不出来。
- **非等比缩放仍是圆笔尖**：线宽取 `sqrt(|det|)`，连接与端帽都是圆，不生成椭圆笔尖（§X）。
- **自交折线的连接**：锐角自交时 miter 尖角可能伸到另一段的外侧，`NonZero` 会把它并进去（不裁剪尖角），这与 Skia 的默认行为一致，不做额外处理。

### 测试

`StrokeStyleTests`（15 个用例）覆盖：三种端帽的边界位置、三种连接的形状差异、`MiterLimit` 退回 bevel、虚线相位与 `DashOffset`、图案随线宽/变换缩放、闭合方形四边缺口、跨起点的虚线合并（外角不能缺口）、超长图案退化成实线（逐像素比对）、`IPen` 默认成员、非法样式与 `Pen` 归一化。用例里的像素坐标都是按几何算出来的（例如 90° 转角 miter 伸出 `r·√2`），不是「跑一次记下来」的黄金值。

---

## AA. 渐变行游标（0.2 性能优化落地笔记）

### 原来的瓶颈：每像素都在算几何

优化前每个像素都要走一遍 `ParameterAt(x, y)`：线性渐变是两次点积 + 一次除法，径向渐变是两次除法 + 一次开方；再线性扫一遍停止点表找区间。1024×768 的矩形 13.3 ms，是纯色填充的 10.9 倍（§Y 的基准表）。

一条渐变的参数沿画布 x 方向是**解析**的：

- 线性渐变 `t(x) = t0 + dt·(x - x0)`，一阶差分即可（`Ddu = 0`）；
- 径向渐变 `q = nx² + ny²` 是 x 的**二次**函数，用二阶差分推进，每像素只剩一次开方。

于是 `GradientRowCursor{U, Du, Ddu, SquareRoot}` 只保存三个 float，`Step()` 就是两次加法。行起点由子类算一次（`TryCreateRowCursor(x0, y)`），热循环里没有点积、没有除法、没有矩阵乘。

### 逆变换：导数就是逆矩阵第一列

笔刷跟随变换（§Y）时采样前要过一遍 `M⁻¹`。画布 x 前进 1 px，用户坐标前进的量恰好是逆矩阵的第一列 `(M11, M12)`，所以有逆矩阵时 `stepX/stepY` 直接取它，起点用一次完整变换即可。

踩到的坑：径向渐变在**没有**逆矩阵时 `ny` 与 x 无关，`duy` 必须是 0。写成 `duy = ay` 会让 `Ddu = 2(dux² + duy²)` 把二次项算成两倍，`q` 每步被推大，圆心直接变黑——`RadialGradientIsSymmetricAroundCenter` 与 `RadialGradientGoesFromCenterToEdge` 立刻抓住。

### 段游标要能双向走

`SampleColorAt(t, ref segment, ...)` 复用跨像素的区间游标：参数单调前进时只做一次比较。但径向渐变的参数一行内先减后增（左半边朝圆心走），所以游标有两个循环——往前推进和往后退回。少了后退那个，左半边会一直停在最后一个区间，整行变成终点色。

### 两条路径的差要能被证明只有 1 LSB

`TryCreateRowCursor` 是 `internal virtual`，默认返回 `false`：外部派生的 `GradientBrush` 仍走逐像素 `ParameterAt` 慢路径（`CustomGradientBrushFallsBackToParameterAt` 盯住）。快慢两条路径共用同一个 `BlendPixel`，混合阶段逐字节相同。

`BlendPixel` 顺带加了一条不透明全覆盖直写（`coverage == 255 && sa > 254.5`）：混合恒等式此时的结果就是源色，省掉 4 次 `Mul255` 和读-改-写。与通用路径逐字节一致（`a = 255`、`inv = 0`、`Mul255(x, 255) == x`），不是「看起来差不多」。

但参数**算不出来一样的位**：差分是 `t0 + k·dt + k²·ddu` 的累计，慢路径每像素重算点积与除法，两者在 float 的最后一位上必然有出入。所以这里的约束不是「逐字节相同」而是「不超过 1 LSB」——`IncrementalGradientMatchesPerPixelPathWithinOneLsb` 用两个镜像派生类（线性、径向各一个，逐像素重算 `ParameterAt`）整缓冲区比对，断言最大通道差 ≤ 1；真出现结构性错误（符号、二次项、段游标）时差值是几十到几百，立刻掉出来。

副作用：AOT smoke 的参考哈希因此变了一次（png 10403 → 10413），§Y 已同步。重排浮点表达式就会动渲染结果，这是「像素级确定性」的代价，值得写清楚而不是假装逐字节不变。

### 结果与剩下的空间

| 用例 | 优化前 | 优化后 |
|---|---|---|
| 线性渐变矩形 1024×768 | 13.3 ms / 10.9× 纯色 | 8.6 ms / 7.1× 纯色 |
| 线性渐变三角形（半画布） | 6.5 ms | 4.3 ms |

每像素仍有分支、夹住、4 次 float→byte，径向渐变还有一次 `sqrt`。再往下砍的方向是「2 停顿时特化 + 去掉 clamp 分支」，收益是常数级；SIMD 得重写混合内核，不在这一层做。0.2 的优先级是混合模式与图层，渐变优化到此为止。

