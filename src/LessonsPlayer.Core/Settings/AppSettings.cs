using LessonsPlayer.Core.Transcription;

namespace LessonsPlayer.Core.Settings;

/// <summary>Preferências persistidas entre execuções.</summary>
public sealed record AppSettings
{
    public static AppSettings Default { get; } = new();

    public string ModelId { get; init; } = WhisperModels.Default.Id;

    public string Language { get; init; } = TranscriptionLanguages.Auto;

    public float Volume { get; init; } = 0.8f;

    public string? LastFolder { get; init; }
}
