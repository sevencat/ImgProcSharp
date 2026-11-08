using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
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

    [GlobalSetup]
    public void Setup()
    {
        const int width = 800;
        const int height = 600;

        using Canvas canvas = Canvas.Create(width, height);
        new DrawingContext(canvas).Fill(
            new LinearGradientBrush(
                new PointF(0, 0),
                new PointF(width, height),
                new ColorStop(0f, Color.Red),
                new ColorStop(1f, Color.Blue)),
            new RectangularPolygon(0, 0, width, height));

        _straight = new byte[width * height * 4];
        canvas.CopyToStraight(_straight, width * 4);
        _png = PngWriter.Encode(_straight, width, height);
    }

    [Benchmark]
    public Image Decode() => Image.Load(_png);

    [Benchmark]
    public byte[] Encode() => PngWriter.Encode(_straight, 800, 600);

    [Benchmark]
    public void DecodeMutateEncode()
    {
        using Image image = Image.Load(_png);
        image.Mutate(x => x
            .Fill(Color.White, new RectangularPolygon(16, 16, 200, 80))
            .Draw(new Pen(Color.Black, 3f), new CirclePolygon(400, 300, 120)));

        byte[] straight = new byte[image.Width * image.Height * 4];
        image.Canvas.CopyToStraight(straight, image.Width * 4);
        _ = PngWriter.Encode(straight, image.Width, image.Height);
    }
}
