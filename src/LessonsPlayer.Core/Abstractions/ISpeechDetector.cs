using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Transcription;

namespace LessonsPlayer.Core.Abstractions;

/// <summary>Detector de atividade de voz (VAD): encontra os trechos do áudio com fala.</summary>
public interface ISpeechDetector
{
    /// <summary>
    /// Detecta onde há fala. Retorna <c>null</c> se a detecção não estiver disponível (modelo ausente, falha
    /// nativa): nesse caso a legenda é gerada do jeito antigo, sem pular nem filtrar nada.
    /// </summary>
    Task<SpeechActivity?> DetectAsync(AudioSamples audio, CancellationToken cancellationToken);
}
