using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Transcription;

namespace LessonsPlayer.Core.Abstractions;

/// <summary>Parâmetros do motor de transcrição.</summary>
/// <param name="ModelPath">Arquivo do modelo ggml.</param>
/// <param name="ModelSize">Arquitetura do modelo.</param>
/// <param name="Language">Código do idioma ou <c>auto</c>.</param>
/// <param name="Gpu">A GPU a usar, ou <c>null</c> para a CPU. Se a GPU falhar, o motor usa a CPU.</param>
/// <param name="EnglishOnly">Modelo <c>.en</c> (muda as cabeças de alinhamento do DTW).</param>
/// <param name="Speech">
/// Onde há fala, segundo o detector de voz, ou <c>null</c> se não houver detecção. O motor pode pular os
/// trechos longos sem fala, em que o Whisper costuma inventar texto.
/// </param>
public sealed record TranscriberSettings(
    string ModelPath,
    WhisperModelSize ModelSize,
    string Language,
    GpuDevice? Gpu,
    bool EnglishOnly = false,
    SpeechActivity? Speech = null);

/// <summary>Motor de reconhecimento de fala.</summary>
public interface ITranscriber
{
    /// <summary>
    /// Carrega o modelo, se ainda não estiver carregado, e diz onde ele ficou: a GPU pedida, ou <c>null</c> se
    /// for a CPU, seja porque ela foi pedida, seja porque a GPU falhou.
    /// </summary>
    Task<GpuDevice?> LoadModelAsync(TranscriberSettings settings, CancellationToken cancellationToken);

    /// <summary>Transcreve o áudio, entregando os trechos conforme ficam prontos.</summary>
    IAsyncEnumerable<RecognizedSegment> TranscribeAsync(
        AudioSamples audio, TranscriberSettings settings, CancellationToken cancellationToken);
}
