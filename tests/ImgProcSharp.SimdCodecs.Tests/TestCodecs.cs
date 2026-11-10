using System.Runtime.CompilerServices;
using Sdcb.SimdCodecs.Jpeg;
using Sdcb.SimdCodecs.Png;

namespace ImgProcSharp.SimdCodecs.Tests;


/// <summary>
/// 测试用的编解码器登记：主库只依赖 <c>Sdcb.SimdCodecs.Abstractions</c>，
/// PNG/JPEG 的实现由测试项目引用格式包后在这里注册（模块初始化时执行，早于任何测试）。
/// </summary>
internal static class TestCodecs
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        CodecRegistry.Register(PngImageDecoder.Registration, JpegImageDecoder.Registration);
        EncoderRegistry.Register(PngImageEncoder.Registration, JpegImageEncoder.Registration);
    }
}
