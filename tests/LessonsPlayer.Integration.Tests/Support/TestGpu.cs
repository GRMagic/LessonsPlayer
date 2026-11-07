using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Infrastructure.Whisper;
using Microsoft.Extensions.Logging.Abstractions;

namespace LessonsPlayer.Integration.Tests.Support;

/// <summary>A GPU que o programa usaria nesta máquina, detectada uma vez, para os testes rodarem como o app.</summary>
internal static class TestGpu
{
    private static readonly Lazy<Task<GpuDevice?>> Detection = new(
        () => new VulkanGpuDetector(NullLogger<VulkanGpuDetector>.Instance).FindGpuAsync(CancellationToken.None));

    public static Task<GpuDevice?> FindAsync() => Detection.Value;
}
