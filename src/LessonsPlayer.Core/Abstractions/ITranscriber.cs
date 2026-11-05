using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Transcription;

namespace LessonsPlayer.Core.Abstractions;

/// <summary>Parâmetros do motor de transcrição.</summary>
public sealed record TranscriberSettings(string ModelPath, WhisperModelSize ModelSize, string Language, bool UseGpu);

/// <summary>Motor de reconhecimento de fala.</summary>
public interface ITranscriber
{
    /// <summary>Transcreve o áudio, entregando os trechos conforme ficam prontos.</summary>
    IAsyncEnumerable<RecognizedSegment> TranscribeAsync(
        AudioSamples audio, TranscriberSettings settings, CancellationToken cancellationToken);
}
