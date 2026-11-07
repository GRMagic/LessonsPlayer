using System.Runtime.InteropServices;
using System.Text;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Transcription;
using Microsoft.Extensions.Logging;

namespace LessonsPlayer.Infrastructure.Whisper;

/// <summary>
/// Escolhe a GPU para a transcrição entre os dispositivos Vulkan, com o índice que o Whisper.net usa
/// (<c>GpuDevice</c>).
/// <para>
/// O ggml-vulkan do Whisper.net 1.9.1 lista todos os dispositivos na ordem do driver, e o Whisper usa o 0. Num
/// notebook com Intel Iris Xe e GTX 1650, o 0 é a Intel (visto no log nativo:
/// <c>0 = Intel(R) Iris(R) Xe Graphics</c>, <c>1 = NVIDIA GeForce GTX 1650</c>, <c>using Vulkan0 backend</c>).
/// Medido com o small em 90 s de uma aula real, com o mesmo texto nos dois: Intel 10,0 s, NVIDIA 4,7 s. A primeira
/// execução na NVIDIA levou 14,7 s, compilando os shaders, que o driver guarda em cache.
/// </para>
/// <list type="bullet">
/// <item>Prefere a placa dedicada. Sem ela, usa o primeiro dispositivo restante; a GPU integrada (Intel, AMD)
/// também serve.</item>
/// <item>Exige Vulkan 1.2 no carregador e no dispositivo, como o ggml-vulkan ("Vulkan 1.2 required").</item>
/// <item>Ignora dispositivos do tipo CPU, como os rasterizadores em software.</item>
/// <item>O índice é a posição na lista do driver (<c>vkEnumeratePhysicalDevices</c>), que é a ordem do ggml-vulkan.</item>
/// </list>
/// Sem o <c>vulkan-1.dll</c> (nenhum driver com Vulkan), não há GPU, e a transcrição usa a CPU.
/// </summary>
public sealed partial class VulkanGpuDetector(ILogger<VulkanGpuDetector> logger) : IGpuDetector
{
    private static readonly uint RequiredApiVersion = VulkanDevice.MakeApiVersion(1, 2);

    // Deslocamentos em VkPhysicalDeviceProperties: apiVersion, driverVersion, vendorID, deviceID, deviceType, deviceName[256].
    private const int ApiVersionOffset = 0;
    private const int DeviceTypeOffset = 16;
    private const int DeviceNameOffset = 20;
    private const int DeviceNameLength = 256;

    /// <summary>Maior que <c>sizeof(VkPhysicalDeviceProperties)</c> (824 bytes no x64).</summary>
    private const int PropertiesBufferSize = 2048;

    private const int StructureTypeInstanceCreateInfo = 1;

    public Task<GpuDevice?> FindGpuAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            var devices = EnumerateDevices();
            var gpu = Choose(devices);
            if (logger.IsEnabled(LogLevel.Information))
            {
                var chosen = gpu is null ? "(nenhuma)" : $"{gpu.Name} (dispositivo {gpu.Index})";
                var all = devices.Count == 0 ? "(nenhum)" : string.Join("; ", devices);
                LogDetected(logger, chosen, all);
            }

            return gpu;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // Sem driver Vulkan, ou um carregador anterior ao 1.1 (sem vkEnumerateInstanceVersion).
            LogNoVulkan(logger, ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Driver com problema: a transcrição segue na CPU em vez de falhar.
            LogDetectionFailed(logger, ex);
            return null;
        }
    }, cancellationToken);

    /// <summary>
    /// A primeira placa dedicada; senão, o primeiro dispositivo que sobra. Os dispositivos vêm na ordem do driver,
    /// e o índice devolvido é a posição na lista inteira (a numeração do ggml-vulkan).
    /// </summary>
    public static GpuDevice? Choose(IReadOnlyList<VulkanDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var usable = devices
            .Select((device, index) => (Device: device, Index: index))
            .Where(x => x.Device.Type != VulkanDeviceType.Cpu && x.Device.ApiVersion >= RequiredApiVersion)
            .ToList();
        var chosen = usable.Where(x => x.Device.Type == VulkanDeviceType.DiscreteGpu).Concat(usable).FirstOrDefault();
        return chosen.Device is null ? null : new GpuDevice(chosen.Device.Name, chosen.Index);
    }

    private static List<VulkanDevice> EnumerateDevices()
    {
        if (Native.EnumerateInstanceVersion(out var instanceVersion) != 0 || instanceVersion < RequiredApiVersion)
        {
            return [];
        }

        var createInfo = new InstanceCreateInfo { SType = StructureTypeInstanceCreateInfo };
        if (Native.CreateInstance(ref createInfo, IntPtr.Zero, out var instance) != 0)
        {
            return [];
        }

        var properties = Marshal.AllocHGlobal(PropertiesBufferSize);
        try
        {
            uint count = 0;
            if (Native.EnumeratePhysicalDevices(instance, ref count, null) != 0 || count == 0)
            {
                return [];
            }

            var handles = new IntPtr[count];
            if (Native.EnumeratePhysicalDevices(instance, ref count, handles) != 0)
            {
                return [];
            }

            var devices = new List<VulkanDevice>((int)count);
            foreach (var handle in handles.Take((int)count))
            {
                Native.GetPhysicalDeviceProperties(handle, properties);
                devices.Add(new VulkanDevice(
                    ReadName(properties + DeviceNameOffset),
                    (VulkanDeviceType)Marshal.ReadInt32(properties, DeviceTypeOffset),
                    unchecked((uint)Marshal.ReadInt32(properties, ApiVersionOffset))));
            }

            return devices;
        }
        finally
        {
            Marshal.FreeHGlobal(properties);
            Native.DestroyInstance(instance, IntPtr.Zero);
        }
    }

    private static string ReadName(IntPtr name)
    {
        var bytes = new byte[DeviceNameLength];
        Marshal.Copy(name, bytes, 0, bytes.Length);
        var length = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length).Trim();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceCreateInfo
    {
        public int SType;
        public IntPtr PNext;
        public uint Flags;
        public IntPtr PApplicationInfo;
        public uint EnabledLayerCount;
        public IntPtr PpEnabledLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr PpEnabledExtensionNames;
    }

    private static class Native
    {
        private const string Library = "vulkan-1.dll";

        [DllImport(Library, EntryPoint = "vkEnumerateInstanceVersion")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int EnumerateInstanceVersion(out uint apiVersion);

        [DllImport(Library, EntryPoint = "vkCreateInstance")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CreateInstance(ref InstanceCreateInfo createInfo, IntPtr allocator, out IntPtr instance);

        [DllImport(Library, EntryPoint = "vkEnumeratePhysicalDevices")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int EnumeratePhysicalDevices(IntPtr instance, ref uint count, [In, Out] IntPtr[]? devices);

        [DllImport(Library, EntryPoint = "vkGetPhysicalDeviceProperties")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern void GetPhysicalDeviceProperties(IntPtr physicalDevice, IntPtr properties);

        [DllImport(Library, EntryPoint = "vkDestroyInstance")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern void DestroyInstance(IntPtr instance, IntPtr allocator);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "GPU para a transcrição: {Gpu}. Dispositivos Vulkan: {Devices}")]
    private static partial void LogDetected(ILogger logger, string gpu, string devices);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sem Vulkan ({Reason}): a transcrição usa a CPU")]
    private static partial void LogNoVulkan(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao procurar a GPU: a transcrição usa a CPU")]
    private static partial void LogDetectionFailed(ILogger logger, Exception exception);
}
