using LessonsPlayer.Core.Audio;

namespace LessonsPlayer.Core.Abstractions;

/// <summary>Decodifica um arquivo de áudio para o formato exigido pelo reconhecimento de fala.</summary>
public interface IAudioDecoder
{
    /// <summary>Retorna amostras mono em <see cref="AudioSamples.SpeechSampleRate"/> Hz.</summary>
    Task<AudioSamples> DecodeForSpeechAsync(string filePath, CancellationToken cancellationToken);
}
