using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Sdcb.SimdCodecs.Jpeg;
using Sdcb.SimdCodecs.Png;

namespace ImgProcSharp.Benchmarks;

/// <summary>
/// 基准入口：<c>dotnet run -c Release --project benchmarks/ImgProcSharp.Benchmarks -- --filter *Fill*</c>。
/// 各 benchmark 类用 <see cref="ShortRunJobAttribute"/>，本机十几秒就能出数；要正式数据加 <c>--job default</c>。
/// </summary>
internal static class BenchmarkProgram
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(BenchmarkProgram).Assembly).Run(args);
}

/// <summary>填充吞吐：纯色走矩形快路径，渐变走逐像素采样。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class FillBenchmarks
{
    private Canvas _canvas = null!;
    private DrawingContext _context = null!;
    private RectangularPolygon _rectangle = null!;
    private LinearGradientBrush _gradient = null!;
    private IPath _triangle = null!;

    [GlobalSetup]
    public void Setup()
    {
        _canvas = Canvas.Create(1024, 768);
        _context = new DrawingContext(_canvas);
        _rectangle = new RectangularPolygon(16, 16, 992, 736);
        _gradient = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(1024, 768),
            new ColorStop(0f, Color.Red),
            new ColorStop(1f, Color.Blue));
        _triangle = new PathBuilder()
            .MoveTo(512, 40)
            .LineTo(1000, 728)
            .LineTo(24, 728)
            .CloseFigure()
            .Build();
    }

    [Benchmark(Baseline = true)]
    public void FillSolidRectangle() => _context.Fill(new Color(100, 149, 237), _rectangle);

    [Benchmark]
    public void FillTranslucentRectangle() => _context.Fill(new Color(100, 149, 237, 128), _rectangle);

    [Benchmark]
    public void FillGradientRectangle() => _context.Fill(_gradient, _rectangle);

    [Benchmark]
    public void FillGradientTriangle() => _context.Fill(_gradient, _triangle);
}

/// <summary>描边吞吐：1000 段折线的轮廓 → 扫描线 → 混合。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class StrokeBenchmarks
{
    private DrawingContext _context = null!;
    private Path _polyline = null!;
    private IPath _circle = null!;
    private Pen _pen = null!;

    [GlobalSetup]
    public void Setup()
    {
        Canvas canvas = Canvas.Create(1024, 768);
        _context = new DrawingContext(canvas);

        var builder = new PathBuilder();
        for (int i = 0; i < 1000; i++)
        {
            float x = 16 + ((i % 50) * 20);
            float y = 32 + ((i / 50) * 36);
            if (i == 0)
            {
                builder.MoveTo(x, y);
            }
            else
            {
                builder.LineTo(x, y);
            }
        }

        _polyline = builder.Build();
        _circle = new CirclePolygon(512, 384, 320);
        _pen = new Pen(Color.Black, 4f);
    }

    [Benchmark]
    public void StrokePolyline1000() => _context.Draw(_pen, _polyline);

    [Benchmark]
    public void StrokeCircle() => _context.Draw(_pen, _circle);
}

/// <summary>文字：字形掩码快路径 + 轮廓慢路径（后者是非恒等变换下 <c>DrawText</c> 自动走的那条）。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class TextBenchmarks
{
    private DrawingContext _context = null!;
    private const string Sample = "Hello, 世界 — ImgProcSharp";

    [GlobalSetup]
    public void Setup() => _context = new DrawingContext(Canvas.Create(1024, 256));

    [Benchmark]
    public void DrawTextMask() =>
        _context.DrawText(Sample, new PointF(16, 24), new TextOptions { Size = 32, Color = Color.Black });

    [Benchmark]
    public void DrawTextOutline() =>
        _context.FillText(new SolidBrush(Color.Black), Sample, new PointF(16, 120), new TextOptions { Size = 32 });

    [Benchmark]
    public void MeasureText() => TextFont.BuiltIn.MeasureText(Sample, 32f);
}

/// <summary>编解码（PNG 由调用方直接用 <see cref="PngWriter"/>，本库不内置编码器）。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class CodecBenchmarks
{
    private byte[] _png = null!;
    private byte[] _straight = null!;
    private byte[] _rgbPng = null!;
    private byte[] _jpeg = null!;
    private Canvas _opaque800 = null!;
    private Canvas _opaque1080 = null!;
    private byte[] _straight1080 = null!;

    [GlobalSetup]
    public void Setup()
    {
        const int width = 800;
        const int height = 600;

        _opaque800 = Canvas.Create(width, height);
        new DrawingContext(_opaque800).Fill(
            new LinearGradientBrush(
                new PointF(0, 0),
                new PointF(width, height),
                new ColorStop(0f, Color.Red),
                new ColorStop(1f, Color.Blue)),
            new RectangularPolygon(0, 0, width, height));

        _straight = new byte[width * height * 4];
        _opaque800.CopyToStraight(_straight, width * 4);
        _png = PngWriter.Encode(_straight, width, height);

        byte[] rgb = new byte[width * height * 3];
        for (int i = 0, j = 0; i < _straight.Length; i += 4, j += 3)
        {
            rgb[j] = _straight[i];
            rgb[j + 1] = _straight[i + 1];
            rgb[j + 2] = _straight[i + 2];
        }

        _rgbPng = PngWriter.EncodeRgb8(rgb, width, height, new PngWriteOptions());
        _jpeg = JpegEncoder.Encode(rgb, width, height, 3, new JpegEncodeOptions());
        _opaque1080 = Canvas.Create(1920, 1080, Color.Red);
        _straight1080 = new byte[1920 * 1080 * 4];
    }

    [Benchmark]
    public bool IsFullyOpaque() => _opaque1080.IsFullyOpaque();

    [Benchmark]
    public void CopyToStraightOpaque() => _opaque1080.CopyToStraight(_straight1080, 1920 * 4);

    [Benchmark]
    public Image Decode() => Image.Load(_png);

    [Benchmark]
    public Image DecodeOpaquePng() => Image.Load(_rgbPng);

    [Benchmark]
    public Image DecodeOpaqueJpeg() => Image.Load(_jpeg);

    [Benchmark]
    public byte[] Encode() => PngWriter.Encode(_straight, 800, 600);

    /// <summary>
    /// 不透明画布直喂编码器：预乘缓冲就是直通缓冲，省掉整图 <c>CopyToStraight</c>，
    /// 再用 <c>DiscardAlpha</c> 免掉 alpha 通道的 deflate 工作量。与 <see cref="Encode"/> 对比即为省下的那部分。
    /// </summary>
    [Benchmark]
    public byte[] EncodeFromCanvas() =>
        PngWriter.Encode(
            _opaque800.Buffer.AsSpan(0, _opaque800.Stride * _opaque800.Height),
            800,
            600,
            new PngWriteOptions { DiscardAlpha = true });

    [Benchmark]
    public void DecodeMutateEncode()
    {
        using Image image = Image.Load(_png);
        image.Mutate(x => x
            .Fill(Color.White, new RectangularPolygon(16, 16, 200, 80))
            .Draw(new Pen(Color.Black, 3f), new CirclePolygon(400, 300, 120)));

        // _png 出自全不透明画布，解码后 alpha 仍全是 255，所以 DiscardAlpha 安全
        Canvas canvas = image.Canvas;
        _ = PngWriter.Encode(
            canvas.Buffer.AsSpan(0, canvas.Stride * canvas.Height),
            image.Width,
            image.Height,
            new PngWriteOptions { DiscardAlpha = true });
    }
}

/// <summary>重采样：4K → 1080p，检查中间缓冲是否还按整图分配。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class ResizeBenchmarks
{
    private Canvas _source = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = Canvas.Create(3840, 2160, Color.Red);
        _source.SetColor(100, 100, Color.FromRgba(0, 255, 0, 255));
    }

    [Benchmark]
    public Canvas Downscale4K() => _source.Resize(1920, 1080, KnownResamplers.Lanczos3);
}

/// <summary>像素导出：去预乘 + 通道重排/剔除。</summary>
[ShortRunJob]
public class ExportBenchmarks
{
    private Canvas _canvas = null!;

    [GlobalSetup]
    public void Setup() => _canvas = Canvas.Create(1024, 768, Color.Red);

    [Benchmark]
    public byte[] ToBgra32() => PixelOps.ToBgra32(_canvas);

    [Benchmark]
    public byte[] ToRgb24() => PixelOps.ToRgb24(_canvas);

    [Benchmark]
    public byte[] ToGray8() => PixelOps.ToGray8(_canvas);
}

/// <summary>整图变换（翻转/旋转）与图案平铺：逐像素搬移类操作的吞吐。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class TransformBenchmarks
{
    private Canvas _canvas = null!;
    private DrawingContext _context = null!;
    private IPath _rectangle = null!;
    private PatternBrush _pattern = null!;

    [GlobalSetup]
    public void Setup()
    {
        _canvas = Canvas.Create(1024, 768, Color.Red);
        _context = new DrawingContext(Canvas.Create(1024, 768));
        _rectangle = new RectangularPolygon(0, 0, 1024, 768);

        using Canvas tile = Canvas.Create(8, 8);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                tile.SetColor(x, y, (x + y) % 2 == 0 ? Color.White : Color.Black);
            }
        }

        // 非零原点让平铺索引真的要走环绕
        _pattern = new PatternBrush(tile, new PointF(3, 5));
    }

    [Benchmark]
    public void FlipHorizontal() => _canvas.FlipHorizontal();

    [Benchmark]
    public Canvas Rotate90() => _canvas.Rotate90();

    [Benchmark]
    public Canvas Rotate180() => _canvas.Rotate180();

    [Benchmark]
    public void FillPattern() => _context.Fill(_pattern, _rectangle);
}
