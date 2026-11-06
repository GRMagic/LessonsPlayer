using Whisper.net.LibraryLoader;

namespace LessonsPlayer.Infrastructure.Whisper;

/// <summary>Configuração global do runtime nativo do Whisper.net, comum ao transcritor e ao detector de voz.</summary>
internal static class WhisperRuntime
{
    private static readonly Lock ConfigurationLock = new();
    private static bool _configured;

    /// <summary>
    /// Ordem de carregamento dos runtimes nativos: Vulkan → CPU. Precisa ser definida antes do primeiro uso da
    /// biblioteca nativa, seja pela transcrição ou pelo VAD.
    /// </summary>
    public static void EnsureConfigured()
    {
        lock (ConfigurationLock)
        {
            if (_configured)
            {
                return;
            }

            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
            _configured = true;
        }
    }
}
