namespace LessonsPlayer.Core.Transcription;

/// <summary>
/// Idiomas oferecidos na interface (códigos ISO 639-1 aceitos pelo Whisper).
/// Ficam de fora idiomas escritos sem espaço entre palavras (ex.: japonês), que a divisão em palavras não trata.
/// </summary>
public static class TranscriptionLanguages
{
    /// <summary>Deixa o Whisper detectar o idioma.</summary>
    public const string Auto = "auto";

    public static IReadOnlyList<string> All { get; } = [Auto, "pt", "en", "es", "fr", "de", "it"];

    public static string NormalizeOrAuto(string? code) =>
        All.FirstOrDefault(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)) ?? Auto;
}
