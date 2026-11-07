namespace LessonsPlayer.Infrastructure.Whisper;

/// <summary>Valores de <c>VkPhysicalDeviceType</c>.</summary>
public enum VulkanDeviceType
{
    Other = 0,
    IntegratedGpu = 1,
    DiscreteGpu = 2,
    VirtualGpu = 3,
    Cpu = 4,
}

/// <summary>Um dispositivo Vulkan como o driver o descreve.</summary>
/// <param name="Name">Nome mostrado pelo driver (ex.: "NVIDIA GeForce GTX 1650").</param>
/// <param name="Type">Placa dedicada, integrada, virtual ou CPU.</param>
/// <param name="ApiVersion">Versão do Vulkan suportada, no formato de <c>VK_MAKE_API_VERSION</c>.</param>
public sealed record VulkanDevice(string Name, VulkanDeviceType Type, uint ApiVersion)
{
    public static uint MakeApiVersion(uint major, uint minor) => (major << 22) | (minor << 12);

    public override string ToString() => $"{Name} ({Type}, Vulkan {ApiVersion >> 22}.{(ApiVersion >> 12) & 0x3FF})";
}
