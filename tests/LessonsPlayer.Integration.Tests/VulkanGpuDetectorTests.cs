using System.Diagnostics;
using LessonsPlayer.Infrastructure.Whisper;
using Microsoft.Extensions.Logging.Abstractions;

namespace LessonsPlayer.Integration.Tests;

/// <summary>
/// Escolha da GPU para a transcrição. A regra é pura (lista de dispositivos → escolha); a detecção de verdade
/// depende da máquina. Verificado à mão em 2026-11-07, com o log nativo do Whisper.net: num notebook com Iris Xe e
/// GTX 1650, o ggml-vulkan lista <c>0 = Intel</c> e <c>1 = NVIDIA</c>, e <c>GpuDevice = 1</c> usa o "Vulkan1".
/// </summary>
[Trait("Category", "Integration")]
public sealed class VulkanGpuDetectorTests
{
    private static readonly uint Vulkan12 = VulkanDevice.MakeApiVersion(1, 2);
    private static readonly uint Vulkan11 = VulkanDevice.MakeApiVersion(1, 1);

    [Fact]
    public void Prefers_the_discrete_gpu_even_when_the_integrated_one_comes_first()
    {
        // A ordem desta máquina: o Whisper usaria a Intel (dispositivo 0), que é ~2x mais lenta.
        var gpu = VulkanGpuDetector.Choose(
        [
            new VulkanDevice("Intel(R) Iris(R) Xe Graphics", VulkanDeviceType.IntegratedGpu, Vulkan12),
            new VulkanDevice("NVIDIA GeForce GTX 1650", VulkanDeviceType.DiscreteGpu, Vulkan12),
        ]);

        gpu.ShouldNotBeNull();
        gpu.Name.ShouldBe("NVIDIA GeForce GTX 1650");
        gpu.Index.ShouldBe(1);
    }

    [Fact]
    public void Uses_the_integrated_gpu_when_there_is_no_discrete_one()
    {
        var gpu = VulkanGpuDetector.Choose([new VulkanDevice("AMD Radeon(TM) Graphics", VulkanDeviceType.IntegratedGpu, Vulkan12)]);

        gpu.ShouldBe(new("AMD Radeon(TM) Graphics", 0));
    }

    [Fact]
    public void Ignores_software_devices_and_devices_without_vulkan_1_2_but_keeps_their_positions()
    {
        var gpu = VulkanGpuDetector.Choose(
        [
            new VulkanDevice("llvmpipe (LLVM 15.0.7, 256 bits)", VulkanDeviceType.Cpu, Vulkan12),
            new VulkanDevice("Old GPU", VulkanDeviceType.DiscreteGpu, Vulkan11),
            new VulkanDevice("Intel(R) UHD Graphics", VulkanDeviceType.IntegratedGpu, Vulkan12),
        ]);

        gpu.ShouldBe(new("Intel(R) UHD Graphics", 2), "o índice é a posição na lista do driver, que é a numeração do Whisper");
    }

    [Fact]
    public void Without_usable_devices_there_is_no_gpu() =>
        VulkanGpuDetector.Choose([new VulkanDevice("llvmpipe", VulkanDeviceType.Cpu, Vulkan12)]).ShouldBeNull();

    /// <summary>Na máquina de verdade: não lança, e responde rápido o bastante para rodar ao abrir o programa.</summary>
    [Fact]
    public async Task Detects_this_machines_gpu_quickly()
    {
        var stopwatch = Stopwatch.StartNew();

        var gpu = await new VulkanGpuDetector(NullLogger<VulkanGpuDetector>.Instance).FindGpuAsync(TestContext.Current.CancellationToken);

        TestContext.Current.TestOutputHelper!.WriteLine($"GPU: {gpu?.ToString() ?? "(nenhuma)"} em {stopwatch.ElapsedMilliseconds} ms");
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        if (gpu is not null)
        {
            gpu.Name.ShouldNotBeNullOrWhiteSpace();
            gpu.Index.ShouldBeGreaterThanOrEqualTo(0);
        }
    }
}
