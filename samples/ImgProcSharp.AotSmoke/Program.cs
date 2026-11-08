using System.Numerics;
using ImgProcSharp;
using Sdcb.SimdCodecs.Png;

// AOT 冒烟：把「裁剪 + 变换 + 渐变 + 描边 + 文字 + PNG 往返」跑一遍，
// 打印一组确定的数值。JIT 与 NativeAOT 两次运行的输出必须逐字节相同：
//   dotnet run -c Release --project samples/ImgProcSharp.AotSmoke
//   dotnet publish samples/ImgProcSharp.AotSmoke -c Release -r win-x64
//   ./samples/ImgProcSharp.AotSmoke/bin/Release/net10.0/win-x64/publish/ImgProcSharp.AotSmoke.exe
// 参考输出（JIT 与 NativeAOT 逐字节一致；任何一位不同都说明光栅化或内嵌字体在裁剪/AOT 下行为变了）：
//   dims=320x200 png=10413 render=C763804986BB6615 decode=C763804986BB6615 ink=54828
// （0.2 的渐变行游标优化之后的值；差分累计与逐像素重算最多差 1 LSB，见 misc §AA）
// 实测 win-x64 自包含 exe 约 9.8 MB（内嵌字体是主要体积来源）。

using Image image = Image.Create(320, 200, Color.White);

image.Mutate(context => context
    .PushClip(new RoundRectanglePolygon(8, 8, 304, 184, 24))
    .PushTransform(Matrix3x2.CreateRotation(-0.08f, new Vector2(160, 100)))
    .Fill(
        new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(320, 200),
            new ColorStop(0f, Color.Red),
            new ColorStop(1f, Color.Blue)),
        new RectangularPolygon(-200, -200, 720, 600))
    .ResetTransform()
    .Draw(new Pen(Color.Black, 3f), new CirclePolygon(160, 100, 60))
    .DrawText(new TextOptions { Size = 48, Color = Color.White }, "AOT", new PointF(118, 72))
    .PopClip());

byte[] straight = new byte[image.Width * image.Height * 4];
image.Canvas.CopyToStraight(straight, image.Width * 4);
byte[] png = PngWriter.Encode(straight, image.Width, image.Height);

using Image decoded = Image.Load(png);
byte[] decodedStraight = new byte[decoded.Width * decoded.Height * 4];
decoded.Canvas.CopyToStraight(decodedStraight, decoded.Width * 4);

int ink = 0;
for (int y = 0; y < image.Height; y++)
{
    for (int x = 0; x < image.Width; x++)
    {
        if (image.Canvas.GetColor(x, y) != Color.White)
        {
            ink++;
        }
    }
}

Console.WriteLine(
    $"dims={image.Width}x{image.Height} png={png.Length} render={Hash(straight):X16} decode={Hash(decodedStraight):X16} ink={ink}");

// 裁剪必须把圆角之外的角保持为白色；这几条断言在 JIT 和 AOT 下都不能改变
if (image.Canvas.GetColor(2, 2) != Color.White || image.Canvas.GetColor(317, 197) != Color.White)
{
    Console.Error.WriteLine("裁剪失效：圆角之外被写入了像素。");
    return 1;
}

if (ink < 20_000)
{
    Console.Error.WriteLine($"绘制内容过少：ink={ink}");
    return 1;
}

return 0;

static ulong Hash(ReadOnlySpan<byte> data)
{
    ulong hash = 14695981039346656037UL;
    foreach (byte value in data)
    {
        hash = (hash ^ value) * 1099511628211UL;
    }

    return hash;
}
