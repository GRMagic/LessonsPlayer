using LessonsPlayer.Core.Transcription;

namespace LessonsPlayer.Core.Abstractions;

/// <summary>Procura a placa de vídeo que a transcrição deve usar.</summary>
public interface IGpuDetector
{
    /// <summary>A GPU para a transcrição, ou <c>null</c> se não houver uma utilizável (aí a CPU é usada). Não lança.</summary>
    Task<GpuDevice?> FindGpuAsync(CancellationToken cancellationToken);
}
