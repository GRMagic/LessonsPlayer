using LessonsPlayer.Core.Transcription;

namespace LessonsPlayer.Core.Abstractions;

/// <summary>Armazena os modelos Whisper localmente, baixando-os quando necessário.</summary>
public interface IModelStore
{
    bool IsDownloaded(WhisperModel model);

    /// <summary>Garante que o modelo está no disco e retorna o caminho. Reporta o progresso do download (0 a 1).</summary>
    Task<string> EnsureDownloadedAsync(WhisperModel model, IProgress<double>? progress, CancellationToken cancellationToken);
}
