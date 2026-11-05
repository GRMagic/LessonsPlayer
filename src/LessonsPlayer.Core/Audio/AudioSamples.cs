namespace LessonsPlayer.Core.Audio;

/// <summary>Amostras PCM float mono, prontas para o reconhecimento de fala.</summary>
public sealed record AudioSamples(ReadOnlyMemory<float> Samples, int SampleRate)
{
    /// <summary>Taxa exigida pelo Whisper.</summary>
    public const int SpeechSampleRate = 16_000;

    public TimeSpan Duration => SampleRate > 0
        ? TimeSpan.FromSeconds((double)Samples.Length / SampleRate)
        : TimeSpan.Zero;
}
